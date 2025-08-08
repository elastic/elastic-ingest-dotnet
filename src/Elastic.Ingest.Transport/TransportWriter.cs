// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Transport;

/// <summary>
/// TODO
/// </summary>
public abstract class TransportWriter<TResponse> where TResponse : TransportResponse, new()
{
	private const int DefaultMinimumFlushSize = 1024;

	private readonly Pipe _pipe = new();
	private readonly Task<TResponse> _requestTask;

	private readonly struct StreamHandlerState
	{
		public readonly required PipeReader PipeReader { get; init; }
		public readonly required int MinimumFlushSize { get; init; }
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="transport"></param>
	/// <param name="cancellationToken"></param>
	protected TransportWriter(ITransport transport, CancellationToken cancellationToken = default)
		: this(transport, DefaultMinimumFlushSize, cancellationToken)
	{
	}

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="transport"></param>
	/// <param name="minimumFlushSize"></param>
	/// <param name="cancellationToken"></param>
	protected TransportWriter(ITransport transport, int minimumFlushSize, CancellationToken cancellationToken = default)
	{
		var state = new StreamHandlerState { PipeReader = _pipe.Reader, MinimumFlushSize = minimumFlushSize };

		// We don't implement the sync action as we only require the async usage here
		var postData = PostData.StreamHandler(state, (_, _) => { }, static async (state, stream, ct) =>
			{
				var written = 0;
				var pipeReader = state.PipeReader;

				while (true)
				{
					var readResult = await pipeReader.ReadAsync(ct).ConfigureAwait(false);

					var buffer = readResult.Buffer;

					if (readResult.IsCompleted)
						break;

					if (buffer.IsSingleSegment)
					{
						stream.Write(buffer.FirstSpan);
						written += buffer.FirstSpan.Length;
					}
					else
					{
						foreach (var segment in buffer)
						{
							stream.Write(segment.Span);
							written += segment.Span.Length;
						}
					}

					if (written > state.MinimumFlushSize)
					{
						await stream.FlushAsync(ct).ConfigureAwait(false);
						written = 0;
					}

					pipeReader.AdvanceTo(buffer.End);
				}

				if (written > 0)
					await stream.FlushAsync(ct).ConfigureAwait(false);
			});

		_requestTask = transport.RequestAsync<TResponse>(new EndpointPath(HttpMethod, PathAndQuery), postData, ConfigureActivity, RequestConfiguration, cancellationToken);
	}

	/// <summary>
	/// TODO
	/// </summary>
	protected abstract HttpMethod HttpMethod { get; }

	/// <summary>
	/// TODO
	/// </summary>
	protected abstract string PathAndQuery { get; }

	/// <summary>
	/// 
	/// </summary>
	protected virtual IRequestConfiguration? RequestConfiguration { get; } = null;

	/// <summary>
	/// TODO
	/// </summary>
	protected virtual Action<Activity>? ConfigureActivity { get; }

	/// <summary>
	/// TODO
	/// </summary>
	protected IBufferWriter<byte> Writer => _pipe.Writer;

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="data"></param>
	protected void Write(ReadOnlySpan<byte> data) => _pipe.Writer.Write(data);

	///// <summary>
	///// TODO
	///// </summary>
	///// <param name="sizeHint"></param>
	///// <returns></returns>
	//protected Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

	///// <summary>
	///// TODO
	///// </summary>
	///// <param name="sizeHint"></param>
	///// <returns></returns>
	//protected Memory<byte> Advance(int bytes) => _pipe.Writer.Advance(bytes);

	/// <summary>
	/// Writes data into the underlying <see cref="PipeWriter" /> using the provided <paramref name="writer"/> function before advancing and flushing.
	/// </summary>
	/// <param name="writer"></param>
	/// <param name="state"></param>
	/// <param name="size"></param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	protected async Task WriteAsync<T>(Func<Memory<byte>, T, int> writer, T state, int size, CancellationToken cancellationToken = default)
	{
		var memory = _pipe.Writer.GetMemory(size);
		var written = writer.Invoke(memory, state);
		_pipe.Writer.Advance(written);
		await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	// TODO - Consider a Write sync method accepting a span, which would skip the flushing.

	/// <summary>
	/// TODO
	/// </summary>
	/// <param name="data"></param>
	/// <param name="ct"></param>
	/// <returns></returns>
	protected ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
		_pipe.Writer.WriteAsync(data, ct);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
		_pipe.Writer.FlushAsync(cancellationToken);

	/// <summary>
	/// 
	/// </summary>
	/// <returns></returns>
	public async Task<TResponse> CompleteAsync() // TODO - Cancellation?
	{
		await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks: This warning affects UI apps and shouldn't apply here as we control everything
		return await _requestTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003 // Avoid awaiting foreign Tasks: This warning affects UI apps and shouldn't apply here as we control everything
	}
}
#endif



