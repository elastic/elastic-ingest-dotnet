// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Elastic.Channels.Buffers;
using Elastic.Channels.Diagnostics;

namespace Elastic.Channels;

/// <summary> Represents a buffered channel implementation</summary>
/// <typeparam name="TEvent">The type of data to be written</typeparam>
public interface IBufferedChannel<in TEvent> : IDisposable
{
	/// <summary>
	/// Tries to write <paramref name="item"/> to the inbound channel.
	/// <para>Returns immediately if successful or unsuccessful</para>
	/// </summary>
	/// <returns>A bool indicating if the write was successful</returns>
	bool TryWrite(TEvent item);

	/// <summary>
	/// Waits for availability on the inbound channel before attempting to write <paramref name="item"/>.
	/// </summary>
	/// <returns>A bool indicating if the write was successful</returns>
	Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default);

	/// <summary>
	/// Waits for availability on the inbound channel before attempting to write each item in <paramref name="events"/>.
	/// </summary>
	/// <returns>A bool indicating if all writes werwase successful</returns>
	Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default);

	/// <summary>
	/// Tries to write many <paramref name="events"/> to the channel returning true if ALL messages were written succesfully
	/// </summary>
	bool TryWriteMany(IEnumerable<TEvent> events);

	/// <inheritdoc cref="IChannelDiagnosticsListener"/>
	IChannelDiagnosticsListener? DiagnosticsListener { get; }
}

