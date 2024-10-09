// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using Elastic.Ingest.Elasticsearch.DataStreams;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary> TODO </summary>
public readonly struct BulkHeader
{
	/// <summary> The index to write to, never set when writing using <see cref="DataStreamChannel{TEvent}"/> </summary>
	public string? Index { get; init; }

	/// <summary> The id of the object being written, never set when writing using <see cref="DataStreamChannel{TEvent}"/>  </summary>
	public string? Id { get; init; }

	/// <summary> Require <see cref="Index"/> to point to an alias, never set when writing using <see cref="DataStreamChannel{TEvent}"/> </summary>
	public bool? RequireAlias { get; init; }

	/// <summary>
	/// A map from the full name of fields to the name of dynamic templates. Defaults to an empty map. If a name matches a dynamic template,
	/// then that template will be applied regardless of other match predicates defined in the template. And if a field is already defined
	/// in the mapping, then this parameter won’t be used.
	/// </summary>
	public IDictionary<string, string>? DynamicTemplates { get; init; }

	/// <summary> If true, the response will include the ingest pipelines that were executed. Defaults to false. </summary>
	public bool? ListExecutedPipelines { get; init; }
}
