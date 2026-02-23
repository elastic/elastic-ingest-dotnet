// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a <see cref="DateTimeOffset"/> property as the last-updated tracking field.
/// The source generator produces a setter delegate so that
/// <see cref="IStaticMappingResolver{T}"/> consumers (e.g., IncrementalSyncOrchestrator)
/// can stamp each document with the current batch timestamp automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LastUpdatedAttribute : Attribute;