/// <summary>
/// The common base implementation of <see cref="IBufferedChannel{TEvent}"/> that all implementations inherit from.
/// <para>This sets up the <see cref="InChannel"/> and <see cref="OutChannel"/> and the implementation that coordinates moving
/// data from one to the other</para>
/// </summary>
/// <typeparam name="TChannelOptions">Concrete channel options implementation</typeparam>
/// <typeparam name="TEvent">The type of data we are looking to <see cref="ExportAsync"/></typeparam>
/// <typeparam name="TResponse">The type of responses we are expecting to get back from <see cref="ExportAsync"/></typeparam>
public abstract class BufferedChannelBase<TChannelOptions, TEvent, TResponse>
	: ChannelWriter<TEvent>, IBufferedChannel<TEvent>
	where TChannelOptions : ChannelOptionsBase<TEvent, TResponse>
	where TResponse : class, new()
{
	private readonly Task _inThread;
	private readonly Task _outThread;
	private readonly SemaphoreSlim _throttleTasks;
	private readonly CountdownEvent? _signal;

	private readonly ChannelCallbackInvoker<TEvent, TResponse> _callbacks;

	/// <inheritdoc cref="IChannelDiagnosticsListener"/>
	public IChannelDiagnosticsListener? DiagnosticsListener { get; }

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	protected BufferedChannelBase(TChannelOptions options) : this(options, null) { }

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	protected BufferedChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, TResponse>>? callbackListeners)
	{
		TokenSource = new CancellationTokenSource();
		Options = options;

		var listeners = callbackListeners == null ? new[] { Options } : callbackListeners.Concat(new[] { Options }).ToArray();
		DiagnosticsListener = listeners
			.Select(l => (l is IChannelDiagnosticsListener c) ? c : null)
			.FirstOrDefault(e=> e != null);
		if (DiagnosticsListener == null && !options.DisableDiagnostics)
		{
			// if no debug listener was already provided but was requested explicitly create one.
			var l =  new ChannelDiagnosticsListener<TEvent, TResponse>(GetType().Name);
			DiagnosticsListener = l;
			listeners = listeners.Concat(new[] { l }).ToArray();
		}
		_callbacks = new ChannelCallbackInvoker<TEvent, TResponse>(listeners);

		var maxConsumers = Math.Max(1, BufferOptions.ExportMaxConcurrency);
		_throttleTasks = new SemaphoreSlim(maxConsumers, maxConsumers);
		_signal = options.BufferOptions.WaitHandle;
		var maxIn = Math.Max(1, BufferOptions.InboundBufferMaxSize);
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
		// The minimum out buffer the max of (1 or OutboundBufferMaxSize) as long as it does not exceed InboundBufferMaxSize
		var maxOut = Math.Min(BufferOptions.InboundBufferMaxSize, Math.Max(1, BufferOptions.OutboundBufferMaxSize));
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

		InboundBuffer = new InboundBuffer<TEvent>(maxOut, BufferOptions.OutboundBufferMaxLifetime);

		_outThread = Task.Factory.StartNew(async () =>
			await ConsumeOutboundEventsAsync().ConfigureAwait(false),
				CancellationToken.None,
				TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness,
				TaskScheduler.Default);

		_inThread = Task.Factory.StartNew(async () =>
			await ConsumeInboundEventsAsync(maxOut, BufferOptions.OutboundBufferMaxLifetime).ConfigureAwait(false),
				CancellationToken.None,
				TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness,
				TaskScheduler.Default);
	}

	/// <summary>
	/// All subclasses of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/> need to at a minimum
	/// implement this method to export buffered collection of <see cref="OutChannel"/>
	/// </summary>
	protected abstract Task<TResponse> ExportAsync(ArraySegment<TEvent> buffer, CancellationToken ctx = default);

	/// <summary>The channel options currently in use</summary>
	public TChannelOptions Options { get; }

	private CancellationTokenSource TokenSource { get; }
	private Channel<IOutboundBuffer<TEvent>> OutChannel { get; }
	private Channel<TEvent> InChannel { get; }
	private BufferOptions BufferOptions => Options.BufferOptions;

	internal InboundBuffer<TEvent> InboundBuffer { get; }

	/// <inheritdoc cref="ChannelWriter{T}.WaitToWriteAsync"/>
	public override ValueTask<bool> WaitToWriteAsync(CancellationToken ctx = default) => InChannel.Writer.WaitToWriteAsync(ctx);

	/// <inheritdoc cref="ChannelWriter{T}.TryComplete"/>
	public override bool TryComplete(Exception? error = null) => InChannel.Writer.TryComplete(error);

	/// <inheritdoc cref="ChannelWriter{T}.TryWrite"/>
	public override bool TryWrite(TEvent item)
	{
		if (InChannel.Writer.TryWrite(item))
		{
			_callbacks.PublishToInboundChannelCallback?.Invoke();
			return true;
		}
		_callbacks.PublishToInboundChannelFailureCallback?.Invoke();
		return false;
	}


	/// <inheritdoc cref="IBufferedChannel{TEvent}.WaitToWriteManyAsync"/>
	public async Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default)
	{
		var allWritten = true;
		foreach (var e in events)
		{
			var written = await WaitToWriteAsync(e, ctx).ConfigureAwait(false);
			if (!written) allWritten = written;
		}
		return allWritten;
	}

	/// <inheritdoc cref="IBufferedChannel{TEvent}.TryWriteMany"/>
	public bool TryWriteMany(IEnumerable<TEvent> events) =>
		events.Select(e => TryWrite(e)).All(b => b);

	/// <inheritdoc cref="ChannelWriter{T}.WaitToWriteAsync"/>
	public virtual async Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;
		if (await InChannel.Writer.WaitToWriteAsync(ctx).ConfigureAwait(false) &&
		    InChannel.Writer.TryWrite(item))
		{
			_callbacks.PublishToInboundChannelCallback?.Invoke();
			return true;
		}
		_callbacks.PublishToInboundChannelFailureCallback?.Invoke();
		return false;
	}

	/// <summary>
	/// Subclasses may override this to yield items from <typeparamref name="TResponse"/> that can be retried.
	/// <para>The default implementation of this simply always returns an empty collection</para>
	/// </summary>
	protected virtual ArraySegment<TEvent> RetryBuffer(TResponse response,
		ArraySegment<TEvent> currentBuffer,
		IWriteTrackingBuffer statistics
	) => EmptyArraySegments<TEvent>.Empty;

	private async Task ConsumeOutboundEvents()
	{
		_callbacks.OutboundChannelStartedCallback?.Invoke();

		var maxConsumers = Options.BufferOptions.ExportMaxConcurrency;
		var taskList = new List<Task>(maxConsumers);

		while (await OutChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
			// ReSharper disable once RemoveRedundantBraces
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			while (OutChannel.Reader.TryRead(out var buffer))
			{
				using (buffer)
				{
					var items = buffer.GetArraySegment();
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
		}
		await Task.WhenAll(taskList).ConfigureAwait(false);
		_callbacks.OutboundChannelExitedCallback?.Invoke();
	}

	private async Task ExportBuffer(ArraySegment<TEvent> items, IOutboundBuffer<TEvent> buffer)
	{
		var maxRetries = Options.BufferOptions.ExportMaxRetries;
		for (var i = 0; i <= maxRetries && items.Count > 0; i++)
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			_callbacks.ExportItemsAttemptCallback?.Invoke(i, items.Count);
			TResponse? response;
			try
			{
				response = await ExportAsync(items, TokenSource.Token).ConfigureAwait(false);
				_callbacks.ExportResponseCallback?.Invoke(response, buffer);
			}
			catch (Exception e)
			{
				_callbacks.ExportExceptionCallback?.Invoke(e);
				break;
			}

			items = RetryBuffer(response, items, buffer);
			if (items.Count > 0 && i == 0)
				_callbacks.ExportRetryableCountCallback?.Invoke(items.Count);

			// delay if we still have items and we are not at the end of the max retry cycle
			var atEndOfRetries = i == maxRetries;
			if (items.Count > 0 && !atEndOfRetries)
			{
				await Task.Delay(Options.BufferOptions.ExportBackoffPeriod(i), TokenSource.Token).ConfigureAwait(false);
				_callbacks.ExportRetryCallback?.Invoke(items);
			}
			// otherwise if retryable items still exist and the user wants to be notified notify the user
			else if (items.Count > 0 && atEndOfRetries)
				_callbacks.ExportMaxRetriesCallback?.Invoke(items);
		}
		_callbacks.ExportBufferCallback?.Invoke();
		if (_signal is { IsSet: false })
			_signal.Signal();
	}

	private async Task ConsumeInboundEvents(int maxQueuedMessages, TimeSpan maxInterval)
	{
		_callbacks.InboundChannelStartedCallback?.Invoke();
		while (await InboundBuffer.WaitToReadAsync(InChannel.Reader).ConfigureAwait(false))
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			while (InboundBuffer.Count < maxQueuedMessages && InChannel.Reader.TryRead(out var item))
			{
				InboundBuffer.Add(item);

				if (InboundBuffer.DurationSinceFirstWaitToRead >= maxInterval)
					break;
			}

			if (InboundBuffer.NoThresholdsHit) continue;

			var outboundBuffer = new OutboundBuffer<TEvent>(InboundBuffer);

			if (await PublishAsync(outboundBuffer).ConfigureAwait(false))
				_callbacks.PublishToOutboundChannelCallback?.Invoke();
			else
				_callbacks.PublishToOutboundChannelFailureCallback?.Invoke();
		}
	}

	private ValueTask<bool> PublishAsync(IOutboundBuffer<TEvent> buffer)
	{
		async Task<bool> AsyncSlowPath(IOutboundBuffer<TEvent> b)
		{
			var maxRetries = Options.BufferOptions.ExportMaxRetries;
			for (var i = 0; i <= maxRetries; i++)
				while (await OutChannel.Writer.WaitToWriteAsync().ConfigureAwait(false))
				{
					if (OutChannel.Writer.TryWrite(b))
						return true;
				}
			return false;
		}

		return OutChannel.Writer.TryWrite(buffer)
			? new ValueTask<bool>(true)
			: new ValueTask<bool>(AsyncSlowPath(buffer));
	}

	/// <inheritdoc cref="object.ToString"/>>
	public override string ToString() =>
		DiagnosticsListener != null ? DiagnosticsListener.ToString() : base.ToString();

	/// <inheritdoc cref="IDisposable.Dispose"/>
	public virtual void Dispose()
	{
		InboundBuffer.Dispose();
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
