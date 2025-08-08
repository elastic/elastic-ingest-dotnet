// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Transport;
using Elastic.Transport;
using Elastic.Transport.Products;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// 
/// </summary>
public sealed class OperationResult
{
	/// <summary>
	/// 
	/// </summary>
	public int OperationIndex { get; internal set; }

	/// <summary>
	/// 
	/// </summary>
	public string Action { get; internal set; } = string.Empty;

	/// <summary>
	/// 
	/// </summary>
	public int StatusCode { get; internal set; }

	// TODO - Error
	// TODO - All
}

/// <summary>
/// 
/// </summary>
public sealed class StreamingBulkResponse : StreamResponseBase // TODO - Pull in things from ElasticsearchResponse
{
	private const int DefaultBufferSize = 1024;

	private static ReadOnlySpan<byte> ErrorBytes => "\"error\""u8;

	private const byte LetterCByte = (byte)'c';
	private const byte LetterDByte = (byte)'d';
	private const byte LetterIByte = (byte)'i';
	private const byte LetterUByte = (byte)'u';

	private readonly byte[] _rentedBuffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
	private readonly ResponseMode _responseMode;

	private JsonReaderState _jsonReaderState = new();
	private int _consumed = 0;
	private int _bufferedBytes = -1;
	private OperationResult? _nextResult = null;
	private int _index = 0;
	private bool _enumerated;

	/// <summary>
	/// 
	/// </summary>
	public StreamingBulkResponse() : this(Stream.Null, ResponseMode.Errors) { }

	/// <summary>
	/// 
	/// </summary>
	/// <param name="body"></param>
	/// <param name="responseMode"></param>
	public StreamingBulkResponse(Stream body, ResponseMode responseMode) : base(body) =>
		_responseMode = responseMode;

	/// <summary>
	/// 
	/// </summary>
	public bool Errors { get; private set; }

	/// <summary>
	/// 
	/// </summary>
	public long Took { get; private set; }

	// TODO - Consider a GetErrorsAndStatusAsync which returns a smaller OperationResult without all fields defined
	// This could be used in optimised paths to reduce the bytes allocated when the consumer only cares about the subset of data

	/// <summary>
	/// 
	/// </summary>
	/// <returns></returns>
	public async IAsyncEnumerable<OperationResult> GetOperationResultsAsync() // TODO - Cancellation
	{
		if (_enumerated)
		{
			throw new InvalidOperationException($"Operation results on a streaming response cannot be enumerated twice.");
		}

		_enumerated = true;

		if (Disposed)
		{
			throw new InvalidOperationException("Operation results on a streaming response cannot be enumerated after disposal.");
		}

		if (_responseMode == ResponseMode.Errors && !Errors)
		{
			Dispose();
			yield break;
		}

		if (_responseMode == ResponseMode.ErrorsAndStatus && !Errors)
		{
			// TODO - Fast path processing
		}

		await Task.Yield(); // TEMP HACK

		var json = Encoding.UTF8.GetString(_rentedBuffer.AsSpan()[.._bufferedBytes]);

		while (_bufferedBytes - _consumed > 0)
		{
			if (TryReadNextItem())
			{
				Debug.Assert(_nextResult is not null);
				var result = _nextResult;
				_nextResult = null;
				yield return result;
			}

			// TODO - Read more from stream at end of buffer
		}
	}

	private Task AdvanceAsync() => Task.CompletedTask;

	private bool TryReadNextItem()
	{
		var readCompleteItem = false;
		var reader = new Utf8JsonReader(_rentedBuffer.AsSpan().Slice(_consumed, _bufferedBytes - _consumed), false, _jsonReaderState);

		while (reader.Read())
		{
			if (reader.CurrentDepth == 2 && reader.TokenType == JsonTokenType.StartObject)
			{
				_nextResult ??= new();
				_nextResult.OperationIndex = _index++;
				continue;
			}

			if (reader.CurrentDepth == 2 && reader.TokenType == JsonTokenType.EndObject)
			{
				readCompleteItem = true;
				break;
			}

			if (reader.CurrentDepth == 4 && _responseMode == ResponseMode.ErrorsAndStatus && !Errors && reader.TokenType == JsonTokenType.Number)
			{
				// This is a faster path when we know there are no errors, and we are using the errors and status filtering
				// In this specific set of circumstances, we can simply read the first numeric value at this depth and be assured we have read the entire item

				Debug.Assert(_nextResult is not null);
				var status = reader.GetInt32();
				_nextResult.StatusCode = status;
				readCompleteItem = true;
				reader.Read();
				reader.Read();
				break;
			}

			if (reader.CurrentDepth == 4 && reader.TokenType == JsonTokenType.Number)
			{
				Debug.Assert(_nextResult is not null);
				var status = reader.GetInt32();
				_nextResult.StatusCode = status;
				break;
			}

			if (reader.CurrentDepth == 3 && reader.TokenType == JsonTokenType.PropertyName)
			{
				Debug.Assert(_nextResult is not null);

				var span = reader.HasValueSequence ? reader.ValueSequence.FirstSpan : reader.ValueSpan;

				if (span.Length == 0)
					throw new JsonException("Invalid operation item action");

				_nextResult.Action = span[0] switch
				{
					LetterCByte => "create",
					LetterDByte => "delete",
					LetterIByte => "index",
					LetterUByte => "update",
					_ => throw new JsonException("Invalid operation item action"),
				};

				continue;
			}

			if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.EndArray)
			{
				// We won't read anything further from the stream as we've reached the end of the items

				_nextResult = null;
				_bufferedBytes = 0;
				Dispose();
				break;
			}
		}

