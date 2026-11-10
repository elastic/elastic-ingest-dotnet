// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Ingest.Elasticsearch.Serialization;

internal static class JsonElements
{
	/// <summary>
	/// Parses <paramref name="json"/> into an element that outlives the call. A <see cref="JsonDocument"/> rents its buffers
	/// from the array pool and hands them back on dispose, so <c>JsonDocument.Parse(json).RootElement.Clone()</c> leaks them.
	/// </summary>
	public static JsonElement Parse(string json)
	{
#if NET11_0_OR_GREATER
		return JsonElement.Parse(json);
#else
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
#endif
	}
}
