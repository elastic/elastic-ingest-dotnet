// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System.Diagnostics;
using OpenTelemetry;

namespace Elastic.Ingest.OpenTelemetry
{
	public class CustomActivityProcessor : BatchActivityExportProcessor
	{
		public CustomActivityProcessor(
			BaseExporter<Activity> exporter,
			int maxQueueSize = 2048,
			int scheduledDelayMilliseconds = 5000,
			int exporterTimeoutMilliseconds = 30000,
			int maxExportBatchSize = 512
		)
			: base(exporter, maxQueueSize, scheduledDelayMilliseconds, exporterTimeoutMilliseconds, maxExportBatchSize)
		{
			Activity.DefaultIdFormat = ActivityIdFormat.W3C;
			Activity.ForceDefaultIdFormat = true;
		}

		public void Add(Activity a) => OnExport(a);

	}
}
