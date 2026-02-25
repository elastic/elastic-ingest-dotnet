// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property on a document type as an input field for AI enrichment.
/// Input fields are read from the indexed document source and used to build the LLM prompt.
/// The ES field name is resolved from <c>[JsonPropertyName]</c> or the context naming policy.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AiInputAttribute : Attribute;
