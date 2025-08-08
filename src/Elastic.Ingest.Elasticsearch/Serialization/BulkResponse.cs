// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary>Represents the _bulk response from Elasticsearch</summary>
public class BulkResponse : ElasticsearchResponse
{
	/// <summary>
	/// 
	/// </summary>
	public bool Errors { get; set; }

	/// <summary>
	/// 
	/// </summary>
	public long Took { get; set; }

	/// <summary>
	/// Individual bulk response items information
	/// </summary>
	[JsonPropertyName("items")]
	//[JsonConverter(typeof(ResponseItemsConverter))]
	public IReadOnlyCollection<BulkResponseItem> Items { get; set; } = [];

	/// <summary> Overall bulk error from Elasticsearch if any</summary>
	[JsonPropertyName("error")]
	public ErrorCause? Error { get; set; }

	

	/// <summary>
	/// Tries and get the error from Elasticsearch as string
	/// </summary>
	/// <returns>True if Elasticsearch contained an overall bulk error</returns>
	public bool TryGetServerErrorReason(out string? reason)
	{
		reason = Error?.Reason;
		return !string.IsNullOrWhiteSpace(reason);
	}

//#if NET
//	private static ReadOnlySpan<byte> ErrorsProperty => "errors"u8;
//	private static ReadOnlySpan<byte> ErrorProperty => "error"u8;
//	private static ReadOnlySpan<byte> TookProperty => "took"u8;
//	private static ReadOnlySpan<byte> IngestTookProperty => "ingest_took"u8;

//	private static readonly byte Comma = (byte)',';

//	/// <summary>
//	/// TODO
//	/// </summary>
//	/// <param name="stream"></param>
//	/// <param name="apiCallDetails"></param>
//	/// /// <param name="errorsOnly"></param>
//	/// <param name="cancellationToken"></param>
//	/// <returns></returns>
//	public static async Task<BulkResponse> FromStreamAsync(Stream stream, ApiCallDetails apiCallDetails, bool errorsOnly, CancellationToken cancellationToken = default)
//	{
//		//var stream = streamResponse.Body;
//		var errors = CheckForErrors(stream, errorsOnly, out var rentedArray, out var took);

//		IReadOnlyCollection<BulkResponseItem> items = [];
//		JsonReaderState jsonReaderState = default;
//		ResponseState responseState = default;

//		if (rentedArray is not null)
//		{
//			var buffer = rentedArray.AsMemory();

//			buffer = buffer[64..]; // skip the read ahead portion.

//			var leftOver = 64;
//			var read = 0;

//			while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
//			{
//				if (errors == ErrorType.ItemErrors)
//				{
//					ReadItems(buffer.Span[..(leftOver + read)], errorsOnly, ref jsonReaderState, ref responseState, ref items);
//				}
//				else if (errors == ErrorType.Error)
//				{
//					// TODO
//				}
//			}
//		}

//		if (rentedArray is not null)
//			ArrayPool<byte>.Shared.Return(rentedArray);

//		// TODO - We need a way to set the ApiCallDetails
//		return new BulkResponse { /*ApiCallDetails = streamResponse.ApiCallDetails,*/ Took = took, Errors = errors == ErrorType.ItemErrors };
//	}

//	private struct ResponseState()
//	{
//		public int ArrayDepth = -1;

//		public bool InItem = false;
//	}

//	private static int ReadItems(ReadOnlySpan<byte> buffer, bool errorsOnly, ref JsonReaderState readerState, ref ResponseState responseState, ref IReadOnlyCollection<BulkResponseItem> items)
//	{
//		var jsonReader = new Utf8JsonReader(buffer, false, readerState);

//		while (jsonReader.Read())
//		{
//			if (responseState.ArrayDepth == -1 && jsonReader.TokenType != JsonTokenType.StartArray)
//				jsonReader.TrySkip();


//		}

//		return 0;
//	}

//	[SkipLocalsInit]
//	private static ErrorType CheckForErrors(Stream stream, bool errorsOnly, [NotNullWhen(true)] out byte[]? rentedArray, out long took)
//	{
//		rentedArray = null;

//		// Read ahead to see if we have a general error or item errors on the response so we can handle accordingly.

//		Span<byte> buffer = stackalloc byte[64]; // This should be sufficient for most scenarios
//		stream.ReadExactly(buffer);

//		var position = 0;
//		bool? errors = null;
//		took = 0;

//		if ((position = buffer.IndexOf(ErrorProperty)) > -1)
//		{
//			var span = buffer[position..];

//			rentedArray = ArrayPool<byte>.Shared.Rent(4096); // TODO - What is the best size for this?
//			buffer.CopyTo(rentedArray);
//			return ErrorType.Error;
//		}

//		if ((position = buffer.IndexOf(TookProperty)) > -1)
//		{
//			var span = buffer[position..];
//			var commaIndex = span.IndexOf(Comma);

//			if (long.TryParse(span[..commaIndex], out var result))
//				took = result;
//		}

//		if ((position = buffer.IndexOf(ErrorsProperty)) > -1)
//		{
//			var span = buffer[position..];
//			var commaIndex = span.IndexOf(Comma);

//			if (commaIndex == 4)
//				errors = true;
//			else if (commaIndex == 5)
//				errors = false;
//		}

//		if (took > 0 && errors.HasValue && errors.Value == true)
//		{
//			rentedArray = ArrayPool<byte>.Shared.Rent(4096); // TODO - What is the best size for this?
//			buffer.CopyTo(rentedArray);
//			return ErrorType.ItemErrors;
//		}

//		if (took > 0 && errors.HasValue && errors.Value == false)
//		{
//			if (errorsOnly == false)
//			{
//				rentedArray = ArrayPool<byte>.Shared.Rent(4096); // TODO - What is the best size for this?
//				buffer.CopyTo(rentedArray);
//			}
			
//			return ErrorType.None;
//		}

//		// TODO - Read ingest_took

//		return ErrorType.Unknown;
//	}

//	private enum ErrorType
//	{
//		Unknown,
//		None,
//		Error,
//		ItemErrors
//	}

//	//{
//	//  "error" : {
//	//    "root_cause" : [
//	//      {
//	//        "type" : "illegal_argument_exception",
//	//        "reason" : "Malformed action/metadata line [3], expected START_OBJECT but found [VALUE_STRING]"

//	//	  }
//	//  "status" : 400
//	//}
//#endif
}

internal class ResponseItemsConverter : JsonConverter<IReadOnlyCollection<BulkResponseItem>>
{
	public static readonly IReadOnlyCollection<BulkResponseItem> EmptyBulkItems =
		new ReadOnlyCollection<BulkResponseItem>([]);

	public override IReadOnlyCollection<BulkResponseItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray) return EmptyBulkItems;

		var list = new List<BulkResponseItem>();
		var depth = reader.CurrentDepth;
		while (reader.Read() && reader.CurrentDepth > depth)
		{
			var item = JsonSerializer.Deserialize<BulkResponseItem>(ref reader, IngestSerializationContext.Default.BulkResponseItem);
			if (item != null)
				list.Add(item);
		}
		return new ReadOnlyCollection<BulkResponseItem>(list);
	}

	public override void Write(Utf8JsonWriter writer, IReadOnlyCollection<BulkResponseItem> value, JsonSerializerOptions options) =>
		throw new NotImplementedException();
}
