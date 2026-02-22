// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Alias strategy that manages a "latest" alias and an active search alias.
/// Extracted from CatalogIndexChannel's alias management logic.
/// </summary>
public class LatestAndSearchAliasStrategy : IAliasStrategy
{
	private readonly string _indexFormat;

	/// <summary>
	/// Creates a new latest-and-search alias strategy.
	/// </summary>
	/// <param name="indexFormat">The format string for computing alias/index names (e.g., "products-{0}").</param>
	public LatestAndSearchAliasStrategy(string indexFormat) => _indexFormat = indexFormat;

	/// <inheritdoc />
	public async Task<bool> ApplyAliasesAsync(AliasContext context, CancellationToken ctx = default) =>
		await ApplyLatestAliasAsync(context, ctx).ConfigureAwait(false)
		&& await ApplyActiveSearchAliasAsync(context, context.IndexName, ctx).ConfigureAwait(false);

	/// <inheritdoc />
	public bool ApplyAliases(AliasContext context) =>
		ApplyLatestAlias(context) && ApplyActiveSearchAlias(context, context.IndexName);

	/// <summary>
	/// Applies the "latest" alias to the current index.
	/// <para>
	/// When <see cref="AliasContext.IndexName"/> is empty, the concrete index is resolved via
	/// the <c>_resolve/index</c> API. This can happen when the caller (e.g.,
	/// <see cref="IncrementalSyncOrchestrator{TEvent}"/>) does not know the timestamped index
	/// name at call time. Prefer passing a precomputed index name via
	/// <see cref="IngestChannel{TEvent}.IndexName"/> to avoid the extra round-trip.
	/// </para>
	/// </summary>
	public async Task<bool> ApplyLatestAliasAsync(AliasContext context, CancellationToken ctx = default)
	{
		var latestAlias = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "latest");
		var matchingIndices = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "*");

		var indexName = context.IndexName;
		if (string.IsNullOrEmpty(indexName))
		{
			indexName = await ResolveLatestIndexAsync(context.Transport, matchingIndices, ctx).ConfigureAwait(false);
			if (string.IsNullOrEmpty(indexName))
				return false;
		}

		var removeAction = $$"""{ "remove": { "index": "{{matchingIndices}}", "alias": "{{latestAlias}}" } }""";
		var addAction = $$"""{ "add": { "index": "{{indexName}}", "alias": "{{latestAlias}}" } }""";

		var currentLatest = await ResolveAliasIndexAsync(context.Transport, matchingIndices, latestAlias, ctx).ConfigureAwait(false);
		var actions = !string.IsNullOrEmpty(currentLatest)
			? string.Join(",\n", removeAction, addAction)
			: addAction;

		var body = $$"""{ "actions": [ {{actions}} ] }""";
		var response = await context.Transport.PostAsync<StringResponse>("_aliases", PostData.String(body), ctx).ConfigureAwait(false);
		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	/// <summary> Applies the active search alias to the index pointed to by the latest alias. </summary>
	public async Task<bool> ApplyActiveSearchAliasAsync(AliasContext context, string? indexPointingToLatestAlias, CancellationToken ctx = default)
	{
		if (string.IsNullOrEmpty(context.ActiveSearchAlias))
			return true;

		var latestAlias = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "latest");

		if (string.IsNullOrEmpty(indexPointingToLatestAlias))
		{
			indexPointingToLatestAlias = await CatAliasIndexAsync(context.Transport, latestAlias, ctx).ConfigureAwait(false);
			if (string.IsNullOrEmpty(indexPointingToLatestAlias))
				return false;
		}

		var matchingIndices = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "*");

		var removeAction = $$"""{ "remove": { "index": "{{matchingIndices}}", "alias": "{{context.ActiveSearchAlias}}" } }""";
		var addAction = $$"""{ "add": { "index": "{{indexPointingToLatestAlias}}", "alias": "{{context.ActiveSearchAlias}}" } }""";

		var currentAlias = await ResolveAliasIndexAsync(context.Transport, matchingIndices, latestAlias, ctx).ConfigureAwait(false);
		var actions = !string.IsNullOrEmpty(currentAlias)
			? string.Join(",\n", removeAction, addAction)
			: addAction;

		var body = $$"""{ "actions": [ {{actions}} ] }""";
		var response = await context.Transport.PostAsync<StringResponse>("_aliases", PostData.String(body), ctx).ConfigureAwait(false);
		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	/// <summary>
	/// Applies the "latest" alias to the current index (synchronous).
	/// See <see cref="ApplyLatestAliasAsync"/> for details on empty index name handling.
	/// </summary>
	private bool ApplyLatestAlias(AliasContext context)
	{
		var latestAlias = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "latest");
		var matchingIndices = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "*");

		var indexName = context.IndexName;
		if (string.IsNullOrEmpty(indexName))
		{
			indexName = ResolveLatestIndex(context.Transport, matchingIndices);
			if (string.IsNullOrEmpty(indexName))
				return false;
		}

		var removeAction = $$"""{ "remove": { "index": "{{matchingIndices}}", "alias": "{{latestAlias}}" } }""";
		var addAction = $$"""{ "add": { "index": "{{indexName}}", "alias": "{{latestAlias}}" } }""";

		var currentLatest = ResolveAliasIndex(context.Transport, matchingIndices, latestAlias);
		var actions = !string.IsNullOrEmpty(currentLatest)
			? string.Join(",\n", removeAction, addAction)
			: addAction;

		var body = $$"""{ "actions": [ {{actions}} ] }""";
		var response = context.Transport.Post<StringResponse>("_aliases", PostData.String(body));
		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	private bool ApplyActiveSearchAlias(AliasContext context, string? indexPointingToLatestAlias)
	{
		if (string.IsNullOrEmpty(context.ActiveSearchAlias))
			return true;

		var latestAlias = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "latest");

		if (string.IsNullOrEmpty(indexPointingToLatestAlias))
		{
			indexPointingToLatestAlias = CatAliasIndex(context.Transport, latestAlias);
			if (string.IsNullOrEmpty(indexPointingToLatestAlias))
				return false;
		}

		var matchingIndices = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "*");

		var removeAction = $$"""{ "remove": { "index": "{{matchingIndices}}", "alias": "{{context.ActiveSearchAlias}}" } }""";
		var addAction = $$"""{ "add": { "index": "{{indexPointingToLatestAlias}}", "alias": "{{context.ActiveSearchAlias}}" } }""";

		var currentAlias = ResolveAliasIndex(context.Transport, matchingIndices, latestAlias);
		var actions = !string.IsNullOrEmpty(currentAlias)
			? string.Join(",\n", removeAction, addAction)
			: addAction;

		var body = $$"""{ "actions": [ {{actions}} ] }""";
		var response = context.Transport.Post<StringResponse>("_aliases", PostData.String(body));
		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	/// <summary>
	/// Resolves the most recent concrete index matching the pattern via the <c>_resolve/index</c> API.
	/// Returns the last index name (alphabetically descending), which for date-patterned indices
	/// corresponds to the most recently created index.
	/// </summary>
	private static async Task<string?> ResolveLatestIndexAsync(ITransport transport, string matchingIndices, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_resolve/index/{matchingIndices}", cancellationToken: ctx
		).ConfigureAwait(false);
		return ExtractLatestIndexName(response);
	}

	/// <summary>
	/// Resolves the most recent concrete index matching the pattern via the <c>_resolve/index</c> API (synchronous).
	/// </summary>
	private static string? ResolveLatestIndex(ITransport transport, string matchingIndices)
	{
		var response = transport.Request<JsonResponse>(
			HttpMethod.GET, $"_resolve/index/{matchingIndices}"
		);
		return ExtractLatestIndexName(response);
	}

	/// <summary>
	/// Extracts the last index name from a <c>_resolve/index</c> response.
	/// Response shape: <c>{ "indices": [{ "name": "my-index-2026.02.22" }, ...] }</c>.
	/// Indices are returned in alphabetical order; the last entry is the most recent
	/// for date-patterned index names.
	/// </summary>
	private static string? ExtractLatestIndexName(JsonResponse response)
	{
		if (response.Body is not JsonObject root)
			return null;

		var indices = root["indices"]?.AsArray();
		if (indices == null || indices.Count == 0)
			return null;

		// Take the last index — alphabetically last matches the most recent date-patterned name
		var name = indices[indices.Count - 1]?["name"]?.GetValue<string>();
		return string.IsNullOrEmpty(name) ? null : name;
	}

	private static async Task<string?> ResolveAliasIndexAsync(ITransport transport, string matchingIndices, string alias, CancellationToken ctx)
	{
		var hasPrevious = await transport.HeadAsync($"{matchingIndices}?allow_no_indices=false", ctx).ConfigureAwait(false);
		if (hasPrevious.ApiCallDetails.HttpStatusCode != 200)
			return null;

		return await CatAliasIndexAsync(transport, alias, ctx).ConfigureAwait(false);
	}

	private static string? ResolveAliasIndex(ITransport transport, string matchingIndices, string alias)
	{
		var hasPrevious = transport.Head($"{matchingIndices}?allow_no_indices=false");
		if (hasPrevious.ApiCallDetails.HttpStatusCode != 200)
			return null;

		return CatAliasIndex(transport, alias);
	}

	private static async Task<string?> CatAliasIndexAsync(ITransport transport, string alias, CancellationToken ctx)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var response = await transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{alias}?h=index", null, rq, ctx
		).ConfigureAwait(false);
		var index = response.Body?.Trim(Environment.NewLine.ToCharArray());
		return string.IsNullOrEmpty(index) ? null : index;
	}

	private static string? CatAliasIndex(ITransport transport, string alias)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var response = transport.Request<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{alias}?h=index", null, rq
		);
		var index = response.Body?.Trim(Environment.NewLine.ToCharArray());
		return string.IsNullOrEmpty(index) ? null : index;
	}
}
