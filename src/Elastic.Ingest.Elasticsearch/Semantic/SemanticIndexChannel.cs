// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.Catalog;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Semantic;

/// Options for configuring a semantic index channel that handles semantic search and inference operations.
public class SemanticIndexChannelOptions<TDocument>(ITransport transport) : CatalogIndexChannelOptionsBase<TDocument>(transport)
{
	/// Assume <see cref="InferenceId"/> and optionally <see cref="SearchInferenceId"/> are pre-existing and do not need to be created.
	public bool UsePreexistingInferenceIds { get; init; }

	/// <inheritdoc cref="IndexChannelOptions{TDocument}.IndexFormat"/>
	public override string IndexFormat { get; set; } = $"{typeof(TDocument).Name.ToLowerInvariant()}-{{0:yyyy.MM.dd.HHmmss}}";

	/// Name of the inference id to be created or use depending on whether <see cref="UsePreexistingInferenceIds"/> is specified
	public string? InferenceId { get; init; }

	/// Name of the inference id to be created or use depending on whether <see cref="UsePreexistingInferenceIds"/> is specified
	public string? SearchInferenceId { get; init; }

	/// The number of threads the search inference endpoint should use if <see cref="UsePreexistingInferenceIds"/> is not specified.
	/// <para>Should be a multiple of 2 and no more than 32</para>
	public int SearchNumThreads { get; init; } = 2;

	/// The number of threads the index inference endpoint should use if <see cref="UsePreexistingInferenceIds"/> is not specified.
	public int IndexNumThreads { get; init; } = 1;

	/// A function that returns the mapping for <typeparamref name="TDocument"/> given the inference id and search inference id.
	public Func<string, string, string>? GetMapping { get; init; }

	/// The timeout for creating the inference id.
	/// <para>If not specified, the default timeout is used.</para>
	/// <para>If specified, the timeout is used for both the inference id and search inference id.</para>
	public TimeSpan? InferenceCreateTimeout { get; init; }
}

