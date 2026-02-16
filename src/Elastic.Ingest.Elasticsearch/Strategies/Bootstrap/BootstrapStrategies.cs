// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Factory methods for creating common <see cref="IBootstrapStrategy"/> configurations.
/// </summary>
public static class BootstrapStrategies
{
	/// <summary>
	/// Creates a data stream bootstrap strategy with component template and data stream template steps.
	/// </summary>
	public static IBootstrapStrategy DataStream() =>
		new DefaultBootstrapStrategy(new ComponentTemplateStep(), new DataStreamTemplateStep());

	/// <summary>
	/// Creates a data stream bootstrap strategy with data stream lifecycle retention.
	/// </summary>
	public static IBootstrapStrategy DataStream(string retention) =>
		new DefaultBootstrapStrategy(
			new ComponentTemplateStep(),
			new DataStreamLifecycleStep(retention),
			new DataStreamTemplateStep());

	/// <summary>
	/// Creates a data stream bootstrap strategy with ILM policy.
	/// </summary>
	public static IBootstrapStrategy DataStreamWithIlm(string ilmPolicyName, string? hotMaxAge = null, string? deleteMinAge = null) =>
		new DefaultBootstrapStrategy(
			new IlmPolicyStep(ilmPolicyName, hotMaxAge, deleteMinAge),
			new ComponentTemplateStep(ilmPolicyName),
			new DataStreamTemplateStep());

	/// <summary>
	/// Creates an index bootstrap strategy with component template and index template steps.
	/// </summary>
	public static IBootstrapStrategy Index() =>
		new DefaultBootstrapStrategy(new ComponentTemplateStep(), new IndexTemplateStep());

	/// <summary>
	/// Creates an index bootstrap strategy with ILM policy.
	/// </summary>
	public static IBootstrapStrategy IndexWithIlm(string ilmPolicyName, string? hotMaxAge = null, string? deleteMinAge = null) =>
		new DefaultBootstrapStrategy(
			new IlmPolicyStep(ilmPolicyName, hotMaxAge, deleteMinAge),
			new ComponentTemplateStep(ilmPolicyName),
			new IndexTemplateStep());

	/// <summary>
	/// Creates a no-op bootstrap strategy (e.g., for WiredStreams where bootstrap is managed by Elasticsearch).
	/// </summary>
	public static IBootstrapStrategy None() =>
		new DefaultBootstrapStrategy(new NoopBootstrapStep());
}
