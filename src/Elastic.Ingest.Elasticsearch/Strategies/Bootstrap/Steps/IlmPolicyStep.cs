// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that creates an ILM (Index Lifecycle Management) policy.
/// Skipped on serverless Elasticsearch which does not support ILM.
/// Should be ordered before <see cref="ComponentTemplateStep"/> in the pipeline.
/// </summary>
public class IlmPolicyStep : IBootstrapStep
{
	private readonly string _policyName;
	private readonly string _policyJson;

	/// <summary>
	/// Creates an ILM policy step with full control over the policy JSON body.
	/// </summary>
	/// <param name="policyName">The ILM policy name.</param>
	/// <param name="policyJson">The full ILM policy JSON body (the value of the <c>"policy"</c> key).</param>
	public IlmPolicyStep(string policyName, string policyJson)
	{
		_policyName = policyName ?? throw new ArgumentNullException(nameof(policyName));
		_policyJson = policyJson ?? throw new ArgumentNullException(nameof(policyJson));
	}

	/// <summary>
	/// Creates an ILM policy step with convenience parameters for common lifecycle settings.
	/// </summary>
	/// <param name="policyName">The ILM policy name.</param>
	/// <param name="hotMaxAge">Maximum age before rolling over (e.g. "30d"). Null to skip hot phase rollover.</param>
	/// <param name="deleteMinAge">Minimum age before deleting (e.g. "90d"). Null to skip delete phase.</param>
	public IlmPolicyStep(string policyName, string? hotMaxAge, string? deleteMinAge)
	{
		_policyName = policyName ?? throw new ArgumentNullException(nameof(policyName));
		_policyJson = BuildPolicyJson(hotMaxAge, deleteMinAge);
	}

	/// <inheritdoc />
	public string Name => $"IlmPolicy({_policyName})";

	/// <inheritdoc />
	public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		if (context.IsServerless)
			return true;

		var exists = await PolicyExistsAsync(context.Transport, _policyName, ctx).ConfigureAwait(false);
		if (exists)
			return true;

		return await PutPolicyAsync(context, ctx).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public bool Execute(BootstrapContext context)
	{
		if (context.IsServerless)
			return true;

		var exists = PolicyExists(context.Transport, _policyName);
		if (exists)
			return true;

		return PutPolicy(context);
	}

	private static async Task<bool> PolicyExistsAsync(ITransport transport, string name, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_ilm/policy/{name}", cancellationToken: ctx
		).ConfigureAwait(false);
		return response.ApiCallDetails.HttpStatusCode is 200;
	}

	private static bool PolicyExists(ITransport transport, string name)
	{
		var response = transport.Request<StringResponse>(HttpMethod.GET, $"_ilm/policy/{name}");
		return response.ApiCallDetails.HttpStatusCode is 200;
	}

	private async Task<bool> PutPolicyAsync(BootstrapContext context, CancellationToken ctx)
	{
		var body = $@"{{ ""policy"": {_policyJson} }}";
		var response = await context.Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_ilm/policy/{_policyName}", PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create ILM policy `{_policyName}`: {response}",
				response.ApiCallDetails.OriginalException
			);
	}

	private bool PutPolicy(BootstrapContext context)
	{
		var body = $@"{{ ""policy"": {_policyJson} }}";
		var response = context.Transport.Request<StringResponse>(
			HttpMethod.PUT, $"_ilm/policy/{_policyName}", PostData.String(body)
		);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create ILM policy `{_policyName}`: {response}",
				response.ApiCallDetails.OriginalException
			);
	}

	private static string BuildPolicyJson(string? hotMaxAge, string? deleteMinAge)
	{
		var hotPhase = hotMaxAge != null
			? $@"""hot"": {{ ""actions"": {{ ""rollover"": {{ ""max_age"": ""{hotMaxAge}"" }} }} }}"
			: @"""hot"": { ""actions"": { ""rollover"": { ""max_age"": ""30d"" } } }";

		var deletePhase = deleteMinAge != null
			? $@", ""delete"": {{ ""min_age"": ""{deleteMinAge}"", ""actions"": {{ ""delete"": {{}} }} }}"
			: "";

		return $@"{{ ""phases"": {{ {hotPhase}{deletePhase} }} }}";
	}
}
