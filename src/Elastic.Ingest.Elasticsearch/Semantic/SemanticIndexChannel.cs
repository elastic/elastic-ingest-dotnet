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
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
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

	/// A function that returns the mapping settings for <typeparamref name="TDocument"/> given the inference id and search inference id.
	public Func<string, string, string>? GetMappingSettings { get; init; }

	/// The timeout for creating the inference id.
	/// <para>If not specified, the default timeout is used.</para>
	/// <para>If specified, the timeout is used for both the inference id and search inference id.</para>
	public TimeSpan? InferenceCreateTimeout { get; init; }

	/// <summary> Set this to true before calling <see cref="IngestChannelBase{TEvent,TChannelOptions}.BootstrapElasticsearchAsync"/> to attempt to reuse an existing index.
	/// <para> Setting this to true does not force the behavior the <see cref="IngestChannelBase{TDocument, TChannelOptions}.ChannelHash"/> should also match the hash stored in the index template _meta</para>
	/// </summary>
	public bool TryReuseIndex { get; set; }
}

/// A channel that writes to an index which allows you to bootstrap inference endpoints <see cref="BootstrapElasticsearch"/> in a controlled fashion.
public class SemanticIndexChannel<TDocument> : CatalogIndexChannel<TDocument, SemanticIndexChannelOptions<TDocument>>
	where TDocument : class
{
	private readonly InferenceEndpointStep _inferenceStep;
	private readonly InferenceEndpointStep _searchInferenceStep;

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

		if (options.ScriptedHashBulkUpsertLookup is not null)
			throw new ArgumentException("SemanticIndexChannel does not support ScriptedHashBulkUpsertLookup");

		_inferenceStep = new InferenceEndpointStep(InferenceId, Options.IndexNumThreads, Options.UsePreexistingInferenceIds, Options.InferenceCreateTimeout);
		_searchInferenceStep = new InferenceEndpointStep(SearchInferenceId, Options.SearchNumThreads, Options.UsePreexistingInferenceIds, Options.InferenceCreateTimeout);
	}

	/// The inference id used, either explicitly passed <see cref="SemanticIndexChannelOptions{TDocument}.InferenceId"/> or a precomputed one based on <typeparamref name="TDocument"/>
	public string InferenceId { get; }

	/// The search inference id used, either explicitly passed <see cref="SemanticIndexChannelOptions{TDocument}.InferenceId"/> or a precomputed one based on <typeparamref name="TDocument"/>
	public string SearchInferenceId { get; }

	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}.AlwaysBootstrapComponentTemplates"/>
	protected override bool AlwaysBootstrapComponentTemplates => true;

	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}.GetMappings"/>>
	protected override string? GetMappings() => Options.GetMapping?.Invoke(InferenceId, SearchInferenceId);

	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}.GetMappingSettings"/>
	protected override string? GetMappingSettings() => Options.GetMappingSettings?.Invoke(InferenceId, SearchInferenceId);

	/// <summary>
	/// Returns whether the channel will attempt to reuse an existing index. Only true if <see cref="SemanticIndexChannelOptions{TDocument}.TryReuseIndex"/> is specified.
	/// <para> If this returns true it does not guarantee reuse, the <see cref="IngestChannelBase{TDocument, TChannelOptions}.ChannelHash"/> should still match the hash stored in the index template _meta</para>
	/// </summary>
	public override bool TryReuseIndex => Options.TryReuseIndex;

	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}.BootstrapElasticsearch"/>
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod)
	{
		var context = CreateInferenceBootstrapContext(bootstrapMethod);

		if (!_inferenceStep.Execute(context))
			return false;

		if (!_searchInferenceStep.Execute(context))
			return false;

		return base.BootstrapElasticsearch(bootstrapMethod);
	}

	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}.BootstrapElasticsearchAsync"/>
	public override async Task<bool> BootstrapElasticsearchAsync(BootstrapMethod bootstrapMethod, CancellationToken ctx = default)
	{
		var context = CreateInferenceBootstrapContext(bootstrapMethod);

		if (!await _inferenceStep.ExecuteAsync(context, ctx).ConfigureAwait(false))
			return false;

		if (!await _searchInferenceStep.ExecuteAsync(context, ctx).ConfigureAwait(false))
			return false;

		return await base.BootstrapElasticsearchAsync(bootstrapMethod, ctx).ConfigureAwait(false);
	}

	private BootstrapContext CreateInferenceBootstrapContext(BootstrapMethod bootstrapMethod) => new()
	{
		Transport = Options.Transport,
		BootstrapMethod = bootstrapMethod,
		TemplateName = TemplateName,
		TemplateWildcard = TemplateWildcard
	};
}
