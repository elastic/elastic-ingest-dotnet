// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as a <c>flattened</c> field: the whole object, with any keys, is indexed as a single
/// field whose leaf values are keywords. Use it for free-form metadata, for example a
/// <c>Dictionary&lt;string, object?&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FlattenedAttribute : Attribute;