/// A channel that writes to an index which allows you to bootstrap inference endpoints <see cref="BootstrapElasticsearch"/> in a controlled fashion.
public class SemanticIndexChannel<TDocument> : CatalogIndexChannel<TDocument, SemanticIndexChannelOptions<TDocument>>
	where TDocument : class
{
	/// <inheritdoc cref="SemanticIndexChannel{TDocument}"/>
	public SemanticIndexChannel(SemanticIndexChannelOptions<TDocument> options) : this(options, null) { }

	/// <inheritdoc cref="SemanticIndexChannel{TDocument}"/>
	public SemanticIndexChannel(SemanticIndexChannelOptions<TDocument> options, ICollection<IChannelCallbacks<TDocument, BulkResponse>>? callbackListeners)
		: base(options, callbackListeners)
	{
		var type = typeof(TDocument).Name.ToLowerInvariant();
		InferenceId = Options.InferenceId ?? $"{type}-elser";
		if (options.UsePreexistingInferenceIds)
			SearchInferenceId = Options.SearchInferenceId ?? Options.InferenceId
				?? throw new ArgumentException("InferenceId must be set when UsePreexistingInferenceIds is true");
		else
			SearchInferenceId = Options.SearchInferenceId ?? $"{type}-search-elser";
	}

	/// The inference id used, either explicitly passed <see cref="SemanticIndexChannelOptions{TDocument}.InferenceId"/> or a precomputed one based on <typeparamref name="TDocument"/>
	public string InferenceId { get; }

	/// The search inference id used, either explicitly passed <see cref="SemanticIndexChannelOptions{TDocument}.InferenceId"/> or a precomputed one based on <typeparamref name="TDocument"/>
	public string SearchInferenceId { get; }

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.AlwaysBootstrapComponentTemplates"/>
	protected override bool AlwaysBootstrapComponentTemplates => true;

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.GetMappings"/>>
	protected override string? GetMappings() => Options.GetMapping?.Invoke(InferenceId, SearchInferenceId);

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.BootstrapElasticsearch"/>
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod, string? ilmPolicy = null)
	{
		if (!BootstrapInference(bootstrapMethod, InferenceId, 1))
			return false;

		if (!BootstrapInference(bootstrapMethod, SearchInferenceId, Options.SearchNumThreads))
			return false;

		return base.BootstrapElasticsearch(bootstrapMethod, ilmPolicy);
	}

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.BootstrapElasticsearchAsync"/>
	public override async Task<bool> BootstrapElasticsearchAsync(BootstrapMethod bootstrapMethod, string? ilmPolicy = null, CancellationToken ctx = default)
	{
		if (!await BootstrapInferenceAsync(bootstrapMethod, InferenceId, Options.IndexNumThreads, ctx).ConfigureAwait(false))
			return false;

		if (!await BootstrapInferenceAsync(bootstrapMethod, SearchInferenceId, Options.SearchNumThreads, ctx).ConfigureAwait(false))
			return false;

		return await base.BootstrapElasticsearchAsync(bootstrapMethod, ilmPolicy, ctx).ConfigureAwait(false);
	}

	private bool BootstrapInference(BootstrapMethod bootstrapMethod, string inferenceId, int numThreads)
	{
		var inferenceExists = Options.Transport.Get<VoidResponse>($"_inference/sparse_embedding/{inferenceId}");
		if (inferenceExists.ApiCallDetails.HttpStatusCode == 200 && Options.UsePreexistingInferenceIds)
			return true;

		if (inferenceExists.ApiCallDetails.HttpStatusCode != 200 && !Options.UsePreexistingInferenceIds)
			return CreateElserInference(bootstrapMethod, inferenceId, numThreads);

		if (inferenceExists.ApiCallDetails.HttpStatusCode == 200)
			return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"Expected inference id {inferenceId} to exist: {inferenceExists}",
				inferenceExists.ApiCallDetails.OriginalException
			);
	}

	private async Task<bool> BootstrapInferenceAsync(BootstrapMethod bootstrapMethod, string inferenceId, int numThreads, CancellationToken ctx)
	{
		var inferenceExists = await Options.Transport.GetAsync<VoidResponse>($"_inference/sparse_embedding/{inferenceId}", ctx).ConfigureAwait(false);
		if (inferenceExists.ApiCallDetails.HttpStatusCode == 200 && Options.UsePreexistingInferenceIds)
			return true;

		if (inferenceExists.ApiCallDetails.HttpStatusCode != 200 && !Options.UsePreexistingInferenceIds)
			return await CreateElserInferenceAsync(bootstrapMethod, inferenceId, numThreads, ctx).ConfigureAwait(false);

		if (inferenceExists.ApiCallDetails.HttpStatusCode == 200)
			return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"Expected inference id {inferenceId} to exist: {inferenceExists}",
				inferenceExists.ApiCallDetails.OriginalException
			);
	}

	private bool CreateElserInference(BootstrapMethod bootstrapMethod, string inferenceId, int numThreads)
	{
		var data = ElserInferenceEndpointJson(numThreads);
		var timeout = Options.InferenceCreateTimeout ?? RequestConfiguration.DefaultRequestTimeout;
		var putInferenceResponse = Options.Transport.Put<StringResponse>($"_inference/sparse_embedding/{inferenceId}", PostData.String(data), timeout);
		if ( putInferenceResponse.ApiCallDetails.HasSuccessfulStatusCode)
			return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure creating inference id {inferenceId}: {putInferenceResponse}",
				putInferenceResponse.ApiCallDetails.OriginalException
			);
	}

	private async Task<bool> CreateElserInferenceAsync(BootstrapMethod bootstrapMethod, string inferenceId, int numThreads, CancellationToken ctx = default)
	{
		var data = ElserInferenceEndpointJson(numThreads);
		var timeout = Options.InferenceCreateTimeout ?? RequestConfiguration.DefaultRequestTimeout;
		var putInferenceResponse = await Options.Transport.PutAsync<StringResponse>($"_inference/sparse_embedding/{inferenceId}", PostData.String(data), timeout, ctx)
			.ConfigureAwait(false);
		if ( putInferenceResponse.ApiCallDetails.HasSuccessfulStatusCode)
			return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure creating inference id {inferenceId}: {putInferenceResponse}",
				putInferenceResponse.ApiCallDetails.OriginalException
			);
	}

	// language=json
	private static string ElserInferenceEndpointJson(int numThreads) =>
		$$"""
		{
		  "service": "elser",
		  "service_settings": {
		    "adaptive_allocations": {
		      "enabled": true,
		      "min_number_of_allocations": 3,
		      "max_number_of_allocations": 10
		    },
		    "num_threads": {{numThreads:N0}}
		  }
		}
		""";
}
