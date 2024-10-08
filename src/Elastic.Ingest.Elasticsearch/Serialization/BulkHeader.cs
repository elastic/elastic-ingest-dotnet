// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using Elastic.Ingest.Elasticsearch.DataStreams;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary> TODO </summary>
public struct BulkHeader
{
	/// <summary> The index to write to, never set when writing using <see cref="DataStreamChannel{TEvent}"/> </summary>
	public string? Index { get; set; }

	/// <summary> The id of the object being written, never set when writing using <see cref="DataStreamChannel{TEvent}"/>  </summary>
	public string? Id { get; set; }

	/// <summary> Require <see cref="Index"/> to point to an alias, never set when writing using <see cref="DataStreamChannel{TEvent}"/> </summary>
	public bool? RequireAlias { get; set; }

	/// <summary> TODO </summary>
	public Dictionary<string, string>? DynamicTemplates { get; init; }
}
