// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
namespace Elastic.Ingest.Elasticsearch.Indices;

/// <summary>
/// Determines the operation header for each bulk operation.
/// </summary>
public enum OperationMode
{
	/// <summary>
	/// The mode will be determined automatically based on default rules for the preferred mode.
	/// </summary>
	Auto = 0,

	/// <summary>
	/// Each document will be sent with an 'index' bulk operation header.
	/// </summary>
	Index = 1,

	/// <summary>
	/// Each document will be sent with a 'create' bulk operation header.
	/// </summary>
	Create = 2
}
