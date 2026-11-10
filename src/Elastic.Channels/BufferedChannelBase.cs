// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Globalization;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	/// <returns>A bool indicating if all writes were successful</returns>
	Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default);

	/// <summary>
	/// Tries to write many <paramref name="events"/> to the channel returning true if ALL messages were written successfully
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
	where TEvent : class
{
	private readonly Task _inTask;
	private readonly Task _outTask;
	private readonly SemaphoreSlim _throttleExportTasks;
	private readonly CountdownEvent? _signal;

	private readonly ChannelCallbackInvoker<TEvent, TResponse> _callbacks;

	/// <inheritdoc cref="IChannelDiagnosticsListener"/>
	public IChannelDiagnosticsListener? DiagnosticsListener { get; }

	/// <summary>The channel options currently in use</summary>
	public TChannelOptions Options { get; }

	/// <summary> An overall cancellation token that may be externally provided </summary>
	protected CancellationTokenSource TokenSource { get; }

	/// <summary>Internal cancellation token for signalling that all publishing activity has completed.</summary>
	private readonly CancellationTokenSource _exitCancelSource = new();

	private Channel<IOutboundBuffer<TEvent>> OutChannel { get; }
	private Channel<TEvent?> InChannel { get; }
	private BufferOptions BufferOptions => Options.BufferOptions;

	private long _inflightEvents;
	/// <summary> The number of inflight events </summary>
	public long InflightEvents => _inflightEvents;

	// Items that were accepted and have not reached an end yet: exported (also after retries), reported as exhausted, or dropped.
	// Unlike _inflightEvents, which stops counting an item when it moves to the inbound buffer, this covers the whole way through
	// the inbound buffer, the outbound channel and the export. Only when it reaches zero is the channel drained.
	private long _pendingItems;
	private bool IsIdle => Interlocked.Read(ref _pendingItems) == 0;

	// Where the channel is in its life. Running until TryComplete (or WaitForDrainAsync, which completes) closes the inbound channel,
	// Completing until the last accepted item reached an end, then Drained. Dispose ends it from any state.
	private int _phase = (int)ChannelPhase.Running;
	private readonly TaskCompletionSource<bool> _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private bool TryTransition(ChannelPhase from, ChannelPhase to) =>
		Interlocked.CompareExchange(ref _phase, (int)to, (int)from) == (int)from;

	/// <summary>An accepted item reached an end. When it was the last one of a completing channel, the channel is drained.</summary>
	private void ReleasePending(long count)
	{
		if (Interlocked.Add(ref _pendingItems, -count) == 0)
			SettleDrain();
	}

	private void SettleDrain()
	{
		if (IsIdle && TryTransition(ChannelPhase.Completing, ChannelPhase.Drained))
			_drained.TrySetResult(true);
	}

	/// <summary> Current number of tasks handling exporting the events </summary>
	public int ExportTasks => _taskList.Count;

	/// <summary>
	/// The effective concurrency.
	/// <para>Either the  configured concurrency <see cref="Channels.BufferOptions.ExportMaxConcurrency"/> or the calculated concurrency.</para>
	/// </summary>
	public int MaxConcurrency { get; }

	/// <summary>
	/// The effective batch export size.
	/// <para>Either the  configured concurrency <see cref="Channels.BufferOptions.OutboundBufferMaxSize"/> or the calculated size.</para>
	/// <para>If the configured <see cref="Channels.BufferOptions.OutboundBufferMaxSize"/> exceeds (<see cref="Channels.BufferOptions.InboundBufferMaxSize"/> / <see cref="MaxConcurrency"/>)</para>
	/// <para>the batch export size will be lowered to (<see cref="Channels.BufferOptions.InboundBufferMaxSize"/> / <see cref="MaxConcurrency"/>) to ensure we saturate <see cref="MaxConcurrency"/></para>
	/// </summary>
	public int BatchExportSize { get; }

	/// <summary>
	/// If <see cref="Channels.BufferOptions.BoundedChannelFullMode"/> is set to <see cref="BoundedChannelFullMode.Wait"/>
	/// and <see cref="InflightEvents"/> approaches <see cref="Channels.BufferOptions.InboundBufferMaxSize"/>
	/// <see cref="WaitToWriteAsync(System.Threading.CancellationToken)"/> will block until <see cref="InflightEvents"/> drops with atleast this size
	/// </summary>
	public int DrainSize { get; }

	private int _inflightExportOperations;
	/// <summary> Outstanding export operations </summary>
	public int InflightExportOperations => _inflightExportOperations;
	private readonly CountdownEvent _waitForOutboundRead;
	private List<Task> _taskList;

	/// <summary> </summary>
	public bool OutboundStarted  => _waitForOutboundRead.IsSet;

	internal InboundBuffer<TEvent> InboundBuffer { get; }

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	protected BufferedChannelBase(TChannelOptions options) : this(options, null) { }

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	protected BufferedChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, TResponse>>? callbackListeners)
	{
		TokenSource = options.CancellationToken.HasValue
			? CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken.Value)
			: new CancellationTokenSource();
		Options = options;

		var listeners = callbackListeners == null ? [Options] : callbackListeners.Concat([Options]).ToArray();
		DiagnosticsListener = listeners
			.Select(l => (l is IChannelDiagnosticsListener c) ? c : null)
			.FirstOrDefault(e => e != null);
		if (DiagnosticsListener == null && !options.DisableDiagnostics)
		{
			// if no debug listener was already provided but was requested explicitly create one.
			var l = new ChannelDiagnosticsListener<TEvent, TResponse>(GetType().Name);
			DiagnosticsListener = l;
			listeners = listeners.Concat([l]).ToArray();
		}
		_callbacks = new ChannelCallbackInvoker<TEvent, TResponse>(listeners);

		var maxIn = Math.Max(Math.Max(1, BufferOptions.InboundBufferMaxSize), BufferOptions.OutboundBufferMaxSize);
		var defaultMaxOut = Math.Max(1, BufferOptions.OutboundBufferMaxSize);
		var calculatedConcurrency = (int)Math.Ceiling(maxIn / (double)defaultMaxOut);
		var defaultConcurrency = Environment.ProcessorCount * 2;
		MaxConcurrency = BufferOptions.ExportMaxConcurrency ?? Math.Min(calculatedConcurrency, defaultConcurrency);

		// The minimum out buffer the max of (1 or OutboundBufferMaxSize) as long as it does not exceed InboundBufferMaxSize / (MaxConcurrency * 2)
		BatchExportSize = Math.Max(1, Math.Min(BufferOptions.InboundBufferMaxSize / (MaxConcurrency), Math.Max(1, BufferOptions.OutboundBufferMaxSize)));
		DrainSize = Math.Min(100_000, Math.Min(BatchExportSize * 2, maxIn / 2));

		_taskList = new List<Task>(MaxConcurrency * 2);

		_throttleExportTasks = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
		_signal = options.BufferOptions.WaitHandle;
		_waitForOutboundRead = new CountdownEvent(1);
		OutChannel = Channel.CreateBounded<IOutboundBuffer<TEvent>>(
			new BoundedChannelOptions(MaxConcurrency * 4)
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

		//we don't expose the fact TEvent is nullable to the consumer
		Action<TEvent?> itemDropped = e =>
		{
			if (e is null) return;
			// a dropped item was counted when it was written, and it will never be read
			Interlocked.Decrement(ref _inflightEvents);
			ReleasePending(1);
			options.BufferItemDropped?.Invoke(e);
		};
		InChannel = Channel.CreateBounded<TEvent?>(new BoundedChannelOptions(maxIn)
		{
			SingleReader = true,
			SingleWriter = false,
			// Stephen Toub comment: https://github.com/dotnet/runtime/issues/26338#issuecomment-393720727
			// AFAICT this is fine since we run in a dedicated long running task.
			AllowSynchronousContinuations = true,
			// wait does not block it simply signals that Writer.TryWrite should return false and be retried
			// DropWrite will make `TryWrite` always return true, which is not what we want.
			FullMode = options.BufferOptions.BoundedChannelFullMode
		}, itemDropped);

		InboundBuffer = new InboundBuffer<TEvent>(BatchExportSize, BufferOptions.OutboundBufferMaxLifetime);

		_outTask = Task.Factory.StartNew(async () =>
				await ConsumeOutboundEventsAsync().ConfigureAwait(false),
			CancellationToken.None,
			TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness,
			TaskScheduler.Default);

		_inTask = Task.Factory.StartNew(async () =>
				await ConsumeInboundEventsAsync(BatchExportSize, BufferOptions.OutboundBufferMaxLifetime).ConfigureAwait(false),
			CancellationToken.None,
			TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness,
			TaskScheduler.Default);
	}

	/// <summary>
	/// All subclasses of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/> need to at a minimum
	/// implement this method to export buffered collection of <see cref="OutChannel"/>
	/// </summary>
	protected abstract Task<TResponse> ExportAsync(ArraySegment<TEvent> buffer, CancellationToken ctx = default);

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	/// <summary>
	/// Notifies all registered callback listeners that a single event's serialized size exceeds
	/// <see cref="BufferOptions.OutboundBufferMaxBytes"/>. The event is still exported (best-effort).
	/// Called by channel implementations from within <see cref="ExportAsync"/>.
	/// </summary>
	protected void NotifyItemExceedsBytesBudget(TEvent @event, long bytes) =>
		_callbacks.ItemExceedsBytesBudgetCallback?.Invoke(@event, bytes);
