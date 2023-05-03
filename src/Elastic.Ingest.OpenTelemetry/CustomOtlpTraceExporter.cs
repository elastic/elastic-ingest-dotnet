// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;

namespace Elastic.Ingest.OpenTelemetry
{
	/// <summary> </summary>
	public class CustomOtlpTraceExporter : OtlpTraceExporter
	{
		/// <summary> </summary>
		public CustomOtlpTraceExporter(OtlpExporterOptions options, TraceChannelOptions channelOptions) : base(options)
		{
			var type = GetType();
			var attrbutes = new[] { new KeyValuePair<string, object>("telemetry.sdk.language", "dotnet") };
			var resource = ResourceBuilder.CreateDefault();
				if (!string.IsNullOrWhiteSpace(channelOptions.ServiceName))
					resource.AddService(channelOptions.ServiceName);

			var buildResource = resource.AddAttributes(attrbutes).Build();
			// hack but there is no other way to set a resource without spinning up the world
			// through SDK.
			// internal void SetResource(Resource resource)
			var prop = type.BaseType?.GetMethod("SetResource", BindingFlags.Instance | BindingFlags.NonPublic);
			prop?.Invoke(this, new object?[]{ buildResource });
		}
	}


	/// <summary> </summary>
	public class TraceChannelOptions : ChannelOptionsBase<Activity, TraceExportResult>
	{
		/// <summary> </summary>
		public string? ServiceName { get; set; }
		/// <summary> </summary>
		public Uri? Endpoint { get; set; }
		/// <summary> </summary>
		public string? SecretToken { get; set; }
	}

	/// <summary> </summary>
	public class TraceExportResult
	{
		/// <summary> </summary>
		public ExportResult Result { get; internal set; }
	}

	/// <summary> </summary>
	public class TraceChannel : BufferedChannelBase<TraceChannelOptions, Activity, TraceExportResult>
	{
		/// <summary> </summary>
		public TraceChannel(TraceChannelOptions options) : this(options, null) { }

		/// <summary> </summary>
		public TraceChannel(TraceChannelOptions options, ICollection<IChannelCallbacks<Activity, TraceExportResult>>? callbackListeners)
			: base(options, callbackListeners) {
			var o = new OtlpExporterOptions
			{
				Endpoint = options.Endpoint,
				Headers = $"Authorization=Bearer {options.SecretToken}"
			};
			TraceExporter = new CustomOtlpTraceExporter(o, options);
            Processor = new CustomActivityProcessor(TraceExporter,
				maxExportBatchSize: options.BufferOptions.OutboundBufferMaxSize,
				maxQueueSize: options.BufferOptions.InboundBufferMaxSize,
				scheduledDelayMilliseconds: (int)options.BufferOptions.OutboundBufferMaxLifetime.TotalMilliseconds,
				exporterTimeoutMilliseconds: (int)options.BufferOptions.OutboundBufferMaxLifetime.TotalMilliseconds
			);
			var bufferType = typeof(BaseExporter<>).Assembly.GetTypes().First(t=>t.Name == "CircularBuffer`1");
			var activityBuffer = bufferType.GetGenericTypeDefinition().MakeGenericType(typeof(Activity));
			var bufferTypeConstructor = activityBuffer.GetConstructors().First();
			var bufferAddMethod = bufferType.GetMethod("Add");

			var batchType = typeof(Batch<Activity>);
			var batchConstructor = batchType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).First(c=>c.GetParameters().Length == 2);

			BatchCreator = (page) =>
			{
				var buffer = bufferTypeConstructor.Invoke(new object[] {options.BufferOptions.OutboundBufferMaxSize });
				bufferAddMethod.Invoke(buffer, new[] { page });
				var batch = (Batch<Activity>)batchConstructor.Invoke(new[] {buffer, options.BufferOptions.OutboundBufferMaxSize });
				return batch;
			};

		}

		private Func<IReadOnlyCollection<Activity>, Batch<Activity>> BatchCreator { get; }

		/// <summary> </summary>
		public CustomOtlpTraceExporter TraceExporter { get; }

		/// <summary> </summary>
		public CustomActivityProcessor Processor { get; }

		/// <summary> </summary>
		protected override Task<TraceExportResult> ExportAsync(ArraySegment<Activity> page, CancellationToken ctx = default)
		{
			var batch = BatchCreator(page);
			var result = TraceExporter.Export(batch);
			return Task.FromResult(new TraceExportResult { Result = result });
		}

		/// <summary> </summary>
		public override void Dispose()
		{
			base.Dispose();
			Processor.Dispose();
		}

	}
}
