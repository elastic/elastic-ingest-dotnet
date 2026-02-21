// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Specifies the Elasticsearch target type for an entity.
/// </summary>
public enum EntityTarget
{
	/// <summary>Traditional Elasticsearch index with optional aliases and date patterns.</summary>
	Index,

	/// <summary>Elasticsearch data stream for append-only time-series or log data.</summary>
	DataStream,

	/// <summary>Wired stream that sends data to the /logs endpoint, managed by Elasticsearch.</summary>
	WiredStream
}
