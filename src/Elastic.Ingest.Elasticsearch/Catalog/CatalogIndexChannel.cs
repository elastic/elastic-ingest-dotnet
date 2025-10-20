// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Catalog;

/// <inheritdoc cref="CatalogIndexChannel{TDocument}" />
public class CatalogIndexChannelOptionsBase<TDocument>(ITransport transport) : IndexChannelOptions<TDocument>(transport)
{
	/// <inheritdoc cref="IndexChannelOptions{TDocument}.IndexFormat"/>
	public override string IndexFormat { get; set; } = $"{typeof(TDocument).Name.ToLowerInvariant()}-{{0:yyyy.MM.dd.HHmmss}}";

	/// The alias name pointing to the version of the index intended to be queries
	/// <para> By calling <see cref="CatalogIndexChannel{TDocument, TChannelOptions}.ApplyAliasesAsync"/> the alias will be updated to point to the latest index.</para>
	public string ActiveSearchAlias { get; set; } = $"{typeof(TDocument).Name.ToLowerInvariant()}";
}

/// <inheritdoc cref="CatalogIndexChannel{TDocument}" />
public class CatalogIndexChannelOptions<TDocument>(ITransport transport) : CatalogIndexChannelOptionsBase<TDocument>(transport)
{
	/// A function that returns the mapping for <typeparamref name="TDocument"/>.
	public Func<string>? GetMapping { get; init; }

	/// A function that returns settings to accompany <see cref="GetMapping"/>.
	public Func<string>? GetMappingSettings { get; init; }

}

/// <inheritdoc cref="CatalogIndexChannel{TDocument}" />
public class CatalogIndexChannel<TDocument> : CatalogIndexChannel<TDocument, CatalogIndexChannelOptions<TDocument>>
	where TDocument : class
{
	/// <inheritdoc cref="CatalogIndexChannel{TDocument}" />
	public CatalogIndexChannel(CatalogIndexChannelOptions<TDocument> options, ICollection<IChannelCallbacks<TDocument, BulkResponse>>? callbackListeners = null)
		: base(options, callbackListeners) { }

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.GetMappings"/>>
	protected override string? GetMappings() => Options.GetMapping?.Invoke();

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.GetMappings"/>>
	/// <inheritdoc />
	protected override string? GetMappingSettings() => Options.GetMappingSettings?.Invoke();
}

