// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that patches the mapping of an <b>existing</b> index with the fields it is missing.
/// <para>
/// Templates only apply when an index is created, so after a type gains a field the live index of a
/// fixed-name index never learns about it. This step reads <c>GET {index}/_mapping</c>, works out which
/// fields of the type's mapping the index does not have (at any nesting depth, including multi-fields and
/// runtime fields) and sends only those with <c>PUT {index}/_mapping</c>.
/// </para>
/// <para>
/// Strictly additive: fields whose type changed cannot be altered by a PUT and are reported through
/// <see cref="BootstrapContext.MappingConflicts"/> instead. Does nothing when the index does not exist yet
/// (the template applies on creation) or when the index is already up to date.
/// </para>
/// Must come after <see cref="ComponentTemplateStep"/>.
/// </summary>
public class IndexMappingPatchStep : IBootstrapStep
{
	/// <inheritdoc />
	public string Name => "IndexMappingPatch";

	/// <inheritdoc />
	public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		if (string.IsNullOrEmpty(context.IndexName) || context.GetMappingsJson is null)
			return true;
		var index = context.IndexName!;

		var get = await context.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"{index}/_mapping", cancellationToken: ctx
		).ConfigureAwait(false);
		if (get.ApiCallDetails.HttpStatusCode == 404)
			return true;
		if (!get.ApiCallDetails.HasSuccessfulStatusCode)
			return Fail(context, $"Failure to read the mapping of `{index}`", get);

		var delta = MappingDiff.ComputeAdditions(context.GetMappingsJson(), get.Body);
		context.MappingConflicts = delta.Conflicts;
		if (delta.IsEmpty)
			return true;

		var put = await context.Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"{index}/_mapping", PostData.String(delta.Json!), cancellationToken: ctx
		).ConfigureAwait(false);
		return put.ApiCallDetails.HasSuccessfulStatusCode || Fail(context, $"Failure to patch the mapping of `{index}`", put);
	}

	/// <inheritdoc />
	public bool Execute(BootstrapContext context)
	{
		if (string.IsNullOrEmpty(context.IndexName) || context.GetMappingsJson is null)
			return true;
		var index = context.IndexName!;

		var get = context.Transport.Request<StringResponse>(HttpMethod.GET, $"{index}/_mapping");
		if (get.ApiCallDetails.HttpStatusCode == 404)
			return true;
		if (!get.ApiCallDetails.HasSuccessfulStatusCode)
			return Fail(context, $"Failure to read the mapping of `{index}`", get);

		var delta = MappingDiff.ComputeAdditions(context.GetMappingsJson(), get.Body);
		context.MappingConflicts = delta.Conflicts;
		if (delta.IsEmpty)
			return true;

		var put = context.Transport.Request<StringResponse>(HttpMethod.PUT, $"{index}/_mapping", PostData.String(delta.Json!));
		return put.ApiCallDetails.HasSuccessfulStatusCode || Fail(context, $"Failure to patch the mapping of `{index}`", put);
	}

	private static bool Fail(BootstrapContext context, string message, StringResponse response) =>
		context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception($"{message} for {context.TemplateWildcard}: {response}", response.ApiCallDetails.OriginalException);
}
