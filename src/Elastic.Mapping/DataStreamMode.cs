// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Specifies the data stream mode for specialized data stream types.
/// Only applicable when <see cref="EntityTarget"/> is <see cref="EntityTarget.DataStream"/>.
/// </summary>
public enum DataStreamMode
{
	/// <summary>Standard data stream with default settings.</summary>
	Default,

	/// <summary>LogsDB data stream optimized for log data with synthetic source and index sorting.</summary>
	LogsDb,

	/// <summary>TSDB (Time Series Data Stream) optimized for metrics with dimension-based routing.</summary>
	Tsdb
}
