// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
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
	/// <param name="indexFormat">The format string for computing alias/index names (e.g., "products-{0:yyyy.MM.dd.HHmmss}").</param>
	public LatestAndSearchAliasStrategy(string indexFormat) => _indexFormat = indexFormat;

	/// <inheritdoc />
	public async Task<bool> ApplyAliasesAsync(AliasContext context, CancellationToken ctx = default) =>
		await ApplyLatestAliasAsync(context, ctx).ConfigureAwait(false)
		&& await ApplyActiveSearchAliasAsync(context, context.IndexName, ctx).ConfigureAwait(false);

	/// <inheritdoc />
	public bool ApplyAliases(AliasContext context) =>
		ApplyLatestAlias(context) && ApplyActiveSearchAlias(context, context.IndexName);

	/// <summary> Applies the "latest" alias to the current index. </summary>
	public async Task<bool> ApplyLatestAliasAsync(AliasContext context, CancellationToken ctx = default)
	{
		var latestAlias = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "latest");
		var matchingIndices = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "*");

		var removeAction = $$"""{ "remove": { "index": "{{matchingIndices}}", "alias": "{{latestAlias}}" } }""";
		var addAction = $$"""{ "add": { "index": "{{context.IndexName}}", "alias": "{{latestAlias}}" } }""";

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

	private bool ApplyLatestAlias(AliasContext context)
	{
		var latestAlias = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "latest");
		var matchingIndices = string.Format(System.Globalization.CultureInfo.InvariantCulture, _indexFormat, "*");

		var removeAction = $$"""{ "remove": { "index": "{{matchingIndices}}", "alias": "{{latestAlias}}" } }""";
		var addAction = $$"""{ "add": { "index": "{{context.IndexName}}", "alias": "{{latestAlias}}" } }""";

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
