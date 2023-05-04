// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System.Diagnostics;
using OpenTelemetry;

namespace Elastic.Ingest.OpenTelemetry;

/// <summary> </summary>
public class CustomActivityProcessor : BatchActivityExportProcessor
{
	/// <summary> </summary>
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

	/// <summary> </summary>
	public void Add(Activity a) => OnExport(a);

}
