// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Resolves index names from <see cref="ElasticsearchTypeContext"/>.
/// </summary>
public static class TypeContextResolver
{
	/// <summary>
	/// Resolves the write alias from a type context.
	/// If DatePattern is set: "{WriteTarget}-latest", otherwise just WriteTarget.
	/// </summary>
	public static string ResolveWriteAlias(ElasticsearchTypeContext tc)
	{
		var writeTarget = tc.IndexStrategy?.WriteTarget;
		if (string.IsNullOrEmpty(writeTarget))
			throw new InvalidOperationException("TypeContext must have IndexStrategy.WriteTarget");
		return tc.IndexStrategy?.DatePattern != null
			? $"{writeTarget}-latest"
			: writeTarget!;
	}

	/// <summary>
	/// Resolves the best read target: ReadAlias if available, otherwise WriteAlias.
	/// </summary>
	public static string ResolveReadTarget(ElasticsearchTypeContext tc)
	{
		var readAlias = tc.SearchStrategy?.ReadAlias;
		if (!string.IsNullOrEmpty(readAlias))
			return readAlias!;
		return ResolveWriteAlias(tc);
	}
}
