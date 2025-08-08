// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// 
/// </summary>
public readonly record struct Target(string Value)
{
	/// <summary>
	/// 
	/// </summary>
	public bool IsEmpty => Value.Equals(string.Empty, StringComparison.Ordinal);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator Target(string value) => new(value);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator string(Target value) => value.Value;

	/// <summary>
	/// 
	/// </summary>
	public static Target Empty { get; } = new(string.Empty);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	public static Target From(string value) => string.IsNullOrEmpty(value) ? Empty : new(value);

	/// <inheritdoc/>
	public override string ToString() => Value;
}

/// <summary>
/// 
/// </summary>
public readonly record struct RequireAlias(bool Value)
{
	/// <summary>
	/// 
	/// </summary>
	/// <param name="value"></param>
	public static implicit operator RequireAlias(bool value) => new(value);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator bool(RequireAlias value) => value.Value;

	internal bool IsFalse => !Value;

	/// <summary>
	/// 
	/// </summary>
	public static RequireAlias True { get; } = new(true);

	/// <summary>
	/// 
	/// </summary>
	public static RequireAlias False { get; } = new(false);
}

/// <summary>
/// 
/// </summary>
public readonly record struct ListExecutedPipelines(bool Value)
{
	/// <summary>
	/// 
	/// </summary>
	public static implicit operator ListExecutedPipelines(bool value) => new(value);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator bool(ListExecutedPipelines value) => value.Value;

	internal bool IsFalse => !Value;

	/// <summary>
	/// 
	/// </summary>
	public static ListExecutedPipelines True { get; } = new(true);

	/// <summary>
	/// 
	/// </summary>
	public static ListExecutedPipelines False { get; } = new(false);
}