		_jsonReaderState = reader.CurrentState;
		_consumed += (int)reader.BytesConsumed;

		return readCompleteItem;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="body"></param>
	/// <param name="responseMode"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public static async Task<StreamingBulkResponse> CreateAsync(Stream body, ResponseMode responseMode = ResponseMode.All, CancellationToken cancellationToken = default)
	{
		var response = new StreamingBulkResponse(body, responseMode);
		await response.InitialReadAsync(cancellationToken).ConfigureAwait(false);
		return response;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="body"></param>
	/// <param name="responseMode"></param>
	/// <returns></returns>
	public static StreamingBulkResponse Create(Stream body, ResponseMode responseMode = ResponseMode.All)
	{
		var response = new StreamingBulkResponse(body, responseMode);
		response.InitialRead();
		return response;
	}

	private void InitialRead()
	{
		_bufferedBytes = Stream.ReadAtLeast(_rentedBuffer, 7);
		InitialReadCore();
	}

	private async Task InitialReadAsync(CancellationToken cancellationToken = default)
	{
		_bufferedBytes = await Stream.ReadAtLeastAsync(_rentedBuffer, 7, false, cancellationToken).ConfigureAwait(false);
		InitialReadCore();
	}

	private void InitialReadCore()
	{
		// This reads as far as the start of the array, setting the `Took` and `Errors` properties from the JSON.

		if (_bufferedBytes == 0)
			throw new Exception("Response was empty");

		if (CheckForError(_rentedBuffer))
		{
			// TODO
		}

		if (_responseMode == ResponseMode.Errors)
		{
			// TODO - Fast path without the reader to check for the errors property and if false, close early
		}

		var reader = new Utf8JsonReader(_rentedBuffer.AsSpan().Slice(0, _bufferedBytes), false, _jsonReaderState);

		var atItems = false;

		// TODO - Read more of the stream if we don't have enough bytes to find the initial elements

		while (!atItems && reader.Read())
		{
			// TODO - Handle error property

			if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.Number)
			{
				Took = reader.GetInt64();
				continue;
			}

			if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.True)
			{
				Errors = true;
				continue;
			}

			// We avoid a few instructions by checking for false as this is the default value for the property

			if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.StartArray)
			{
				atItems = true;
				continue;
			}
		}

		_jsonReaderState = reader.CurrentState;
		_consumed = (int)reader.BytesConsumed;

		static bool CheckForError(ReadOnlySpan<byte> initialBytes)
		{
			// TODO - If we haven't read at least 7 bytes, we need to advance

			if (initialBytes.Slice(1, 7).SequenceEqual(ErrorBytes))
				return true;

			return false;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="disposing"></param>
	protected override void Dispose(bool disposing)
	{
		if (_rentedBuffer is not null)
			ArrayPool<byte>.Shared.Return(_rentedBuffer);

		base.Dispose(disposing);
	}
}

/// <summary>
/// 
/// </summary>
public sealed class StreamingBulkResponseBuilder : TypedResponseBuilder<StreamingBulkResponse>
{
	/// <summary>
	/// 
	/// </summary>
	/// <param name="apiCallDetails"></param>
	/// <param name="boundConfiguration"></param>
	/// <param name="responseStream"></param>
	/// <param name="contentType"></param>
	/// <param name="contentLength"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	protected override StreamingBulkResponse? Build(ApiCallDetails apiCallDetails, BoundConfiguration boundConfiguration, Stream responseStream, string contentType, long contentLength)
	{
		var responseMode = GetResponseMode(apiCallDetails);
		var response = StreamingBulkResponse.Create(responseStream, responseMode);
		return response;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="apiCallDetails"></param>
	/// <param name="boundConfiguration"></param>
	/// <param name="responseStream"></param>
	/// <param name="contentType"></param>
	/// <param name="contentLength"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	protected override async Task<StreamingBulkResponse?> BuildAsync(
		ApiCallDetails apiCallDetails,
		BoundConfiguration boundConfiguration,
		Stream responseStream,
		string contentType,
		long contentLength,
		CancellationToken cancellationToken = default)
	{
		var responseMode = GetResponseMode(apiCallDetails);
		var response = await StreamingBulkResponse.CreateAsync(responseStream, responseMode, cancellationToken).ConfigureAwait(false);
		return response;
	}

