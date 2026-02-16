// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Rollover strategy that calls <c>POST {target}/_rollover</c> with optional conditions.
/// </summary>
public class ManualRolloverStrategy : IRolloverStrategy
{
	/// <inheritdoc />
	public async Task<bool> RolloverAsync(RolloverContext context, CancellationToken ctx = default)
	{
		var body = BuildConditionsBody(context);
		var response = await context.Transport.RequestAsync<StringResponse>(
			HttpMethod.POST,
			$"{context.Target}/_rollover",
			body != null ? PostData.String(body) : null,
			cancellationToken: ctx
		).ConfigureAwait(false);

		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	/// <inheritdoc />
	public bool Rollover(RolloverContext context)
	{
		var body = BuildConditionsBody(context);
		var response = context.Transport.Request<StringResponse>(
			HttpMethod.POST,
			$"{context.Target}/_rollover",
			body != null ? PostData.String(body) : null
		);

		return response.ApiCallDetails.HasSuccessfulStatusCode;
	}

	private static string? BuildConditionsBody(RolloverContext context)
	{
		if (context.MaxAge == null && context.MaxSize == null && context.MaxDocs == null)
			return null;

		var conditions = new System.Text.StringBuilder("{ \"conditions\": {");
		var first = true;

		if (context.MaxAge != null)
		{
			conditions.Append(" \"max_age\": \"").Append(context.MaxAge).Append('"');
			first = false;
		}

		if (context.MaxSize != null)
		{
			if (!first) conditions.Append(',');
			conditions.Append(" \"max_primary_shard_size\": \"").Append(context.MaxSize).Append('"');
			first = false;
		}

		if (context.MaxDocs != null)
		{
			if (!first) conditions.Append(',');
			conditions.Append(" \"max_docs\": ").Append(context.MaxDocs);
		}

		conditions.Append(" } }");
		return conditions.ToString();
	}
}