/// <summary>
/// 
/// </summary>
public readonly record struct DynamicTemplateMappings : IEnumerable<KeyValuePair<string, string>>
{
	private const int OverflowAdditionalCapacity = 8;

	// Up to eight mappings are stored in an inline array. If there are more items than will fit in the inline array,
	// an array is allocated to store all the items.

#if NET8_0_OR_GREATER
	private readonly InlineDynamicTemplates _inlineMappings;
#else
	internal readonly KeyValuePair<string, string> _tag1;
	internal readonly KeyValuePair<string, string> _tag2;
	internal readonly KeyValuePair<string, string> _tag3;
	internal readonly KeyValuePair<string, string> _tag4;
	internal readonly KeyValuePair<string, string> _tag5;
	internal readonly KeyValuePair<string, string> _tag6;
	internal readonly KeyValuePair<string, string> _tag7;
	internal readonly KeyValuePair<string, string> _tag8;
#endif
	private readonly KeyValuePair<string, string>[]? _overflowMappings;
	private readonly int _mappingCount;

	/// <summary>
	/// 
	/// </summary>
	public DynamicTemplateMappings() => _mappingCount = 0;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="dynamicTemplatesMappings"></param>
	public DynamicTemplateMappings(IDictionary<string, string> dynamicTemplatesMappings)
	{
		_mappingCount = dynamicTemplatesMappings.Count;

		if (_mappingCount == 0)
			return;

		if (_mappingCount > OverflowAdditionalCapacity)
		{
			_overflowMappings = [.. dynamicTemplatesMappings];
			return;
		}

		var count = 0;

#if NET8_0_OR_GREATER
		scoped Span<KeyValuePair<string, string>> mappings = _inlineMappings;

		foreach (var mapping in dynamicTemplatesMappings)
		{
			mappings[count++] = mapping;
		}
#else
		foreach (var mapping in dynamicTemplatesMappings)
		{
			switch (count++)
			{
				case 1:
					_tag1 = mapping;
					break;

				case 2:
					_tag2 = mapping;
					break;

				case 3:
					_tag3 = mapping;
					break;

				case 4:
					_tag4 = mapping;
					break;

				case 5:
					_tag5 = mapping;
					break;

				case 6:
					_tag6 = mapping;
					break;

				case 7:
					_tag7 = mapping;
					break;

				case 8:
					_tag8 = mapping;
					break;
			}
		}
#endif
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="dynamicTemplates"></param>
	public DynamicTemplateMappings(params ReadOnlySpan<KeyValuePair<string, string>> dynamicTemplates) => throw new NotImplementedException();

	readonly IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator() => new Enumerator(in this);

	readonly IEnumerator IEnumerable.GetEnumerator() => new Enumerator(in this);

	/// <summary>
	/// 
	/// </summary>
	public static DynamicTemplateMappings Empty { get; } = new();

	/// <summary>
	/// 
	/// </summary>
	public readonly int Count => _mappingCount;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	public readonly KeyValuePair<string, string> this[int index]
	{
		get
		{
#if NET8_0_OR_GREATER
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_mappingCount, nameof(index));
			return _overflowMappings is null ? _inlineMappings[index] : _overflowMappings[index];
#else
			if ((uint)index >= (uint)_mappingCount)
				throw new ArgumentNullException(nameof(index));

			if (_overflowMappings is not null)
            {
                return _overflowMappings[index];
            }

			return index switch
			{
				0 => _tag1,
				1 => _tag2,
				2 => _tag3,
				3 => _tag4,
				4 => _tag5,
				5 => _tag6,
				6 => _tag7,
				7 => _tag8,
				_ => default, // we shouldn't come here anyway.
			};
#endif
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator DynamicTemplateMappings(Dictionary<string, string> value) => new(value);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator DynamicTemplateMappings(ReadOnlyDictionary<string, string> value) => new(value);

#if NET8_0_OR_GREATER
	[InlineArray(8)]
	private struct InlineDynamicTemplates
	{
		public const int Length = 8;
#pragma warning disable IDE0044 // Add readonly modifier
		private KeyValuePair<string, string> _first;
#pragma warning restore IDE0044 // Add readonly modifier
	}
#endif

	/// <summary>
	/// 
	/// </summary>
	public struct Enumerator : IEnumerator<KeyValuePair<string, string>>
	{
		private readonly DynamicTemplateMappings _dynamicTemplates;
		private int _index;

		internal Enumerator(in DynamicTemplateMappings tagList)
		{
			_index = -1;
			_dynamicTemplates = tagList;
		}

		/// <summary>
		/// 
		/// </summary>
		public readonly KeyValuePair<string, string> Current => _dynamicTemplates[_index];

		readonly object IEnumerator.Current => _dynamicTemplates[_index];

		/// <summary>
		/// 
		/// </summary>
		public void Dispose() => _index = _dynamicTemplates.Count;

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		public bool MoveNext()
		{
			_index++;
			return _index < _dynamicTemplates.Count;
		}

		/// <summary>
		/// 
		/// </summary>
		public void Reset() => _index = -1;
	}
}

/// <summary>
/// 
/// </summary>
/// <param name="Id"></param>
/// <param name="Target"></param>
/// <param name="RequireAlias"></param>
/// <param name="ListExecutedPipelines"></param>
/// <param name="DynamicTemplateMappings"></param>
public readonly record struct BulkIndexOptions(
	Id Id,
	Target Target,
	RequireAlias RequireAlias,
	ListExecutedPipelines ListExecutedPipelines,
	DynamicTemplateMappings DynamicTemplateMappings)
{
}

/// <summary>
/// 
/// </summary>
public readonly record struct Id(string Value)
{
	/// <summary>
	/// 
	/// </summary>
	public bool IsEmpty => Value.Equals(string.Empty, StringComparison.Ordinal);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator Id(string value) => new(value);

	/// <summary>
	/// 
	/// </summary>
	public static implicit operator string(Id value) => value.Value;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	public static Id From(string value) => string.IsNullOrEmpty(value) ? Empty : new(value);

	/// <summary>
	/// 
	/// </summary>
	public static Id Empty { get; } = new(string.Empty);

	/// <inheritdoc/>
	public override string ToString() => Value;
}

internal static class OperationHeaderWriter
{
	internal static ReadOnlySpan<byte> IndexPrefixBytesSpan => """{"index":{"""u8;
	internal static ReadOnlySpan<byte> CreatePrefixBytesSpan => """{"create":{"""u8;
	internal static ReadOnlySpan<byte> DeletePrefixBytesSpan => """{"delete":{"""u8;
	internal static ReadOnlySpan<byte> UpdatePrefixBytesSpan => """{"update":{"""u8;
	internal static ReadOnlySpan<byte> SuffixBytesSpan => """}}"""u8;

	internal static ReadOnlySpan<byte> IdPropertyPrefixBytesSpan => "\"_id\":\""u8;
	internal static ReadOnlySpan<byte> IndexPropertyPrefixBytesSpan => "\"_index\":\""u8;
	internal static ReadOnlySpan<byte> ListExecutedPipelinesPropertyPrefixBytesSpan => "\"list_executed_pipelines\":"u8;
	internal static ReadOnlySpan<byte> RequireAliasPropertyPrefixBytesSpan => "\"require_alias\":"u8;
	internal static ReadOnlySpan<byte> DyanamicTemplatesPropertyPrefixBytesSpan => "\"dynamic_templates\":"u8;

	internal static ReadOnlySpan<byte> TrueBytesSpan => "true"u8;
	internal static ReadOnlySpan<byte> FalseBytesSpan => "false"u8;

	internal const byte DoubleQuoteByte = (byte)'"';
	internal static ReadOnlySpan<byte> DoubleQuote => [DoubleQuoteByte];
	internal static ReadOnlySpan<byte> Comma => [(byte)','];

	internal static ReadOnlySpan<byte> PlainIndexBytesSpan => """{"index":{}}"""u8;
	internal static ReadOnlySpan<byte> PlainCreateBytesSpan => """{"create":{}}"""u8;

	internal static ReadOnlySpan<byte> OpenSquare => [(byte)'['];
	internal static ReadOnlySpan<byte> CloseSquare => [(byte)']'];
	internal static ReadOnlySpan<byte> OpenCurlyBrace => [(byte)'{'];

	internal const byte CloseCurlyBraceByte = (byte)'}';
	internal static ReadOnlySpan<byte> CloseCurlyBrace => [CloseCurlyBraceByte];

	internal static ReadOnlySpan<byte> Colon => [(byte)':'];

	internal const byte NewLineByte = (byte)'\n';
	internal static ReadOnlySpan<byte> NewLine => [NewLineByte];

#if NETSTANDARD
	internal static byte[] PlainIndexBytes => PlainIndexBytesSpan.ToArray();
	internal static byte[] PlainCreateBytes => PlainCreateBytesSpan.ToArray();
	internal static byte[] IndexPrefixBytes => IndexPrefixBytesSpan.ToArray();

	private static readonly JsonEncodedText CreateOperation = JsonEncodedText.Encode("create");
	private static readonly JsonEncodedText UpdateOperation = JsonEncodedText.Encode("update");
	private static readonly JsonEncodedText IndexOperation = JsonEncodedText.Encode("index");
	private static readonly JsonEncodedText DeleteOperation = JsonEncodedText.Encode("delete");
	private static readonly JsonEncodedText IdProperty = JsonEncodedText.Encode("_id");
	private static readonly JsonEncodedText IndexProperty = JsonEncodedText.Encode("_index");
	private static readonly JsonEncodedText RequireAliasProperty = JsonEncodedText.Encode("require_alias");
	private static readonly JsonEncodedText ListExecutedPipelinesProperty = JsonEncodedText.Encode("list_executed_pipelines");
	private static readonly JsonEncodedText DynamicTemplatesProperty = JsonEncodedText.Encode("dynamic_templates");
#endif

//#if NET8_0_OR_GREATER
//	[SkipLocalsInit]
//#endif
//	internal static void WriteIndexOperation(
//		this IBufferWriter<byte> writer,
//		string? id = null,
//		string? target = null,
//		bool? requireAlias = false,
//		bool? listExecutedPipelines = false,
//		IReadOnlyDictionary<string, string>? dynamicTemplates = null)
//	{
//#if NET8_0_OR_GREATER
//		ArgumentNullException.ThrowIfNull(writer);
//#else
//		if (writer is null)
//			throw new ArgumentNullException(nameof(writer));
//#endif
//		if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(target) && requireAlias is null && listExecutedPipelines is null && dynamicTemplates is null)
//		{
//			writer.Write(PlainCreateBytesSpan);
//			return;
//		}

//		var lengthRequired = CalculateIndexOperationHeaderLength(id, target, requireAlias, listExecutedPipelines, dynamicTemplates);

//		byte[]? rentedArray = null;

//		Span<byte> buffer = lengthRequired <= 256
//			? stackalloc byte[lengthRequired]
//			: (rentedArray = ArrayPool<byte>.Shared.Rent(lengthRequired));

//		IndexPrefixBytesSpan.CopyTo(buffer);

//		if (!string.IsNullOrEmpty(id))
//		{

//		}

//		writer.Write(CloseCurlyBrace);
//		writer.Write(NewLine);

//		if (rentedArray is not null)
//			ArrayPool<byte>.Shared.Return(rentedArray);
//	}

	public static bool TryWriteIndexOperationHeader(Memory<byte> destination, out int bytesWritten)
	{
		bytesWritten = 0;

		var length = PlainIndexBytesSpan.Length;

		if (destination.Length < length + 1)
			return false;

		PlainIndexBytesSpan.CopyTo(destination.Span);
		destination.Span[length] = NewLineByte;
		bytesWritten = length + 1;

		return true;
	}

	public static bool TryWriteIndexOperationHeader(Memory<byte> memory,
		out int bytesWritten, in Id id) =>
			TryWriteIndexOperationHeader(memory, id, Target.Empty, new RequireAlias(false), new ListExecutedPipelines(false), [], out bytesWritten);

	public static bool TryWriteIndexOperationHeader(Memory<byte> memory,
		out int bytesWritten, in Id id, in Target target) =>
			TryWriteIndexOperationHeader(memory, id, target, new RequireAlias(false), new ListExecutedPipelines(false), [], out bytesWritten);

#if NET8_0_OR_GREATER
	[SkipLocalsInit]
#endif
	public static bool TryWriteIndexOperationHeader(
		Memory<byte> destination,
		Id id,
		Target target,
		RequireAlias requireAlias,
		ListExecutedPipelines listExecutedPipelines,
		DynamicTemplateMappings dynamicTemplateMappings,
		out int bytesWritten)
	{
		bytesWritten = 0;

		var span = destination.Span;

		if (id.IsEmpty && target.IsEmpty && requireAlias.IsFalse && listExecutedPipelines.IsFalse && dynamicTemplateMappings.Count == 0)
		{
			if (!PlainIndexBytesSpan.TryCopyTo(span))
				return false;

			bytesWritten = PlainIndexBytesSpan.Length;
			return true;
		}

		if (!IndexPrefixBytesSpan.TryCopyTo(span[bytesWritten..]))
			return false;

		bytesWritten += IndexPrefixBytesSpan.Length;

		if (!id.IsEmpty)
		{
			if (!IdPropertyPrefixBytesSpan.TryCopyTo(span[bytesWritten..]))
				return false;

			bytesWritten += IdPropertyPrefixBytesSpan.Length;

#if !NET
			throw new NotImplementedException();
#else
			if (!Encoding.UTF8.TryGetBytes(id.Value, span[bytesWritten..], out var written))
				return false;

			bytesWritten += written;
 
			if (span.Length - bytesWritten < 1)
				return false;

			span[bytesWritten++] = DoubleQuoteByte;
#endif
		}

		span = span[bytesWritten..];

		if (span.Length < 3)
			return false;

		span[0] = CloseCurlyBraceByte;
		span[1] = CloseCurlyBraceByte;
		span[2] = NewLineByte;

		bytesWritten += 3;
		return true;
	}

	public static int CalculateIndexOperationHeaderLength(
		Id id,
		Target target,
		RequireAlias requireAlias,
		ListExecutedPipelines listExecutedPipelines,
		DynamicTemplateMappings dynamicTemplateMappings)
	{
		if (id.IsEmpty && target.IsEmpty && requireAlias.IsFalse && listExecutedPipelines.IsFalse && dynamicTemplateMappings.Count == 0)
		{
			return PlainIndexBytesSpan.Length + 1; // include 1 extra byte for the newline
		}

		var length = IndexPrefixBytesSpan.Length + SuffixBytesSpan.Length + 1; // include final curly braces + newline

		return CalculateHeaderLength(length, id, target, requireAlias, listExecutedPipelines, dynamicTemplateMappings);
	}

	private static int CalculateHeaderLength(
		int headerObjectLength,
		Id id,
		Target target,
		RequireAlias requireAlias,
		ListExecutedPipelines listExecutedPipelines,
		DynamicTemplateMappings dynamicTemplateMappings)
	{
		var length = headerObjectLength;
		var propertyCount = 0;

		if (!id.IsEmpty)
		{
			length += IdPropertyPrefixBytesSpan.Length;
			length += Encoding.UTF8.GetByteCount(id.Value) + 2; // include 2 bytes for the double quotes around the string

			propertyCount++;
		}

		if (!target.IsEmpty)
		{
			length += IndexPropertyPrefixBytesSpan.Length;
			length += Encoding.UTF8.GetByteCount(target.Value) + 2; // include 2 bytes for the double quotes around the string

			propertyCount++;
		}

		if (!requireAlias.IsFalse)
		{
			length += RequireAliasPropertyPrefixBytesSpan.Length;
			length += requireAlias.Value ? 4 : 5;

			propertyCount++;
		}

		if (!listExecutedPipelines.IsFalse)
		{
			length += ListExecutedPipelinesPropertyPrefixBytesSpan.Length;
			length += listExecutedPipelines.Value ? 4 : 5;

			propertyCount++;
		}

		if (dynamicTemplateMappings.Count > 0)
		{
			length += 2; // curly brackets

			foreach (var entry in dynamicTemplateMappings)
			{
				var key = entry.Key;
				var value = entry.Value;

				if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
				{
					length += key.Length + value.Length + 5; // data + double quotes + colon
				}
			}
		}

		// {"Name":"Hello","Data":{"test":"value","test2":"value2"}}

		length += dynamicTemplateMappings.Count - 1; // commas
		length += propertyCount - 1; // commas
		length++; // newline

		return length;
	}
}

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
{
#if NET8_0_OR_GREATER

	[SkipLocalsInit]
	private static ValueTask SerializeHeaderAsync(Stream stream, HeaderSerializationStrategy operation, ref readonly BulkHeader? header, CancellationToken ctx)
	{
		if (!header.HasValue)
		{
			switch (operation)
			{
				case HeaderSerializationStrategy.Index:
					stream.Write(OperationHeaderWriter.PlainIndexBytesSpan);
					break;
				case HeaderSerializationStrategy.Create:
					stream.Write(OperationHeaderWriter.PlainCreateBytesSpan);
					break;
				default:
					throw new ArgumentException($"Expected non null value for {operation}.", nameof(header));
			}

			return ValueTask.CompletedTask;
		}

		//var pipe = PipeWriter.Create(stream, Options);
		//OperationHeaderWriter.WriteIndexOperation(pipe, header.Value.Id); // TODO - Finish this.

		Span<byte> buffer = stackalloc byte[256];

		switch (operation)
		{
			case HeaderSerializationStrategy.Index:
				stream.Write(OperationHeaderWriter.IndexPrefixBytesSpan);
				break;
			case HeaderSerializationStrategy.Create:
				stream.Write(OperationHeaderWriter.CreatePrefixBytesSpan);
				break;
			case HeaderSerializationStrategy.Delete:
				stream.Write(OperationHeaderWriter.DeletePrefixBytesSpan);
				break;
			case HeaderSerializationStrategy.Update:
				stream.Write(OperationHeaderWriter.UpdatePrefixBytesSpan);
				break;
			default:
				throw new ArgumentException($"Unexpected operation {operation}.");
		}

		var propertyCount = 0;
		var headerValue = header.Value;

		if (!string.IsNullOrEmpty(headerValue.Index))
			WriteString(OperationHeaderWriter.IndexPropertyPrefixBytesSpan, stream, headerValue.Index, buffer, ref propertyCount);

		if (!string.IsNullOrEmpty(headerValue.Id))
			WriteString(OperationHeaderWriter.IdPropertyPrefixBytesSpan, stream, headerValue.Id, buffer, ref propertyCount);

		if (headerValue.RequireAlias.HasValue && headerValue.RequireAlias.Value)
		{
			WriteTrue(OperationHeaderWriter.RequireAliasPropertyPrefixBytesSpan, stream, ref propertyCount);
		}
		else if (headerValue.RequireAlias.HasValue && !headerValue.RequireAlias.Value)
		{
			WriteFalse(OperationHeaderWriter.RequireAliasPropertyPrefixBytesSpan, stream, ref propertyCount);
		}

		if (headerValue.ListExecutedPipelines.HasValue && headerValue.ListExecutedPipelines.Value)
		{
			WriteTrue(OperationHeaderWriter.ListExecutedPipelinesPropertyPrefixBytesSpan, stream, ref propertyCount);
		}
		else if (headerValue.ListExecutedPipelines.HasValue && !headerValue.ListExecutedPipelines.Value)
		{
			WriteFalse(OperationHeaderWriter.ListExecutedPipelinesPropertyPrefixBytesSpan, stream, ref propertyCount);
		}

		if (headerValue.DynamicTemplates is not null && headerValue.DynamicTemplates.Count > 0)
		{
			if (propertyCount > 0)
				stream.Write(OperationHeaderWriter.Comma);

			stream.Write(OperationHeaderWriter.DyanamicTemplatesPropertyPrefixBytesSpan);
			stream.Write(OperationHeaderWriter.OpenSquare);

			var entryCount = 0;
			foreach (var (key, value) in headerValue.DynamicTemplates)
			{
				WriteDictionaryEntry(stream, key, value, buffer, ref entryCount);
			}

			stream.Write(OperationHeaderWriter.CloseSquare);
			propertyCount++;
		}

		static void WriteString(ReadOnlySpan<byte> propertyNamePrefix, Stream stream, string value, Span<byte> buffer, ref int propertyCount)
		{
			if (propertyCount > 0)
				stream.Write(OperationHeaderWriter.Comma);

			stream.Write(propertyNamePrefix); // This includes the open quotes for the value

			var length = Encoding.UTF8.GetByteCount(value);

			if (length <= 256)
			{
				Encoding.UTF8.TryGetBytes(value, buffer, out var written);
				stream.Write(buffer[..written]);
			}
			else
			{
				var rentedArray = ArrayPool<byte>.Shared.Rent(length);
				Encoding.UTF8.TryGetBytes(value, rentedArray.AsSpan(), out var written);
				stream.Write(buffer[..written]);
				ArrayPool<byte>.Shared.Return(rentedArray);
			}

			stream.Write(OperationHeaderWriter.DoubleQuote);
			propertyCount++;
		}

		static void WriteTrue(ReadOnlySpan<byte> propertyNamePrefix, Stream stream, ref int propertyCount)
		{
			if (propertyCount > 0)
				stream.Write(OperationHeaderWriter.Comma);

			stream.Write(propertyNamePrefix);
			stream.Write(OperationHeaderWriter.TrueBytesSpan);

			propertyCount++;
		}

		static void WriteFalse(ReadOnlySpan<byte> propertyNamePrefix, Stream stream, ref int propertyCount)
		{
			if (propertyCount > 0)
				stream.Write(OperationHeaderWriter.Comma);

			stream.Write(propertyNamePrefix);
			stream.Write(OperationHeaderWriter.FalseBytesSpan);

			propertyCount++;
		}

		static void WriteDictionaryEntry(Stream stream, string key, string value, Span<byte> buffer, ref int entryCount)
		{
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
				return;

			if (entryCount > 0)
				stream.Write(OperationHeaderWriter.Comma);

			stream.Write(OperationHeaderWriter.OpenCurlyBrace);
			WriteQuotedStringBytes(stream, key, buffer);
			stream.Write(OperationHeaderWriter.Colon);
			WriteQuotedStringBytes(stream, value, buffer);
			stream.Write(OperationHeaderWriter.CloseCurlyBrace);

			entryCount++;
		}

		static void WriteQuotedStringBytes(Stream stream, string value, Span<byte> buffer)
		{
			stream.Write(OperationHeaderWriter.DoubleQuote);
			var length = Encoding.UTF8.GetByteCount(value);
			if (length <= 256)
			{
				Encoding.UTF8.TryGetBytes(value, buffer, out var written);
				stream.Write(buffer[..written]);
			}
			else
			{
				var rentedArray = ArrayPool<byte>.Shared.Rent(length);
				Encoding.UTF8.TryGetBytes(value, rentedArray.AsSpan(), out var written);
				stream.Write(buffer[..written]);
				ArrayPool<byte>.Shared.Return(rentedArray);
			}
			stream.Write(OperationHeaderWriter.DoubleQuote);
		}

		stream.Write(OperationHeaderWriter.SuffixBytesSpan);

		return ValueTask.CompletedTask;
	}
#else
	private static Task SerializeHeaderAsync(Stream stream, HeaderSerializationStrategy operation, ref readonly BulkHeader? header, CancellationToken ctx)
	{
	//	if (!header.HasValue)
	//	{
	//		if (operation != HeaderSerializationStrategy.Index || operation != HeaderSerializationStrategy.Create)
	//			throw new ArgumentException($"Expected non null value for {operation}.", nameof(header));

	//		return HandleNullBulkHeaderAsync(stream, operation);
	//	}

	//	var operationString = operation switch
	//	{
	//		HeaderSerializationStrategy.Create => CreateOperation,
	//		HeaderSerializationStrategy.Delete => DeleteOperation,
	//		HeaderSerializationStrategy.Index => IndexOperation,
	//		HeaderSerializationStrategy.Update => UpdateOperation,
	//		HeaderSerializationStrategy.IndexNoParams => throw new InvalidOperationException(),
	//		HeaderSerializationStrategy.CreateNoParams => throw new InvalidOperationException(),
	//		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	//	};

	//	var headerValue = header.Value;

	//	return SerializeHeaderAsync(stream, operationString, headerValue.Id, headerValue.Index, headerValue.RequireAlias,
	//		headerValue.ListExecutedPipelines, headerValue.DynamicTemplates, ctx);
	//}

	//private static async Task HandleNullBulkHeaderAsync(Stream stream, HeaderSerializationStrategy operation)
	//{
	//	switch (operation)
	//	{
	//		case HeaderSerializationStrategy.Index:
	//			await stream.WriteAsync(PlainIndexBytes, 0, PlainCreateBytes.Length).ConfigureAwait(false);
	//			break;
	//		case HeaderSerializationStrategy.Create:
	//			await stream.WriteAsync(PlainCreateBytes, 0, PlainCreateBytes.Length).ConfigureAwait(false);
	//			break;
	//	}
	//}

	//private static async Task SerializeHeaderAsync(Stream stream, JsonEncodedText operation, string? id, string? index, bool? requireAlias,
	//	bool? listExecutedPipelines, IDictionary<string, string>? dynamicTemplates, CancellationToken ctx)
	//{
	//	var writer = new Utf8JsonWriter(stream, default);

	//	await using (writer.ConfigureAwait(false))
	//	{
	//		writer.WriteStartObject();
	//		writer.WritePropertyName(operation);
	//		writer.WriteStartObject();

	//		if (!string.IsNullOrWhiteSpace(index))
	//			writer.WriteString(IndexProperty, index);

	//		if (!string.IsNullOrWhiteSpace(id))
	//			writer.WriteString(IdProperty, id);

	//		if (requireAlias.HasValue)
	//			writer.WriteBoolean(RequireAliasProperty, requireAlias.Value);

	//		if (listExecutedPipelines.HasValue)
	//			writer.WriteBoolean(ListExecutedPipelinesProperty, listExecutedPipelines.Value);

	//		if (dynamicTemplates is not null)
	//		{
	//			writer.WritePropertyName(DynamicTemplatesProperty);
	//			writer.WriteStartArray();

	//			foreach (var template in dynamicTemplates)
	//			{
	//				writer.WriteStartObject();
	//				writer.WriteString(template.Key, template.Value);
	//				writer.WriteEndObject();
	//			}

	//			writer.WriteEndArray();
	//		}

	//		writer.WriteEndObject();
	//		writer.WriteEndObject();
	//	}
	if (true){};
	return Task.CompletedTask;
	}
#endif

#if NET8_0_OR_GREATER
	private static ValueTask SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx = default)
	{
		stream.Write(OperationHeaderWriter.PlainIndexBytesSpan);
		return ValueTask.CompletedTask;
	}
#else
	private static async ValueTask SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx) =>
		await stream.WriteAsync(OperationHeaderWriter.PlainIndexBytes, 0, OperationHeaderWriter.PlainIndexBytes.Length, ctx).ConfigureAwait(false);
#endif

#if NET8_0_OR_GREATER
	private static ValueTask SerializePlainCreateHeaderAsync(Stream stream, CancellationToken ctx = default)
	{
		stream.Write(OperationHeaderWriter.PlainCreateBytesSpan);
		return ValueTask.CompletedTask;
	}
#else
	private static async ValueTask SerializePlainCreateHeaderAsync(Stream stream, CancellationToken ctx) =>
		await stream.WriteAsync(OperationHeaderWriter.PlainCreateBytes, 0, OperationHeaderWriter.PlainCreateBytes.Length, ctx).ConfigureAwait(false);
#endif
}