/// A channel which is optimized for writing fixed catalog datasets to Elasticsearch.
/// <para>If you have continuous data, use one of the two time-series optimized channels.</para>
/// <para><see cref="DataStreamChannel{TEvent}"/> (preferred)</para>
/// <para><see cref="IndexChannel{TEvent}"/></para>
/// <para></para>
/// <para>Primary use case for this channel to provide controlled aliasing of indices after all data has been written</para>
public class CatalogIndexChannel<TDocument, TChannelOptions> : IndexChannel<TDocument, TChannelOptions>
	where TChannelOptions : CatalogIndexChannelOptionsBase<TDocument>
	where TDocument : class
{
	private string _url;

	/// <inheritdoc cref="CatalogIndexChannel{TDocument}"/>
	public CatalogIndexChannel(TChannelOptions options, ICollection<IChannelCallbacks<TDocument, BulkResponse>>? callbackListeners = null)
		: base(options, callbackListeners)
	{
		var date = DateTimeOffset.UtcNow;
		IndexName = string.Format(Options.IndexFormat, date);

		// If the index format is not a variable format, we can't use it for aliasing.
		if (IndexName.Equals(Options.IndexFormat, StringComparison.Ordinal))
			throw new Exception($"{nameof(Options.IndexFormat)} needs to be variable format accepting {{0}} in order to be used with aliasing");

		if (string.IsNullOrEmpty(Options.ActiveSearchAlias))
			throw new Exception($"{nameof(Options.ActiveSearchAlias)} may not be null or empty, has to be set to ensure alias helpers function correctly");

		_url = $"{IndexName}/{base.BulkPathAndQuery}";
	}

	/// The index name used for indexing. This the configured <see cref="IndexChannelOptions{TDocument}.IndexFormat"/> to compute a single index name for all operations.
	/// <para>Since this is catalog data and not time series data, all data needs to end up in a single index</para>
	public string IndexName { get; private set; }

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.CreateBulkOperationHeader"/>
	protected override BulkOperationHeader CreateBulkOperationHeader(TDocument @event) =>
		BulkRequestDataFactory.CreateBulkOperationHeaderForIndex(@event, ChannelHash, Options, true);

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.RefreshTargets"/>
	protected override string RefreshTargets => IndexName;

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent, TChannelOptions}.BulkPathAndQuery"/>
	protected override string BulkPathAndQuery => _url;

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.AlwaysBootstrapComponentTemplates"/>
	protected override bool AlwaysBootstrapComponentTemplates => true;

	/// <inheritdoc />
	public override async Task<bool> BootstrapElasticsearchAsync(BootstrapMethod bootstrapMethod, string? ilmPolicy = null, CancellationToken ctx = default)
	{
		if (Options.ScriptedHashBulkUpsertLookup is null)
			return await base.BootstrapElasticsearchAsync(bootstrapMethod, ilmPolicy, ctx).ConfigureAwait(false);

		// Ensure channel hash is set before bootstrapping, normally done as part of the bootstrap process
		GenerateChannelHash(bootstrapMethod, ilmPolicy, out _, out _, out _, out _);

		var indexTemplateExists = await IndexTemplateExistsAsync(TemplateName, ctx).ConfigureAwait(false);
		var indexTemplateMatchesHash = indexTemplateExists && await IndexTemplateMatchesHashAsync(TemplateName, ChannelHash, ctx).ConfigureAwait(false);

		var latestAlias = string.Format(Options.IndexFormat, "latest");
		var matchingIndices = string.Format(Options.IndexFormat, "*");
		var currentIndex = await ShouldRemovePreviousAliasAsync(matchingIndices, latestAlias, ctx).ConfigureAwait(false);
		// ensure we index to the latest index unless we have no previous versions, or the index template has changed
		if (string.IsNullOrEmpty(currentIndex) || !indexTemplateExists || !indexTemplateMatchesHash)
			return await base.BootstrapElasticsearchAsync(bootstrapMethod, ilmPolicy, ctx).ConfigureAwait(false);

		IndexName = currentIndex;
		_url = $"{IndexName}/{base.BulkPathAndQuery}";
		return await base.BootstrapElasticsearchAsync(bootstrapMethod, ilmPolicy, ctx).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod, string? ilmPolicy = null)
	{
		if (Options.ScriptedHashBulkUpsertLookup is null)
			return base.BootstrapElasticsearch(bootstrapMethod, ilmPolicy);

		// Ensure channel hash is set before bootstrapping, normally done as part of the bootstrap process
		GenerateChannelHash(bootstrapMethod, ilmPolicy, out _, out _, out _, out _);

		var indexTemplateExists = IndexTemplateExists(TemplateName);
		var indexTemplateMatchesHash = indexTemplateExists && IndexTemplateMatchesHash(TemplateName, ChannelHash);

		var latestAlias = string.Format(Options.IndexFormat, "latest");
		var matchingIndices = string.Format(Options.IndexFormat, "*");
		var currentIndex = ShouldRemovePreviousAlias(matchingIndices, latestAlias);
		// ensure we index to the latest index unless we have no previous versions, or the index template has changed
		if (string.IsNullOrEmpty(currentIndex) || !indexTemplateExists || !indexTemplateMatchesHash)
			return base.BootstrapElasticsearch(bootstrapMethod, ilmPolicy);

		IndexName = currentIndex;
		_url = $"{IndexName}/{base.BulkPathAndQuery}";
		return base.BootstrapElasticsearch(bootstrapMethod, ilmPolicy);
	}

	/// Applies the latest alias to the index.
	public async Task<bool> ApplyLatestAliasAsync(CancellationToken ctx = default)
	{
		var latestAlias = string.Format(Options.IndexFormat, "latest");
		var matchingIndices = string.Format(Options.IndexFormat, "*");

		var removeAction = // language=json
			$$"""
			  {
			    "remove": {
			      "index": "{{matchingIndices}}",
			      "alias": "{{latestAlias}}"
			    }
			  }
			  """;
		var addAction = // language=json
			$$"""
			  {
			    "add": {
			      "index": "{{IndexName}}",
			      "alias": "{{latestAlias}}"
			    }
			  }
			  """;

		var currentLatestAlias = await ShouldRemovePreviousAliasAsync(matchingIndices, latestAlias, ctx).ConfigureAwait(false);
		var actions = !string.IsNullOrEmpty(currentLatestAlias) ? string.Join(",\n", removeAction, addAction) : addAction;

		var putAliasesJson = // language=json
			$$"""
			  {
			    "actions": [
			      {{actions}}
			    ]
			  }
			  """;
		var response = await Options.Transport.PostAsync<StringResponse>($"_aliases", PostData.String(putAliasesJson), ctx).ConfigureAwait(false);
		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	/// Applies the active alias to the index to the point of the latest index.
	/// Uses <see cref="CatalogIndexChannelOptionsBase{TDocument}.ActiveSearchAlias"/> as the active alias
	/// <param name="indexPointingToLatestAlias">If null, the backing index for the latest alias will be queried.</param>
	/// <param name="ctx"></param>
	public async Task<bool> ApplyActiveSearchAliasAsync(string? indexPointingToLatestAlias = null, CancellationToken ctx = default)
	{
		var latestAlias = string.Format(Options.IndexFormat, "latest");
		indexPointingToLatestAlias ??= (await CatAsync($"_cat/aliases/{latestAlias}?h=index", ctx).ConfigureAwait(false))
			.Trim(Environment.NewLine.ToCharArray());

		if (string.IsNullOrEmpty(indexPointingToLatestAlias))
			return false;

		var matchingIndices = string.Format(Options.IndexFormat, "*");
		var removeAction = // language=json
			$$"""
			  {
			    "remove": {
			      "index": "{{matchingIndices}}",
			      "alias": "{{Options.ActiveSearchAlias}}"
			    }
			  }
			  """;
		var addAction = // language=json
			$$"""
			  {
			    "add": {
			      "index": "{{indexPointingToLatestAlias}}",
			      "alias": "{{Options.ActiveSearchAlias}}"
			    }
			  }
			  """;

		var currentAlias = await ShouldRemovePreviousAliasAsync(matchingIndices, latestAlias, ctx).ConfigureAwait(false);
		var actions = !string.IsNullOrEmpty(currentAlias) ? string.Join(",\n", removeAction, addAction) : addAction;

		var putAliasesJson = // language=json
			$$"""
			  {
			    "actions": [
			      {{actions}}
			    ]
			  }
			  """;
		var response = await Options.Transport.PostAsync<StringResponse>($"_aliases", PostData.String(putAliasesJson), ctx).ConfigureAwait(false);
		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	private async Task<string> ShouldRemovePreviousAliasAsync(string matchingIndices, string alias, CancellationToken ctx)
	{
		var hasPreviousVersions = await Options.Transport.HeadAsync($"{matchingIndices}?allow_no_indices=false", ctx).ConfigureAwait(false);
		var queryAliasIndex = (await CatAsync($"_cat/aliases/{alias}?h=index", ctx).ConfigureAwait(false))
			.Trim(Environment.NewLine.ToCharArray());
		return hasPreviousVersions.ApiCallDetails.HttpStatusCode == 200 ? queryAliasIndex : string.Empty;
	}
	private string ShouldRemovePreviousAlias(string matchingIndices, string alias)
	{
		var hasPreviousVersions = Options.Transport.Head($"{matchingIndices}?allow_no_indices=false");
		var queryAliasIndex = Cat($"_cat/aliases/{alias}?h=index").Trim(Environment.NewLine.ToCharArray());
		return hasPreviousVersions.ApiCallDetails.HttpStatusCode == 200 ? queryAliasIndex : string.Empty;
	}

	private async Task<string> CatAsync(string url, CancellationToken ctx)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var catResponse = await Options.Transport.RequestAsync<StringResponse>(new EndpointPath(HttpMethod.GET, url), postData: null, null, rq, ctx)
			.ConfigureAwait(false);
		return catResponse.Body;
	}

	private string Cat(string url)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var catResponse = Options.Transport.Request<StringResponse>(new EndpointPath(HttpMethod.GET, url), postData: null, null, rq);
		return catResponse.Body;
	}


	/// Applies the latest and current aliases to <see cref="IndexName"/>. Use this if you want to ensure that the latest index is always the active index
	/// immediately after writing to the index.
	/// <para>If you want more control of the timing call <see cref="ApplyLatestAliasAsync"/> first immediately</para>
	/// <para>You can then call <see cref="ApplyActiveSearchAliasAsync"/> to ensure the latest alias is current at your own leisure</para>
	public async Task<bool> ApplyAliasesAsync(CancellationToken ctx = default) =>
		await ApplyLatestAliasAsync(ctx).ConfigureAwait(false) && await ApplyActiveSearchAliasAsync(IndexName, ctx).ConfigureAwait(false);
}
