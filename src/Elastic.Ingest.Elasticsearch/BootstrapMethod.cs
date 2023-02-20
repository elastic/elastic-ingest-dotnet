// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Ingest.Elasticsearch;

/// <summary> Controls how to bootstrap target indices or data streams</summary>
public enum BootstrapMethod
{
	/// <summary>
	/// No Bootstrap of Elasticsearch should occur. The default option.
	/// </summary>
	None,

	/// <summary>
	/// Bootstrap Elasticsearch silently, ignoring any failures to do so.
	/// </summary>
	Silent,

	/// <summary>
	/// Bootstraps Elasticsearch and throws exceptions if it fails to do so.
	/// </summary>
	Failure
}

