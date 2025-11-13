// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;

namespace Elastic.Ingest.Transport;

/// <summary>Returns the version of this library</summary>
public static class LibraryVersion
{
	/// <summary> Type to reflect version information from</summary>
	// ReSharper disable once ClassNeverInstantiated.Local
	private sealed class VersionType { }

	/// <summary> </summary>
	public static readonly ReflectionVersionInfo Current = ReflectionVersionInfo.Create<VersionType>();
}
