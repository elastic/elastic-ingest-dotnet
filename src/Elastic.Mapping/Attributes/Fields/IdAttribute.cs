// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as the document identifier for Elasticsearch indexing and upserts.
/// The source generator produces a <c>Func&lt;object, string?&gt;</c> accessor delegate
/// that the ingest channel uses to extract the document <c>_id</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class IdAttribute : Attribute;
