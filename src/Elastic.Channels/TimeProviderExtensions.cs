// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Channels;

/// <summary>
/// Waiting on a <see cref="TimeProvider"/>. The same code on every target: <c>Task.Delay(TimeSpan, TimeProvider, CancellationToken)</c>
/// only exists on net8.0 and later, and the <c>netstandard</c> targets get <see cref="TimeProvider"/> from a polyfill package.
/// </summary>
internal static class TimeProviderExtensions
{
	/// <summary>Completes after <paramref name="delay"/> on <paramref name="timeProvider"/>, or is cancelled with <paramref name="cancellationToken"/>.</summary>
	public static async Task DelayAsync(this TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (delay <= TimeSpan.Zero) return;

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var timer = timeProvider.CreateTimer(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), completion, delay, Timeout.InfiniteTimeSpan);
		using var registration = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), completion);
#pragma warning disable VSTHRD003 // the task is the completion source this method owns
		await completion.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
	}

	/// <summary>
	/// Waits for <paramref name="task"/> for at most <paramref name="limit"/> on <paramref name="timeProvider"/>.
	/// Throws <see cref="TimeoutException"/> when the limit passes first, and <see cref="OperationCanceledException"/> when cancelled.
	/// </summary>
#pragma warning disable VSTHRD003 // waiting for a task the caller started is the point of this method
	public static async Task<T> WaitAsync<T>(this TimeProvider timeProvider, Task<T> task, TimeSpan limit, CancellationToken cancellationToken)
	{
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var timeout = timeProvider.DelayAsync(limit, stop.Token);
		var finished = await Task.WhenAny(task, timeout).ConfigureAwait(false);
#if NET8_0_OR_GREATER
		await stop.CancelAsync().ConfigureAwait(false);
#else
		stop.Cancel();
#endif
		if (finished == task)
			return await task.ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		throw new TimeoutException();
	}
#pragma warning restore VSTHRD003
}