	private static ResponseMode GetResponseMode(ApiCallDetails apiCallDetails)
	{
		var responseMode = ResponseMode.All;

		var query = apiCallDetails.Uri?.Query;

		if (!string.IsNullOrEmpty(query) && query.Contains("filter_path")) // TODO : Optimise
		{
			responseMode = ResponseMode.ErrorsAndStatus; // TODO: Handle other potential options
		}

		return responseMode;
	}
}

/// <summary>
/// 
/// </summary>
public enum ResponseMode
{
	/// <summary>
	/// 
	/// </summary>
	Errors,

	/// <summary>
	/// 
	/// </summary>
	ErrorsAndStatus,

	/// <summary>
	/// 
	/// </summary>
	All
}

/// <summary>
/// 
/// </summary>
public static class TransportExtensions
{
	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	/// <returns></returns>
	public static ElasticsearchBulkWriter GetElasticsearchBulkWriter(
	this ITransport transport) =>
		new(transport, string.Empty, ResponseMode.ErrorsAndStatus);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	/// <param name="target"></param>
	/// <returns></returns>
	public static ElasticsearchBulkWriter GetElasticsearchBulkWriter(
		this ITransport transport, Target target) =>
			new(transport, target, ResponseMode.ErrorsAndStatus);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	/// <param name="responseMode"></param>
	/// <param name="target"></param>
	/// <returns></returns>
	public static ElasticsearchBulkWriter GetElasticsearchBulkWriter(
		this ITransport transport, Target target, ResponseMode responseMode) =>
			new(transport, target, responseMode);
}

/// <summary>
/// 
/// </summary>
public static class Ingest
{
	private sealed class IngestProductRegistration : ElasticsearchProductRegistration
	{
		public IngestProductRegistration() : base(typeof(IngestProductRegistration)) { }

		public override string Name => "elastic-ingest-dotnet";
		public override string? ServiceIdentifier => "ing";

		public static IngestProductRegistration Instance { get; } = new();
	}

