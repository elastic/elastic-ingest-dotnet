// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch.Serialization;

[JsonSerializable(typeof(BulkOperationHeader))]
[JsonSerializable(typeof(CreateOperation))]
[JsonSerializable(typeof(IndexOperation))]
[JsonSerializable(typeof(DeleteOperation))]
[JsonSerializable(typeof(UpdateOperation))]
[JsonSerializable(typeof(BulkResponseItem))]
[JsonSerializable(typeof(BulkResponse))]
[JsonSerializable(typeof(BulkResponseItem))]
internal partial class IngestSerializationContext : JsonSerializerContext
{
	static IngestSerializationContext() =>
		Default = new IngestSerializationContext(ElasticsearchChannelStatics.SerializerOptions);
}
