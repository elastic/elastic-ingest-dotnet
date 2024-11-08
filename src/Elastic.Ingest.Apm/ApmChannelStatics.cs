// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport;

namespace Elastic.Ingest.Apm;

internal static class ApmChannelStatics
{
	public static readonly byte[] LineFeed = { (byte)'\n' };

	public static readonly RequestConfiguration RequestConfig = new() { ContentType = "application/x-ndjson" };

	public static readonly JsonSerializerOptions SerializerOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 64, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};
}