#endif

	/// <inheritdoc cref="ChannelWriter{T}.WaitToWriteAsync"/>
	public override async ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
	{
		if (BufferOptions.BoundedChannelFullMode == BoundedChannelFullMode.Wait && _inflightEvents >= BufferOptions.InboundBufferMaxSize - DrainSize)
			for (var i = 0; i < 10 && _inflightEvents >= BufferOptions.InboundBufferMaxSize - DrainSize; i++)
				await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
		return await InChannel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Educated guess for how long we should wait for an outgoing export.
	/// Defaults to 1s
	/// </summary>
	protected virtual TimeSpan DrainRequestTimeout => TimeSpan.FromSeconds(1);

	/// <summary>
	/// Completes the channel and waits until every item accepted so far reached an end: exported, reported as exhausted or dropped.
	/// <para>Asynchronous: no thread is blocked while waiting, whatever the wait.</para>
	/// </summary>
	/// <param name="maxWait">
	/// The longest to wait. When not given, an estimate from <see cref="DrainRequestTimeout"/> and the number of exports still
	/// to run, so an export that never finishes does not hold the caller forever.
	/// </param>
	/// <param name="ctx">Cancels the wait with an <see cref="OperationCanceledException"/>.</param>
	/// <returns><c>true</c> when the channel drained, <c>false</c> when <paramref name="maxWait"/> passed, or the channel was disposed, first.</returns>
#pragma warning disable VSTHRD003 // the task awaited is the TaskCompletionSource this channel completes itself
	public async ValueTask<bool> WaitForDrainAsync(TimeSpan? maxWait = null, CancellationToken ctx = default)
	{
		_ = TryComplete();
		SettleDrain();

		var limit = maxWait ?? ExpectedDrainTime();
		try
		{
			return await WaitAsync(_drained.Task, limit, ctx).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			return false;
		}
	}

	private TimeSpan ExpectedDrainTime()
	{
		var exportsToRun = (long)Math.Ceiling(Math.Max(0, Interlocked.Read(ref _pendingItems)) / (double)BatchExportSize) + MaxConcurrency;
		return TimeSpan.FromTicks(DrainRequestTimeout.Ticks * exportsToRun);
	}

	private static async Task<bool> WaitAsync(Task<bool> task, TimeSpan limit, CancellationToken ctx)
	{
#if NET6_0_OR_GREATER
		return await task.WaitAsync(limit, ctx).ConfigureAwait(false);
#else
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(ctx);
		var timeout = Task.Delay(limit, stop.Token);
		var finished = await Task.WhenAny(task, timeout).ConfigureAwait(false);
		stop.Cancel();
		if (finished == task)
			return await task.ConfigureAwait(false);
		ctx.ThrowIfCancellationRequested();
		throw new TimeoutException();
#endif
	}

#pragma warning restore VSTHRD003

	/// <inheritdoc cref="ChannelWriter{T}.TryComplete"/>
	public override bool TryComplete(Exception? error = null)
	{
		var completed = InChannel.Writer.TryComplete(error);
		if (completed)
		{
			_ = TryTransition(ChannelPhase.Running, ChannelPhase.Completing);
			SettleDrain();
		}
		return completed;
	}

	/// <inheritdoc cref="ChannelWriter{T}.TryWrite"/>
	public override bool TryWrite(TEvent item)
	{
		// Counted before it can be read: the reader takes the count off again, so counting after the write could see it go below zero.
		Interlocked.Increment(ref _inflightEvents);
		Interlocked.Increment(ref _pendingItems);
		if (InChannel.Writer.TryWrite(item))
		{
			_callbacks.PublishToInboundChannelCallback?.Invoke();
			return true;
		}
		Interlocked.Decrement(ref _inflightEvents);
		ReleasePending(1);
		_callbacks.PublishToInboundChannelFailureCallback?.Invoke();
		return false;
	}


	/// <inheritdoc cref="IBufferedChannel{TEvent}.WaitToWriteManyAsync"/>
	public async Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;
		var allWritten = true;
		foreach (var e in events)
		{
			var written = await WaitToWriteAsync(e, ctx).ConfigureAwait(false);
			if (!written) allWritten = written;
		}
		return allWritten;
	}

	/// <inheritdoc cref="IBufferedChannel{TEvent}.TryWriteMany"/>
	public bool TryWriteMany(IEnumerable<TEvent> events)
	{
		var allWritten = true;

		foreach (var @event in events)
		{
			var written = TryWrite(@event);
			if (!written) allWritten = written;
		}

		return allWritten;
	}

	/// <inheritdoc cref="ChannelWriter{T}.WaitToWriteAsync"/>
	public virtual async Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;

		if (await WaitToWriteAsync(ctx).ConfigureAwait(false))
		{
			Interlocked.Increment(ref _inflightEvents);
			Interlocked.Increment(ref _pendingItems);
			if (InChannel.Writer.TryWrite(item))
			{
				_callbacks.PublishToInboundChannelCallback?.Invoke();
				return true;
			}
			Interlocked.Decrement(ref _inflightEvents);
			ReleasePending(1);
		}

		_callbacks.PublishToInboundChannelFailureCallback?.Invoke();
		return false;
	}

	/// <summary>
	/// Subclasses may override this to yield items from <typeparamref name="TResponse"/> that can be retried.
	/// <para>The default implementation of this simply always returns an empty collection</para>
	/// </summary>
	protected virtual ArraySegment<TEvent> RetryBuffer(TResponse response, ArraySegment<TEvent> currentBuffer, IWriteTrackingBuffer statistics) =>
		EmptyArraySegments<TEvent>.Empty;

	private async Task ConsumeOutboundEventsAsync()
	{
		_callbacks.OutboundChannelStartedCallback?.Invoke();

		_taskList = new List<Task>(MaxConcurrency * 2);

		while (await OutChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
		{
			if (_waitForOutboundRead is { IsSet: false })
				_waitForOutboundRead.Signal();
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			while (OutChannel.Reader.TryRead(out var buffer))
			{
				var items = buffer.GetArraySegment();
				await _throttleExportTasks.WaitAsync(TokenSource.Token).ConfigureAwait(false);
				var t = ExportBufferAsync(items, buffer);
				_taskList.Add(t);

				if (_taskList.Count >= MaxConcurrency)
				{
					var completedTask = await Task.WhenAny(_taskList).ConfigureAwait(false);
					_taskList.Remove(completedTask);
				}
				_throttleExportTasks.Release();
			}
		}
		await Task.WhenAll(_taskList).ConfigureAwait(false);

#if NET8_0_OR_GREATER
		await _exitCancelSource.CancelAsync().ConfigureAwait(false);
#else
		_exitCancelSource.Cancel();
#endif
		_callbacks.OutboundChannelExitedCallback?.Invoke();
	}

	private async Task<int> ExportBufferAsync(ArraySegment<TEvent> items, IOutboundBuffer<TEvent> buffer)
	{
		var exhausted = 0;
		Interlocked.Increment(ref _inflightExportOperations);
		using var outboundBuffer = buffer;
		var itemsInBuffer = items.Count;
		try
		{
			var maxRetries = Options.BufferOptions.ExportMaxRetries;
			for (var i = 0; i <= maxRetries && items.Count > 0; i++)
			{
				if (TokenSource.Token.IsCancellationRequested) break;
				if (_signal is { IsSet: true }) break;

				_callbacks.ExportItemsAttemptCallback?.Invoke(i, items.Count);
				TResponse? response = null;
				var threw = false;

				// delay if we still have items, and we are not at the end of the max retry cycle
				var atEndOfRetries = i == maxRetries;
				try
				{
					response = await ExportAsync(items, TokenSource.Token).ConfigureAwait(false);
					_callbacks.ExportResponseCallback?.Invoke(response,
						new WriteTrackingBufferEventData
						{
							Count = outboundBuffer.Count,
							DurationSinceFirstWrite = outboundBuffer.DurationSinceFirstWrite,
							EstimatedBytes = outboundBuffer.EstimatedBytes
						});
				}
				catch (Exception e)
				{
					threw = true;
					_callbacks.ExportExceptionCallback?.Invoke(e);
					// An exception is not a response to pick retryable items from. Unless the caller said this one is safe to try again,
					// the items are given up on and reported, instead of being sent again with unknown idempotence or dropped silently.
					if (atEndOfRetries || !Options.BufferOptions.ExportRetryOnException(e))
					{
						exhausted = items.Count;
						_callbacks.ExportMaxRetriesCallback?.Invoke(items);
						break;
					}
				}

				// An export that threw and was allowed to retry has no response to pick items from, so all of them are tried again.
				items = threw
					? items
					: response == null
						? EmptyArraySegments<TEvent>.Empty
						: RetryBuffer(response, items, outboundBuffer);
				if (items.Count > 0 && i == 0)
					_callbacks.ExportRetryableCountCallback?.Invoke(items.Count);

				if (items.Count > 0 && !atEndOfRetries)
				{
					await Task.Delay(Options.BufferOptions.ExportBackoffPeriod(i), TokenSource.Token).ConfigureAwait(false);
					_callbacks.ExportRetryCallback?.Invoke(items);
				}
				// otherwise if retryable items still exist and the user wants to be notified
				else if (items.Count > 0 && atEndOfRetries)
				{
					exhausted = items.Count;
					_callbacks.ExportMaxRetriesCallback?.Invoke(items);
				}
			}
		}
		finally
		{
			// however the export ends (a cancelled backoff, a callback that throws) it stops being in flight and signals the waiters
			Interlocked.Decrement(ref _inflightExportOperations);
			ReleasePending(itemsInBuffer);
			_callbacks.ExportBufferCallback?.Invoke();
			TrySignal(_signal);
		}
		return exhausted;
	}

	/// <summary>
	/// Signals a countdown event once, unless it already reached zero. Two exports ending together can both see a count of one,
	/// and the second signal throws, so the check and the signal cannot be separate steps.
	/// </summary>
	private static void TrySignal(CountdownEvent? countdown)
	{
		if (countdown is not { IsSet: false }) return;
		try
		{
			countdown.Signal();
		}
		catch (InvalidOperationException)
		{
			// the other export signalled the last count first
		}
	}

	private static readonly TimeSpan MinimumWake = TimeSpan.FromMilliseconds(1);

	/// <summary>Wakes the inbound reader so a partial buffer is flushed on time. Nothing to do when items are waiting: the reader is awake.</summary>
	private void WakeInboundReader()
	{
		if (InChannel.Reader.CanCount && InChannel.Reader.Count > 0) return;
		_ = InChannel.Writer.TryWrite(null);
	}

	private async Task ConsumeInboundEventsAsync(int maxQueuedMessages, TimeSpan maxInterval)
	{
		_callbacks.InboundChannelStartedCallback?.Invoke();

		var reader = InChannel.Reader;

		// A buffer that is not full is flushed once it has waited for maxInterval. The reader sleeps in WaitToReadAsync, so a timer
		// wakes it: armed when a buffer gets its first item, disarmed when the buffer is flushed. One timer for the life of the loop,
		// where each read used to create a Task.Delay and a WhenAny and left the losing timer running until it expired.
		using var flushTimer = new Timer(static state => ((BufferedChannelBase<TChannelOptions, TEvent, TResponse>)state!).WakeInboundReader(),
			this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

		try
		{
			while (true)
			{
				InboundBuffer.MarkWaiting();
				if (!await reader.WaitToReadAsync(TokenSource.Token).ConfigureAwait(false))
					break;

				if (TokenSource.Token.IsCancellationRequested) break;
				if (_signal is { IsSet: true }) break;

				while (InboundBuffer.Count < maxQueuedMessages && reader.TryRead(out var item))
				{
					// the wake-up marker the timer writes
					if (item is null)
						continue;

					InboundBuffer.Add(item);
					Interlocked.Decrement(ref _inflightEvents);

					if (InboundBuffer.DurationSinceFirstWaitToRead >= maxInterval)
						break;
				}

				if (InboundBuffer.ThresholdsHit)
				{
					_ = flushTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
					await FlushBufferAsync().ConfigureAwait(false);
				}
				else if (InboundBuffer.Count > 0)
				{
					// Not due yet: wake for the rest of its lifetime. Armed after every pass that leaves items behind, and not only
					// for the first item, because the timer can fire a hair before the buffer is strictly older than its lifetime,
					// and a timer that is spent then would leave the buffer waiting for the next write.
					var rest = InboundBuffer.TimeUntilFlush;
					_ = flushTimer.Change(rest < MinimumWake ? MinimumWake : rest, Timeout.InfiniteTimeSpan);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// the channel was cancelled: what is in the buffer is flushed below, as when the loop ends any other way
		}
		catch (Exception)
		{
			// The inbound channel was completed with an error (TryComplete(error)): nothing more will ever arrive, which ends the loop
			// like a plain completion. Reading on from a reader that throws at once would spin forever.
		}

		// It's possible to break out of the above while loop before a threshold was met to flush the buffer.
		// This ensures we flush if there are any items left in the inbound buffer.
		if (InboundBuffer.Count > 0)
			await FlushBufferAsync().ConfigureAwait(false);

		OutChannel.Writer.TryComplete();

		async Task FlushBufferAsync()
		{
			var outboundBuffer = new OutboundBuffer<TEvent>(InboundBuffer);

			if (await PublishAsync(outboundBuffer).ConfigureAwait(false))
				_callbacks.PublishToOutboundChannelCallback?.Invoke();
			else
				_callbacks.PublishToOutboundChannelFailureCallback?.Invoke();
		}
	}

	private ValueTask<bool> PublishAsync(IOutboundBuffer<TEvent> buffer)
	{
		async Task<bool> AsyncSlowPathAsync(IOutboundBuffer<TEvent> b)
		{
			var maxRetries = Options.BufferOptions.ExportMaxRetries;
			for (var i = 0; i <= maxRetries; i++)
				while (await OutChannel.Writer.WaitToWriteAsync().ConfigureAwait(false))
					if (OutChannel.Writer.TryWrite(b))
						return true;

			return false;
		}

		return OutChannel.Writer.TryWrite(buffer)
			? new ValueTask<bool>(true)
			: new ValueTask<bool>(AsyncSlowPathAsync(buffer));
	}

	/// <summary>
	/// Pulls <paramref name="source"/> to exhaustion, batches it by <see cref="BatchExportSize"/> and exports every batch
	/// with the same retry and callback machinery used for pushed events. The returned task completes when every batch settled.
	/// <para>This bypasses the inbound buffer: it starts no threads, never touches <see cref="InflightEvents"/>, never
	/// completes the channel and can be called repeatedly and alongside <see cref="TryWrite"/>.</para>
	/// </summary>
	/// <param name="source">The finite sequence to export.</param>
	/// <param name="maxConcurrency">Maximum batches in flight, defaults to <see cref="MaxConcurrency"/>. Use <c>1</c> to preserve order.</param>
	/// <param name="ctx">Cancels reading, batches already in flight are awaited.</param>
	public async Task<IngestAllResult> IngestAllAsync(IEnumerable<TEvent> source, int? maxConcurrency = null, CancellationToken ctx = default)
	{
		var run = new PullRun(this, maxConcurrency, ctx);
		try
		{
			foreach (var item in source)
			{
				ctx.ThrowIfCancellationRequested();
				await run.AddAsync(item).ConfigureAwait(false);
			}
			await run.FlushAsync().ConfigureAwait(false);
			return await run.CompleteAsync().ConfigureAwait(false);
		}
		catch
		{
			await run.AbortAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <inheritdoc cref="IngestAllAsync(IEnumerable{TEvent},int?,CancellationToken)"/>
	/// <remarks>A partial batch is exported once it is older than <see cref="Channels.BufferOptions.OutboundBufferMaxLifetime"/>, also while waiting on a slow source.</remarks>
	public async Task<IngestAllResult> IngestAllAsync(IAsyncEnumerable<TEvent> source, int? maxConcurrency = null, CancellationToken ctx = default)
	{
		var run = new PullRun(this, maxConcurrency, ctx);
		var enumerator = source.GetAsyncEnumerator(ctx);
		try
		{
			var maxLifetime = Options.BufferOptions.OutboundBufferMaxLifetime;
			while (true)
			{
				var next = enumerator.MoveNextAsync();
				bool hasNext;
				if (next.IsCompleted || run.PendingCount == 0)
					hasNext = await next.ConfigureAwait(false);
				else
				{
					// slow source with a partial batch waiting: flush it when it gets too old instead of holding it hostage
					var moveNext = next.AsTask();
					var remaining = maxLifetime - run.BatchAge;
					if (remaining > TimeSpan.Zero)
					{
						using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx);
						var delay = Task.Delay(remaining, cts.Token);
						var winner = await Task.WhenAny(moveNext, delay).ConfigureAwait(false);
						if (winner == moveNext)
						{
#if NET8_0_OR_GREATER
							await cts.CancelAsync().ConfigureAwait(false);
#else
							cts.Cancel();
#endif
						}
						else await run.FlushAsync().ConfigureAwait(false);
					}
					else
						await run.FlushAsync().ConfigureAwait(false);
					hasNext = await moveNext.ConfigureAwait(false);
				}
				if (!hasNext) break;
				await run.AddAsync(enumerator.Current).ConfigureAwait(false);
			}
			await run.FlushAsync().ConfigureAwait(false);
			return await run.CompleteAsync().ConfigureAwait(false);
		}
		catch
		{
			await run.AbortAsync().ConfigureAwait(false);
			throw;
		}
		finally
		{
			await enumerator.DisposeAsync().ConfigureAwait(false);
		}
	}

	// injectable so tests can prove every pulled batch array is returned
	internal ArrayPool<TEvent> PullPool { get; set; } = ArrayPool<TEvent>.Shared;

	/// <summary>State of one <c>IngestAllAsync</c> call: the batch being filled and the exports in flight.</summary>
	private sealed class PullRun
	{
		private readonly BufferedChannelBase<TChannelOptions, TEvent, TResponse> _channel;
		private readonly int _concurrency;
		private readonly int _batchSize;
		private readonly List<Task<int>> _inflight;
		private TEvent[]? _batch;
		private int _count;
		private long _batchStart;
		private long _read;
		private long _batches;
		private long _exhausted;

		public PullRun(BufferedChannelBase<TChannelOptions, TEvent, TResponse> channel, int? maxConcurrency, CancellationToken ctx)
		{
			_channel = channel;
			_concurrency = Math.Max(1, maxConcurrency ?? channel.MaxConcurrency);
			_batchSize = Math.Max(1, channel.BatchExportSize);
			_inflight = new List<Task<int>>(_concurrency);
			_ = ctx;
		}

		public int PendingCount => _count;

		public TimeSpan BatchAge => _count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((System.Diagnostics.Stopwatch.GetTimestamp() - _batchStart) * TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency);

		public ValueTask AddAsync(TEvent item)
		{
			if (_batch is null)
			{
				_batch = _channel.PullPool.Rent(_batchSize);
				_batchStart = System.Diagnostics.Stopwatch.GetTimestamp();
			}
			_batch[_count++] = item;
			_read++;
			return _count >= _batchSize ? new ValueTask(FlushAsync()) : default;
		}

		public async Task FlushAsync()
		{
			if (_count == 0 || _batch is null) return;

			var buffer = new PullBuffer<TEvent>(_batch, _count, BatchAge, _channel.PullPool);
			_batch = null;
			_count = 0;
			_batches++;

			if (_concurrency == 1)
			{
				_exhausted += await _channel.ExportBufferAsync(buffer.GetArraySegment(), buffer).ConfigureAwait(false);
				return;
			}

			while (_inflight.Count >= _concurrency)
				await ReapOneAsync().ConfigureAwait(false);
			_inflight.Add(_channel.ExportBufferAsync(buffer.GetArraySegment(), buffer));
		}

		private async Task ReapOneAsync()
		{
			var done = await Task.WhenAny(_inflight).ConfigureAwait(false);
			_inflight.Remove(done);
			_exhausted += await done.ConfigureAwait(false);
		}

		public async Task<IngestAllResult> CompleteAsync()
		{
			while (_inflight.Count > 0)
				await ReapOneAsync().ConfigureAwait(false);
			return new IngestAllResult(_read, _batches, _exhausted);
		}

		/// <summary>Returns the unsent batch and waits for exports in flight so nothing outlives the call.</summary>
		public async Task AbortAsync()
		{
			if (_batch is not null)
			{
				Array.Clear(_batch, 0, _count);
				_channel.PullPool.Return(_batch);
				_batch = null;
				_count = 0;
			}
			try { await Task.WhenAll(_inflight).ConfigureAwait(false); }
			catch
			{
				// the original failure is the one that matters
			}
		}
	}

	/// <inheritdoc cref="object.ToString"/>>
	public override string ToString()
	{
		if (DiagnosticsListener == null) return base.ToString() ?? string.Empty;
		var sb = new StringBuilder();
		sb.AppendLine(DiagnosticsListener.ToString());
		sb.AppendLine(((FormattableString)$"{nameof(InflightEvents)}: {InflightEvents:N0}").ToString(CultureInfo.CurrentCulture));
		sb.AppendLine(((FormattableString)$"{nameof(BufferOptions.InboundBufferMaxSize)}: {BufferOptions.InboundBufferMaxSize:N0}").ToString(CultureInfo.CurrentCulture));
		sb.AppendLine(((FormattableString)$"{nameof(InflightExportOperations)}: {InflightExportOperations:N0}").ToString(CultureInfo.CurrentCulture));
		sb.AppendLine(((FormattableString)$"{nameof(BatchExportSize)}: {BatchExportSize:N0}").ToString(CultureInfo.CurrentCulture));
		sb.AppendLine(((FormattableString)$"{nameof(DrainSize)}: {DrainSize:N0}").ToString(CultureInfo.CurrentCulture));
		sb.AppendLine(((FormattableString)$"{nameof(MaxConcurrency)}: {MaxConcurrency:N0}").ToString(CultureInfo.CurrentCulture));
		sb.AppendLine(((FormattableString)$"{nameof(ExportTasks)}: {ExportTasks:N0}").ToString(CultureInfo.CurrentCulture));
		return sb.ToString();
	}

	/// <inheritdoc cref="IDisposable.Dispose"/>
	public virtual void Dispose()
	{
		// once: a second call, or a call from a finalizer, finds the channel disposed already
		var before = (ChannelPhase)Interlocked.Exchange(ref _phase, (int)ChannelPhase.Disposed);
		if (before == ChannelPhase.Disposed) return;
		// a waiter for the drain is told it did not happen, unless it did
		_ = _drained.TrySetResult(before == ChannelPhase.Drained);

		try
		{
			// Mark inchannel completed to flush buffer and end task, signaling the end to the out channel
			InChannel.Writer.TryComplete();
			// Wait a reasonable duration for the out channel to complete before disposing the rest
			if (!_exitCancelSource.IsCancellationRequested)
			{
				// Allow one retry before we exit
				var maxWaitForComplete = Options.BufferOptions.ExportBackoffPeriod(1);
				_exitCancelSource.Token.WaitHandle.WaitOne(maxWaitForComplete);
			}
			_exitCancelSource.Dispose();
			InboundBuffer.Dispose();
			TokenSource.Cancel();
			TokenSource.Dispose();
		}
		catch
		{
			// ignored
		}
		try
		{
			_inTask.Dispose();
		}
		catch
		{
			// ignored
		}
		try
		{
			_outTask.Dispose();
		}
		catch
		{
			// ignored
		}
	}
}

/// <summary>The life of a buffered channel: it accepts items until completed, finishes the ones it accepted, then is done.</summary>
internal enum ChannelPhase
{
	/// <summary>Accepting items.</summary>
	Running,
	/// <summary>No more items are accepted; the ones that were are still on their way through the buffers and exports.</summary>
	Completing,
	/// <summary>Every accepted item reached an end.</summary>
	Drained,
	/// <summary>Disposed. Terminal.</summary>
	Disposed
}
