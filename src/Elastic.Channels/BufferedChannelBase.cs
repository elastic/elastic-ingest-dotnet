// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Elastic.Channels;

public interface IBufferedChannel<in TEvent> : IDisposable
{
	bool TryWrite(TEvent item);

	Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default);

	async Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default)
	{
		var allWritten = true;
		foreach (var e in events)
		{
			var written = await WaitToWriteAsync(e, ctx).ConfigureAwait(false);
			if (!written) allWritten = written;
		}
		return allWritten;
	}
}

public abstract class BufferedChannelBase<TEvent, TResponse> : BufferedChannelBase<ChannelOptionsBase<TEvent, TResponse>, TEvent, TResponse>
	where TResponse : class, new()
{
	protected BufferedChannelBase(ChannelOptionsBase<TEvent, TResponse> options) : base(options) { }
}

public abstract class BufferedChannelBase<TChannelOptions, TEvent, TResponse>
	: ChannelWriter<TEvent>, IBufferedChannel<TEvent>
	where TChannelOptions : ChannelOptionsBase<TEvent, TResponse>
	where TResponse : class, new()
{
	private readonly Task _inThread;
	private readonly Task _outThread;
	private readonly SemaphoreSlim _throttleTasks;
	private readonly CountdownEvent? _signal;

	protected BufferedChannelBase(TChannelOptions options)
	{
		TokenSource = new CancellationTokenSource();
		Options = options;
		var maxConsumers = Math.Max(1, BufferOptions.ConcurrentConsumers);
		_throttleTasks = new SemaphoreSlim(maxConsumers, maxConsumers);
		_signal = options.BufferOptions.WaitHandle;
		var maxIn = Math.Max(1, BufferOptions.MaxInFlightMessages);
		InChannel = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(maxIn)
		{
			SingleReader = false,
			SingleWriter = false,
			// Stephen Toub comment: https://github.com/dotnet/runtime/issues/26338#issuecomment-393720727
			// AFAICT this is fine since we run in a dedicated long running task.
			AllowSynchronousContinuations = true,
			// wait does not block it simply signals that Writer.TryWrite should return false and be retried
			// DropWrite will make `TryWrite` always return true, which is not what we want.
			FullMode = BoundedChannelFullMode.Wait
		});
		// The minimum out buffer the max of (1 or MaxConsumerBufferSize) as long as it does not exceed MaxInFlightMessages
		var maxOut = Math.Min(BufferOptions.MaxInFlightMessages, Math.Max(1, BufferOptions.MaxConsumerBufferSize));
		OutChannel = Channel.CreateBounded<IOutboundBuffer<TEvent>>(
			new BoundedChannelOptions(maxOut)
			{
				SingleReader = false,
				SingleWriter = true,
				// Stephen Toub comment: https://github.com/dotnet/runtime/issues/26338#issuecomment-393720727
				// AFAICT this is fine since we run in a dedicated long running task.
				AllowSynchronousContinuations = true,
				// wait does not block it simply signals that Writer.TryWrite should return false and be retried
				// DropWrite will make `TryWrite` always return true, which is not what we want.
				FullMode = BoundedChannelFullMode.Wait
			});

		var waitHandle = BufferOptions.WaitHandle;
		_inThread = Task.Factory.StartNew(async () =>
				await ConsumeInboundEvents(maxOut, BufferOptions.MaxConsumerBufferLifetime)
					.ConfigureAwait(false)
			, TaskCreationOptions.LongRunning
		);

		_outThread = Task.Factory.StartNew(async () => await ConsumeOutboundEvents().ConfigureAwait(false), TaskCreationOptions.LongRunning);
	}

	public TChannelOptions Options { get; }

	private CancellationTokenSource TokenSource { get; }
	protected Channel<IOutboundBuffer<TEvent>> OutChannel { get; }
	protected Channel<TEvent> InChannel { get; }
	protected BufferOptions BufferOptions => Options.BufferOptions;

	public override ValueTask<bool> WaitToWriteAsync(CancellationToken ctx = default) => InChannel.Writer.WaitToWriteAsync(ctx);

	public override bool TryComplete(Exception? error = null) => InChannel.Writer.TryComplete(error);

	public override bool TryWrite(TEvent item)
	{
		if (InChannel.Writer.TryWrite(item)) return true;

		Options.PublishRejectionCallback?.Invoke(item);
		return false;
	}

	public virtual async Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;
		if (await InChannel.Writer.WaitToWriteAsync(ctx).ConfigureAwait(false) &&
		    InChannel.Writer.TryWrite(item)) return true;

		Options.PublishRejectionCallback?.Invoke(item);
		return false;
	}

	protected abstract Task<TResponse> Send(IReadOnlyCollection<TEvent> buffer, CancellationToken ctx = default);

	private static readonly IReadOnlyCollection<TEvent> DefaultRetryBuffer = new TEvent[] { };

	protected virtual IReadOnlyCollection<TEvent> RetryBuffer(
		TResponse response,
		IReadOnlyCollection<TEvent> currentBuffer,
		IWriteTrackingBuffer statistics
	) => DefaultRetryBuffer;

	private async Task ConsumeOutboundEvents()
	{
		var maxConsumers = Options.BufferOptions.ConcurrentConsumers;
		var taskList = new List<Task>(maxConsumers);

		while (await OutChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
			// ReSharper disable once RemoveRedundantBraces
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			while (OutChannel.Reader.TryRead(out var buffer))
			{
				var items = buffer.Items;
				await _throttleTasks.WaitAsync().ConfigureAwait(false);
				var t = ExportBuffer(items, buffer);
				taskList.Add(t);

				if (taskList.Count >= maxConsumers)
				{
					var completedTask = await Task.WhenAny(taskList).ConfigureAwait(false);
					taskList.Remove(completedTask);
				}
				_throttleTasks.Release();
			}
		}
		await Task.WhenAll(taskList).ConfigureAwait(false);
	}

	private async Task ExportBuffer(IReadOnlyCollection<TEvent> items, IWriteTrackingBuffer buffer)
	{
		var maxRetries = Options.BufferOptions.MaxRetries;
		for (var i = 0; i <= maxRetries && items.Count > 0; i++)
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			Options.BulkAttemptCallback?.Invoke(i, items.Count);
			TResponse? response;
			try
			{
				response = await Send(items, TokenSource.Token).ConfigureAwait(false);
				Options.ResponseCallback?.Invoke(response, buffer);
			}
			catch (Exception e)
			{
				Options.ExceptionCallback?.Invoke(e);
				break;
			}

			items = RetryBuffer(response, items, buffer);

			// delay if we still have items and we are not at the end of the max retry cycle
			var atEndOfRetries = i == maxRetries;
			if (items.Count > 0 && !atEndOfRetries)
			{
				await Task.Delay(Options.BufferOptions.BackoffPeriod(i), TokenSource.Token).ConfigureAwait(false);
				Options.RetryCallBack?.Invoke(items);
			}
			// otherwise if retryable items still exist and the user wants to be notified notify the user
			else if (items.Count > 0 && atEndOfRetries)
				Options.MaxRetriesExceededCallback?.Invoke(items);
		}
		Options.BufferOptions.BufferFlushCallback?.Invoke();
		if (_signal is { IsSet: false })
			_signal.Signal();
	}

	private async Task ConsumeInboundEvents(int maxQueuedMessages, TimeSpan maxInterval)
	{
		using var inboundBuffer = new InboundBuffer<TEvent>(maxQueuedMessages, maxInterval);

		while (await inboundBuffer.WaitToReadAsync(InChannel.Reader).ConfigureAwait(false))
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			while (inboundBuffer.Count < maxQueuedMessages && InChannel.Reader.TryRead(out var item))
			{
				inboundBuffer.Add(item);

				if (inboundBuffer.DurationSinceFirstWaitToRead >= maxInterval)
					break;
			}

			if (inboundBuffer.NoThresholdsHit) continue;

			var outboundBuffer = new OutboundBuffer<TEvent>(inboundBuffer);
			inboundBuffer.Reset();

			if (await PublishAsync(outboundBuffer).ConfigureAwait(false))
				continue;

			foreach (var e in inboundBuffer.Buffer)
				Options.PublishRejectionCallback?.Invoke(e);
		}
	}

	private ValueTask<bool> PublishAsync(IOutboundBuffer<TEvent> buffer)
	{
		async Task<bool> AsyncSlowPath(IOutboundBuffer<TEvent> b)
		{
			var maxRetries = Options.BufferOptions.MaxRetries;
			for (var i = 0; i <= maxRetries; i++)
				while (await OutChannel.Writer.WaitToWriteAsync().ConfigureAwait(false))
				{
					Options.OutboundChannelRetryCallback?.Invoke(i);
					if (OutChannel.Writer.TryWrite(b))
						return true;
				}
			return false;
		}

		return OutChannel.Writer.TryWrite(buffer)
			? new ValueTask<bool>(true)
			: new ValueTask<bool>(AsyncSlowPath(buffer));
	}

	public virtual void Dispose()
	{
		try
		{
			TokenSource.Cancel();
			InChannel.Writer.TryComplete();
			OutChannel.Writer.TryComplete();
		}
		catch
		{
			// ignored
		}
		try
		{
			_inThread.Dispose();
		}
		catch
		{
			// ignored
		}
		try
		{
			_outThread.Dispose();
		}
		catch
		{
			// ignored
		}
	}
}
