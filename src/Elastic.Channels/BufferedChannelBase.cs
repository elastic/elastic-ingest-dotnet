// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
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
	private Channel<TEvent> InChannel { get; }
	private BufferOptions BufferOptions => Options.BufferOptions;

	private long _inflightEvents;
	/// <summary> The number of inflight events </summary>
	public long InflightEvents => _inflightEvents;

	/// <summary> Current number of tasks handling exporting the events </summary>
	public int ExportTasks => _taskList.Count;

	/// <summary>
	/// The effective concurrency.
	/// <para>Either the  configured concurrency <see cref="Channels.BufferOptions.ExportMaxConcurrency"/> or the calculated concurrency.</para>
	/// </summary>
	public int MaxConcurrency { get; }

	/// <summary>
	/// The effective batch export size .
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

	private int _ongoingExportOperations;
	/// <summary> Outstanding export operations </summary>
	public int BatchExportOperations => _ongoingExportOperations;
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

		var listeners = callbackListeners == null ? new[] { Options } : callbackListeners.Concat(new[] { Options }).ToArray();
		DiagnosticsListener = listeners
			.Select(l => (l is IChannelDiagnosticsListener c) ? c : null)
			.FirstOrDefault(e => e != null);
		if (DiagnosticsListener == null && !options.DisableDiagnostics)
		{
			// if no debug listener was already provided but was requested explicitly create one.
			var l = new ChannelDiagnosticsListener<TEvent, TResponse>(GetType().Name);
			DiagnosticsListener = l;
			listeners = listeners.Concat(new[] { l }).ToArray();
		}
		_callbacks = new ChannelCallbackInvoker<TEvent, TResponse>(listeners);

		var maxIn = Math.Max(Math.Max(1, BufferOptions.InboundBufferMaxSize), BufferOptions.OutboundBufferMaxSize);
		var defaultMaxOut = Math.Max(1, BufferOptions.OutboundBufferMaxSize);
		var calculatedConcurrency = (int)Math.Ceiling(maxIn / (double)defaultMaxOut);
		var defaultConcurrency = Environment.ProcessorCount * 2;
		MaxConcurrency = BufferOptions.ExportMaxConcurrency ?? Math.Min(calculatedConcurrency, defaultConcurrency);

		// The minimum out buffer the max of (1 or OutboundBufferMaxSize) as long as it does not exceed InboundBufferMaxSize / (MaxConcurrency * 2)
		BatchExportSize = Math.Min(BufferOptions.InboundBufferMaxSize / (MaxConcurrency), Math.Max(1, BufferOptions.OutboundBufferMaxSize));
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
		InChannel = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(maxIn)
		{
			SingleReader = false,
			SingleWriter = false,
			// Stephen Toub comment: https://github.com/dotnet/runtime/issues/26338#issuecomment-393720727
			// AFAICT this is fine since we run in a dedicated long running task.
			AllowSynchronousContinuations = true,
			// wait does not block it simply signals that Writer.TryWrite should return false and be retried
			// DropWrite will make `TryWrite` always return true, which is not what we want.
			FullMode = options.BufferOptions.BoundedChannelFullMode
		});

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

	/// <inheritdoc cref="ChannelWriter{T}.WaitToWriteAsync"/>
	public override async ValueTask<bool> WaitToWriteAsync(CancellationToken ctx = default)
	{
		if (BufferOptions.BoundedChannelFullMode == BoundedChannelFullMode.Wait && _inflightEvents >= BufferOptions.InboundBufferMaxSize - DrainSize)
			for (var i = 0; i < 10 && _inflightEvents >= BufferOptions.InboundBufferMaxSize - DrainSize; i++)
				await Task.Delay(TimeSpan.FromMilliseconds(100), ctx).ConfigureAwait(false);
		return await InChannel.Writer.WaitToWriteAsync(ctx).ConfigureAwait(false);
	}

	/// <inheritdoc cref="ChannelWriter{T}.TryComplete"/>
	public override bool TryComplete(Exception? error = null) => InChannel.Writer.TryComplete(error);

	/// <inheritdoc cref="ChannelWriter{T}.TryWrite"/>
	public override bool TryWrite(TEvent item)
	{
		if (InChannel.Writer.TryWrite(item))
		{
			Interlocked.Increment(ref _inflightEvents);
			_callbacks.PublishToInboundChannelCallback?.Invoke();
			return true;
		}
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

		if (await WaitToWriteAsync(ctx).ConfigureAwait(false) && InChannel.Writer.TryWrite(item))
		{
			Interlocked.Increment(ref _inflightEvents);
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
		_exitCancelSource.Cancel();
		_callbacks.OutboundChannelExitedCallback?.Invoke();
	}

	private async Task ExportBufferAsync(ArraySegment<TEvent> items, IOutboundBuffer<TEvent> buffer)
	{
		Interlocked.Increment(ref _ongoingExportOperations);
		using var outboundBuffer = buffer;
		var maxRetries = Options.BufferOptions.ExportMaxRetries;
		for (var i = 0; i <= maxRetries && items.Count > 0; i++)
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			_callbacks.ExportItemsAttemptCallback?.Invoke(i, items.Count);
			TResponse? response = null;

			// delay if we still have items and we are not at the end of the max retry cycle
			var atEndOfRetries = i == maxRetries;
			try
			{
				response = await ExportAsync(items, TokenSource.Token).ConfigureAwait(false);
				_callbacks.ExportResponseCallback?.Invoke(response,
					new WriteTrackingBufferEventData { Count = outboundBuffer.Count, DurationSinceFirstWrite = outboundBuffer.DurationSinceFirstWrite });
			}
			catch (Exception e)
			{
				_callbacks.ExportExceptionCallback?.Invoke(e);
				if (atEndOfRetries)
					break;
			}

			items = response == null
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
				_callbacks.ExportMaxRetriesCallback?.Invoke(items);
		}
		Interlocked.Decrement(ref _ongoingExportOperations);
		_callbacks.ExportBufferCallback?.Invoke();
		if (_signal is { IsSet: false })
			_signal.Signal();
	}

	private async Task ConsumeInboundEventsAsync(int maxQueuedMessages, TimeSpan maxInterval)
	{
		_callbacks.InboundChannelStartedCallback?.Invoke();

		while (await InboundBuffer.WaitToReadAsync(InChannel.Reader).ConfigureAwait(false))
		{
			if (TokenSource.Token.IsCancellationRequested) break;
			if (_signal is { IsSet: true }) break;

			while (InboundBuffer.Count < maxQueuedMessages && InChannel.Reader.TryRead(out var item))
			{
				InboundBuffer.Add(item);
				Interlocked.Decrement(ref _inflightEvents);

				if (InboundBuffer.DurationSinceFirstWaitToRead >= maxInterval)
					break;
			}

			if (InboundBuffer.ThresholdsHit)
				await FlushBufferAsync().ConfigureAwait(false);
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

	/// <inheritdoc cref="object.ToString"/>>
	public override string ToString()
	{
		if (DiagnosticsListener == null) return base.ToString();
		var sb = new StringBuilder();
		sb.AppendLine(DiagnosticsListener.ToString());
		sb.AppendLine($"{nameof(InflightEvents)}: {InflightEvents:N0}");
		sb.AppendLine($"{nameof(BufferOptions.InboundBufferMaxSize)}: {BufferOptions.InboundBufferMaxSize:N0}");
		sb.AppendLine($"{nameof(BatchExportOperations)}: {BatchExportOperations:N0}");
		sb.AppendLine($"{nameof(BatchExportSize)}: {BatchExportSize:N0}");
		sb.AppendLine($"{nameof(DrainSize)}: {DrainSize:N0}");
		sb.AppendLine($"{nameof(MaxConcurrency)}: {MaxConcurrency:N0}");
		sb.AppendLine($"{nameof(ExportTasks)}: {ExportTasks:N0}");
		return sb.ToString();
	}

	/// <inheritdoc cref="IDisposable.Dispose"/>
	public virtual void Dispose()
	{
		try
		{
			// Mark inchannel completed to flush buffer and end task, signalling end to outchannel
			InChannel.Writer.TryComplete();
			// Wait a reasonable duration for the outchannel to complete before disposing the rest
			if (!_exitCancelSource.IsCancellationRequested)
			{
				// Allow one retry before we exit
				var maxwait = Options.BufferOptions.ExportBackoffPeriod(1);
				_exitCancelSource.Token.WaitHandle.WaitOne(maxwait);
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
