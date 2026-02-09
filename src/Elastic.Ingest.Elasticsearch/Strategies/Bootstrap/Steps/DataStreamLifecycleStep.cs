// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that configures Data Stream Lifecycle (DSL) -- the serverless-compatible
/// alternative to ILM. Stores the retention period in <see cref="BootstrapContext.DataStreamLifecycleRetention"/>
/// for <see cref="DataStreamTemplateStep"/> to embed in the index template.
/// <para>Should be ordered before <see cref="DataStreamTemplateStep"/> in the pipeline.</para>
/// </summary>
public class DataStreamLifecycleStep : IBootstrapStep
{
	private readonly string _dataRetention;

	/// <summary>
	/// Creates a new Data Stream Lifecycle step.
	/// </summary>
	/// <param name="dataRetention">The data retention period (e.g. "30d", "7d", "90d").</param>
	public DataStreamLifecycleStep(string dataRetention) =>
		_dataRetention = dataRetention ?? throw new ArgumentNullException(nameof(dataRetention));

	/// <inheritdoc />
	public string Name => "DataStreamLifecycle";

	/// <inheritdoc />
	public Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		StoreRetention(context);
		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public bool Execute(BootstrapContext context)
	{
		StoreRetention(context);
		return true;
	}

	private void StoreRetention(BootstrapContext context)
	{
		context.Properties ??= new Dictionary<string, object>();
		context.Properties["data_stream_lifecycle_retention"] = _dataRetention;
	}
}
