// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Elastic.Transport;

namespace Elastic.Ingest.Transport;

//TODO make ReflectionVersionInfo in Elastic.Transport not sealed
public sealed class LibraryVersion : VersionInfo
{
	private static readonly Regex VersionRegex = new(@"^\d+\.\d+\.\d\-?");

	public static readonly LibraryVersion Current = Create<LibraryVersion>();

	private LibraryVersion() { }

	private static LibraryVersion Create<T>()
	{
		var fullVersion = DetermineVersionFromType(typeof(T));
		var clientVersion = new LibraryVersion();
		clientVersion.StoreVersion(fullVersion);
		return clientVersion;
	}

	private static LibraryVersion Create(Type type)
	{
		var fullVersion = DetermineVersionFromType(type);
		var clientVersion = new LibraryVersion();
		clientVersion.StoreVersion(fullVersion);
		return clientVersion;
	}

	private static string DetermineVersionFromType(Type type)
	{
		var productVersion = EmptyVersion;
		var assembly = type.Assembly;

		try
		{
			productVersion = type.Assembly?.GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? EmptyVersion;
		}
		catch
		{
			// ignore failures and fall through
		}

		try
		{
			if (productVersion == EmptyVersion)
				productVersion = FileVersionInfo.GetVersionInfo(assembly.Location)?.ProductVersion ?? EmptyVersion;
		}
		catch
		{
			// ignore failures and fall through
		}

		try
		{
			// This fallback may not include the minor version numbers
			if (productVersion == EmptyVersion)
				productVersion = assembly.GetName()?.Version?.ToString() ?? EmptyVersion;
		}
		catch
		{
			// ignore failures and fall through
		}

		if (productVersion == EmptyVersion) return EmptyVersion;

		var match = VersionRegex.Match(productVersion);

		return match.Success ? match.Value : EmptyVersion;
	}
}
