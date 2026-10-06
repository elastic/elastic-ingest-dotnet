// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

public record Doc(string Id, string Name, int N);

[JsonSerializable(typeof(Doc))]
[JsonSerializable(typeof(string))]
public partial class BulkTestContext : JsonSerializerContext;
