// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Explicitly marks a property as the timestamp field for data stream routing and index date patterns.
/// Use when the type has multiple date fields or no property named "timestamp" or "@timestamp".
/// The source generator produces a <c>Func&lt;object, DateTimeOffset?&gt;</c> accessor delegate.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class TimestampAttribute : Attribute;
