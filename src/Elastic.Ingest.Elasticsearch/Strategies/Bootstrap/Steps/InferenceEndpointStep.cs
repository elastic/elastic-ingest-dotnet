// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that creates ELSER inference endpoints.
/// </summary>
public class InferenceEndpointStep : IBootstrapStep
{
	private readonly string _inferenceId;
	private readonly int _numThreads;
	private readonly bool _usePreexisting;
	private readonly TimeSpan? _createTimeout;

	/// <summary>
	/// Creates a new inference endpoint step.
	/// </summary>
	public InferenceEndpointStep(string inferenceId, int numThreads = 1, bool usePreexisting = false, TimeSpan? createTimeout = null)
	{
		_inferenceId = inferenceId;
		_numThreads = numThreads;
		_usePreexisting = usePreexisting;
		_createTimeout = createTimeout;
	}

	/// <inheritdoc />
	public string Name => $"InferenceEndpoint({_inferenceId})";

	/// <inheritdoc />
	public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		var exists = await context.Transport.GetAsync<VoidResponse>($"_inference/sparse_embedding/{_inferenceId}", ctx).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200 && _usePreexisting)
			return true;

		if (exists.ApiCallDetails.HttpStatusCode != 200 && !_usePreexisting)
			return await CreateElserInferenceAsync(context, ctx).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"Expected inference id {_inferenceId} to exist: {exists}",
				exists.ApiCallDetails.OriginalException);
	}

	/// <inheritdoc />
	public bool Execute(BootstrapContext context)
	{
		var exists = context.Transport.Get<VoidResponse>($"_inference/sparse_embedding/{_inferenceId}");

		if (exists.ApiCallDetails.HttpStatusCode == 200 && _usePreexisting)
			return true;

		if (exists.ApiCallDetails.HttpStatusCode != 200 && !_usePreexisting)
			return CreateElserInference(context);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"Expected inference id {_inferenceId} to exist: {exists}",
				exists.ApiCallDetails.OriginalException);
	}

	private async Task<bool> CreateElserInferenceAsync(BootstrapContext context, CancellationToken ctx)
	{
		var data = ElserInferenceEndpointJson();
		var timeout = _createTimeout ?? RequestConfiguration.DefaultRequestTimeout;
		var response = await context.Transport.PutAsync<StringResponse>($"_inference/sparse_embedding/{_inferenceId}", PostData.String(data), timeout, ctx)
			.ConfigureAwait(false);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"Failure creating inference id {_inferenceId}: {response}",
				response.ApiCallDetails.OriginalException);
	}

	private bool CreateElserInference(BootstrapContext context)
	{
		var data = ElserInferenceEndpointJson();
		var timeout = _createTimeout ?? RequestConfiguration.DefaultRequestTimeout;
		var response = context.Transport.Put<StringResponse>($"_inference/sparse_embedding/{_inferenceId}", PostData.String(data), timeout);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"Failure creating inference id {_inferenceId}: {response}",
				response.ApiCallDetails.OriginalException);
	}

	// language=json
	private string ElserInferenceEndpointJson() =>
		$$"""
		{
		  "service": "elser",
		  "service_settings": {
		    "adaptive_allocations": {
		      "enabled": true,
		      "min_number_of_allocations": 3,
		      "max_number_of_allocations": 10
		    },
		    "num_threads": {{_numThreads:N0}}
		  }
		}
		""";
}
