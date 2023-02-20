// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Linq;

namespace Elastic.Ingest.Elasticsearch.DataStreams
{
	/// <summary>
	/// Strongly types a reference to a data stream using Elastic's data stream naming scheme
	/// </summary>
	public record DataStreamName
	{
		/// <summary> Generic type describing the data</summary>
		public string Type { get; init; }

		/// <summary> Describes the data ingested and its structure</summary>
		public string DataSet { get; init; }

		/// <summary> User-configurable arbitrary grouping</summary>
		public string Namespace { get; init; }

		private static readonly char[] BadCharacters = { '\\', '/', '*', '?', '"', '<', '>', '|', ' ', ',', '#' };
		private static readonly string BadCharactersError = string.Join(", ", BadCharacters.Select(c => $"'{c}'").ToArray());

		/// <inheritdoc cref="DataStreamName"/>
		public DataStreamName(string type, string dataSet = "generic", string @namespace = "default")
		{
			if (string.IsNullOrEmpty(type)) throw new ArgumentException($"{nameof(type)} can not be null or empty", nameof(type));
			if (string.IsNullOrEmpty(dataSet)) throw new ArgumentException($"{nameof(dataSet)} can not be null or empty", nameof(dataSet));
			if (string.IsNullOrEmpty(@namespace)) throw new ArgumentException($"{nameof(@namespace)} can not be null or empty", nameof(@namespace));
			if (type.IndexOfAny(BadCharacters) > 0)
				throw new ArgumentException($"{nameof(type)} can not contain any of {BadCharactersError}", nameof(type));
			if (dataSet.IndexOfAny(BadCharacters) > 0)
				throw new ArgumentException($"{nameof(dataSet)} can not contain any of {BadCharactersError}", nameof(type));
			if (@namespace.IndexOfAny(BadCharacters) > 0)
				throw new ArgumentException($"{nameof(@namespace)} can not contain any of {BadCharactersError}", nameof(type));

			Type = type.ToLowerInvariant();
			DataSet = dataSet.ToLowerInvariant();
			Namespace = @namespace.ToLowerInvariant();
		}

		/// <summary> Returns a good index template name for this data stream</summary>
		public string GetTemplateName() => $"{Type}-{DataSet}";
		/// <summary> Returns a good index template wildcard match for this data stream</summary>
		public string GetNamespaceWildcard() => $"{Type}-{DataSet}-*";

		private string? _stringValue;
		/// <inheritdoc cref="object.ToString"/>>
		public override string ToString()
		{
			if (_stringValue != null) return _stringValue;

			_stringValue = $"{Type}-{DataSet}-{Namespace}";
			return _stringValue;
		}
	}
}
