// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Apm.Model;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Apm;

/// <summary>
/// An <see cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/> implementation that sends V2 intake API data
/// to APM server.
/// </summary>
public class ApmChannel : TransportChannelBase<ApmChannelOptions, IIntakeObject, EventIntakeResponse, IntakeErrorItem>
{
/// <inheritdoc cref="ApmChannel"/>
	public ApmChannel(ApmChannelOptions options) : base(options) { }

	//retry if APM server returns 429
	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Retry"/>
	protected override bool Retry(EventIntakeResponse response) => response.ApiCallDetails.HttpStatusCode == 429;

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryAllItems"/>
	protected override bool RetryAllItems(EventIntakeResponse response) => response.ApiCallDetails.HttpStatusCode == 429;

	//APM does not return the status for all events sent. Therefor we always return an empty set for individual items to retry
	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Zip"/>
	protected override List<(IIntakeObject, IntakeErrorItem)> Zip(EventIntakeResponse response, IReadOnlyCollection<IIntakeObject> page) =>
		_emptyZip;

	private readonly List<(IIntakeObject, IntakeErrorItem)> _emptyZip = new();

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryEvent"/>
	protected override bool RetryEvent((IIntakeObject, IntakeErrorItem) @event) => false;

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RejectEvent"/>
	protected override bool RejectEvent((IIntakeObject, IntakeErrorItem) @event) => false;

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/>
	protected override Task<EventIntakeResponse> ExportAsync(ITransport transport, ArraySegment<IIntakeObject> page, CancellationToken ctx = default) =>
		transport.RequestAsync<EventIntakeResponse>(new EndpointPath(HttpMethod.POST, "/intake/v2/events"),
			PostData.StreamHandler(page,
				(_, _) =>
				{
					/* NOT USED */
				},
				async (b, stream, ctx) => { await WriteBufferToStreamAsync(b, stream, ctx).ConfigureAwait(false); })
			, default, ApmChannelStatics.RequestConfig, ctx);

	private async Task WriteStanzaToStreamAsync(Stream stream, CancellationToken ctx)
	{
		// {"metadata":{"process":{"pid":1234,"title":"/usr/lib/jvm/java-10-openjdk-amd64/bin/java","ppid":1,"argv":["-v"]},
		// "system":{"architecture":"amd64","detected_hostname":"8ec7ceb99074","configured_hostname":"host1","platform":"Linux","container":{"id":"8ec7ceb990749e79b37f6dc6cd3628633618d6ce412553a552a0fa6b69419ad4"},
		// "kubernetes":{"namespace":"default","pod":{"uid":"b17f231da0ad128dc6c6c0b2e82f6f303d3893e3","name":"instrumented-java-service"},"node":{"name":"node-name"}}},
		// "service":{"name":"1234_service-12a3","version":"4.3.0","node":{"configured_name":"8ec7ceb990749e79b37f6dc6cd3628633618d6ce412553a552a0fa6b69419ad4"},"environment":"production","language":{"name":"Java","version":"10.0.2"},
		// "agent":{"version":"1.10.0","name":"java","ephemeral_id":"e71be9ac-93b0-44b9-a997-5638f6ccfc36"},"framework":{"name":"spring","version":"5.0.0"},"runtime":{"name":"Java","version":"10.0.2"}},"labels":{"group":"experimental","ab_testing":true,"segment":5}}}
		// TODO cache
		var p = Process.GetCurrentProcess();
		var metadata = new
		{
			metadata = new
			{
				process = new { pid = p.Id, title = p.ProcessName },
				service = new
				{
					name = System.Text.RegularExpressions.Regex.Replace(p.ProcessName, "[^a-zA-Z0-9 _-]", "_"),
					version = "1.0.0",
					agent = new { name = "dotnet", version = "0.0.1" }
				}
			}
		};
		await JsonSerializer.SerializeAsync(stream, metadata, metadata.GetType(), ApmChannelStatics.SerializerOptions, ctx)
			.ConfigureAwait(false);
		await stream.WriteAsync(ApmChannelStatics.LineFeed, 0, 1, ctx).ConfigureAwait(false);
	}

	private async Task WriteBufferToStreamAsync(IReadOnlyCollection<IIntakeObject> b, Stream stream, CancellationToken ctx)
	{
		await WriteStanzaToStreamAsync(stream, ctx).ConfigureAwait(false);
		foreach (var @event in b)
		{
			if (@event == null) continue;

			var type = @event switch
			{
				Transaction _ => "transaction",
				_ => "unknown"
			};
			var dictionary = new Dictionary<string, object>() { { type, @event } };

			await JsonSerializer.SerializeAsync(stream, dictionary, dictionary.GetType(), ApmChannelStatics.SerializerOptions, ctx)
				.ConfigureAwait(false);

			await stream.WriteAsync(ApmChannelStatics.LineFeed, 0, 1, ctx).ConfigureAwait(false);
		}
	}
}