	private static DistributedTransport CreateTransport(Uri endpoint, AuthorizationHeader authorization)
	{
		var transportConfiguration = new TransportConfiguration(endpoint, IngestProductRegistration.Instance)
		{
			Authentication = authorization
		};

		return new DistributedTransport(transportConfiguration);
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="endpoint"></param>
	/// <param name="authorization"></param>
	/// <returns></returns>
	public static ElasticsearchBulkWriter GetElasticsearchBulkWriter(Uri endpoint, AuthorizationHeader authorization) =>
		GetElasticsearchBulkWriter(endpoint, authorization, Target.Empty, ResponseMode.ErrorsAndStatus);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="endpoint"></param>
	/// <param name="authorization"></param>
	/// <param name="target"></param>
	/// <returns></returns>
	public static ElasticsearchBulkWriter GetElasticsearchBulkWriter(Uri endpoint, AuthorizationHeader authorization, Target target) =>
		GetElasticsearchBulkWriter(endpoint, authorization, target, ResponseMode.ErrorsAndStatus);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="endpoint"></param>
	/// <param name="authorization"></param>
	/// <param name="target"></param>
	/// <param name="responseMode"></param>
	/// <returns></returns>
	public static ElasticsearchBulkWriter GetElasticsearchBulkWriter(Uri endpoint, AuthorizationHeader authorization, Target target, ResponseMode responseMode)
	{
		var transport = CreateTransport(endpoint, authorization);
		return transport.GetElasticsearchBulkWriter(target, responseMode);
	}
}

/// <summary>
/// TODO
/// </summary>
/// <remarks>
/// 
/// </remarks>
/// <param name="transport"></param>
/// <param name="target"></param>
/// <param name="responseMode"></param>
/// <param name="cancellationToken"></param>
public sealed class ElasticsearchBulkWriter(
	ITransport transport,
	in Target target,
	in ResponseMode responseMode,
	CancellationToken cancellationToken = default) : TransportWriter<StreamingBulkResponse>(transport, cancellationToken)
{
	private readonly Target _target = target;
	private readonly ResponseMode _responseMode = responseMode;
	private string? _pathAndQuery;
	private int _operationCount;

	private static readonly IRequestConfiguration DefaultRequestConfiguration = new RequestConfiguration
	{
		ResponseBuilders = [new StreamingBulkResponseBuilder()],
		UserAgent = UserAgent.Create("elastic-ingest-dotnet", typeof(ElasticsearchBulkWriter)),
		DisableAuditTrail = true
	};

	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	public ElasticsearchBulkWriter(ITransport transport) : this(transport, Target.Empty, ResponseMode.ErrorsAndStatus)
	{
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	/// <param name="responseMode"></param>
	public ElasticsearchBulkWriter(ITransport transport, ResponseMode responseMode) : this(transport, Target.Empty, responseMode)
	{
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	/// <param name="target"></param>
	public ElasticsearchBulkWriter(ITransport transport, Target target) : this(transport, target, ResponseMode.ErrorsAndStatus)
	{
	}

	/// <inheritdoc />
	protected override IRequestConfiguration? RequestConfiguration { get; } =
		transport is ITransport<ITransportConfiguration> t ? BoundConfiguration.Create(t, DefaultRequestConfiguration) : DefaultRequestConfiguration;

	/// <inheritdoc />
	protected override HttpMethod HttpMethod => HttpMethod.POST;

	/// <inheritdoc />
	protected override Action<Activity>? ConfigureActivity => activity =>
	{
		activity.DisplayName = !_target.IsEmpty ? $"Elasticsearch bulk request to {_target}" : "Elasticsearch bulk request";

		if (!activity.IsAllDataRequested)
			return;

		activity.AddTag("db.operation.name", "bulk");

		if (!_target.IsEmpty)
		{
			activity.AddTag("db.elasticsearch.path_parts.target", _target.Value);
		}

		if (_operationCount > 1)
		{
			activity.AddTag("db.operation.batch.size", _operationCount);
		}
	};

	/// <inheritdoc />
	protected override string PathAndQuery
	{
		get
		{
			if (_pathAndQuery is not null)
				return _pathAndQuery;

			if (_responseMode == ResponseMode.All)
			{
				return _pathAndQuery = !_target.IsEmpty ? $"/{_target}/_bulk" : "/_bulk";
			}

			return _pathAndQuery = $"{(!_target.IsEmpty ? $"/{_target}/_bulk" : "/_bulk")}?" +
				$"filter_path={(_responseMode == ResponseMode.ErrorsAndStatus
					? "errors,took,error,items.*.status,items.*.error"
					: "errors,took,error,items.*.error")}";
		}
	}

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="target"></param>
	/// <param name="requireAlias"></param>
	/// <param name="listExecutedPipelines"></param>
	/// <param name="dynamicTemplates"></param>
	public async Task WriteCreateOperationAsync(
		ReadOnlyMemory<byte> operationPayload,
		string? id = null,
		string? target = null,
		bool? requireAlias = false,
		bool? listExecutedPipelines = false,
		IReadOnlyDictionary<string, string>? dynamicTemplates = null)
	{
		if (true)
		{

		}

		await WriteAsync(operationPayload).ConfigureAwait(false);
	}

	// TODO - Experimental attribute
	/// <summary>
	/// 
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public async ValueTask WriteIndexOperationAsync(ReadOnlyMemory<byte> operationPayload, CancellationToken cancellationToken = default)
	{
		var lengthRequired = OperationHeaderWriter.PlainIndexBytesSpan.Length + 1;
		lengthRequired += operationPayload.Length + 1;

		//Writer.Write

		await WriteAsync(static (memory, state) =>
		{
			var totalWritten = 0;

			if (!OperationHeaderWriter.TryWriteIndexOperationHeader(memory, out var written))
				return totalWritten;

			totalWritten += written;

			state.Span.CopyTo(memory.Span[totalWritten..]);
			totalWritten += state.Span.Length;
			memory.Span[totalWritten++] = OperationHeaderWriter.NewLineByte;

			return totalWritten;
		},
		operationPayload, lengthRequired, cancellationToken).ConfigureAwait(false);

		Interlocked.Increment(ref _operationCount);
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public ValueTask WriteIndexOperationAsync(
		in ReadOnlyMemory<byte> operationPayload,
		in Id id,
		CancellationToken cancellationToken = default) =>
			WriteIndexOperationAsync(operationPayload, id, Target.Empty, RequireAlias.False, ListExecutedPipelines.False, DynamicTemplateMappings.Empty, cancellationToken);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="target"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public ValueTask WriteIndexOperationAsync(
		in ReadOnlyMemory<byte> operationPayload,
		in Target target,
		CancellationToken cancellationToken = default) =>
			WriteIndexOperationAsync(operationPayload, Id.Empty, target, RequireAlias.False, ListExecutedPipelines.False, DynamicTemplateMappings.Empty, cancellationToken);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="target"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public ValueTask WriteIndexOperationAsync(
		in ReadOnlyMemory<byte> operationPayload,
		in Id id,
		in Target target,
		CancellationToken cancellationToken = default) =>
			WriteIndexOperationAsync(operationPayload, id, target, RequireAlias.False, ListExecutedPipelines.False, DynamicTemplateMappings.Empty, cancellationToken);

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="bulkIndexOptions"></param>
	/// <param name="cancellationToken"></param>
	public ValueTask WriteIndexOperationAsync(
		in ReadOnlyMemory<byte> operationPayload,
		in BulkIndexOptions bulkIndexOptions,
		in CancellationToken cancellationToken = default) =>
			WriteIndexOperationAsync(
				operationPayload,
				bulkIndexOptions.Id,
				bulkIndexOptions.Target,
				bulkIndexOptions.RequireAlias,
				bulkIndexOptions.ListExecutedPipelines,
				bulkIndexOptions.DynamicTemplateMappings,
				cancellationToken);

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="target"></param>
	/// <param name="requireAlias"></param>
	/// <param name="listExecutedPipelines"></param>
	/// <param name="dynamicTemplateMappings"></param>
	/// <param name="cancellationToken"></param>
	private async ValueTask WriteIndexOperationAsync(
		ReadOnlyMemory<byte> operationPayload,
		Id id,
		Target target,
		RequireAlias requireAlias,
		ListExecutedPipelines listExecutedPipelines,
		DynamicTemplateMappings dynamicTemplateMappings,
		CancellationToken cancellationToken = default)
	{
		var lengthRequired = OperationHeaderWriter.CalculateIndexOperationHeaderLength(id, target, requireAlias, listExecutedPipelines, dynamicTemplateMappings);

		lengthRequired += operationPayload.Length + 1; // include 1 byte for newline at the end of the operation

		await WriteAsync(static (memory, state) =>
		{
			var totalWritten = 0;

			var (payload, id, target, requireAlias, listExecutedPipelines, dynamicTemplates) = state;

			if (!OperationHeaderWriter.TryWriteIndexOperationHeader(memory, id, target, requireAlias, listExecutedPipelines, dynamicTemplates, out var written))
				return totalWritten;

			totalWritten += written;

			payload.Span.CopyTo(memory.Span[totalWritten..]);
			totalWritten += payload.Span.Length;

			memory.Span[totalWritten] = OperationHeaderWriter.NewLineByte;
			totalWritten++;

			return totalWritten;
		},
		(operationPayload, id, target, requireAlias, listExecutedPipelines, dynamicTemplateMappings), lengthRequired, cancellationToken).ConfigureAwait(false);

		Interlocked.Increment(ref _operationCount);
	}

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="target"></param>
	/// <param name="requireAlias"></param>
	/// <param name="listExecutedPipelines"></param>
	/// <param name="dynamicTemplates"></param>
	public void WriteIndexOperation(
		ReadOnlySpan<byte> operationPayload,
		string? id = null,
		string? target = null,
		bool? requireAlias = false,
		bool? listExecutedPipelines = false,
		IReadOnlyDictionary<string, string>? dynamicTemplates = null)
	{
		if (true)
		{

		}

		// This is temp code
		Write(OperationHeaderWriter.PlainIndexBytesSpan);
		Write([(byte)'\n']);
		Write(operationPayload);
		Write([(byte)'\n']);
	}

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="target"></param>
	/// <param name="requireAlias"></param>
	/// <param name="cancellationToken"></param>
	public async Task WriteDeleteOperationAsync(
		ReadOnlyMemory<byte> operationPayload,
		string? id = null,
		string? target = null,
		bool? requireAlias = false,
		CancellationToken cancellationToken = default)
	{
		if (true)
		{

		}

		await WriteAsync(operationPayload, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="operationPayload"></param>
	/// <param name="id"></param>
	/// <param name="target"></param>
	/// <param name="requireAlias"></param>
	public async Task WriteUpdateOperationAsync(
		ReadOnlyMemory<byte> operationPayload,
		string? id = null,
		string? target = null,
		bool? requireAlias = false)
	{
		if (true)
		{

		}

		await WriteAsync(operationPayload).ConfigureAwait(false);
	}
}
#endif
