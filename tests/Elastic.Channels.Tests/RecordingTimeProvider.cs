// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace Elastic.Channels.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that says what the code under test did with its timers: how many it created, how often one was armed,
/// and it can fire them without time passing, as a timer that fires early does. FakeTimeProvider keeps its timers to itself.
/// </summary>
public sealed class RecordingTimeProvider : FakeTimeProvider
{
	private readonly ConcurrentBag<RecordingTimer> _timers = [];
	private int _created;
	private int _arms;

	/// <summary>
	/// How far the wall clock is set off the clock that timers and timestamps run on. A negative value is an NTP step back: the wall clock
	/// moves, time as a duration does not.
	/// </summary>
	public TimeSpan WallClockOffset { get; set; }

	public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + WallClockOffset;

	// FakeTimeProvider derives timestamps from GetUtcNow(). The monotonic clock must not see a wall clock that was set, or there is nothing to tell apart.
	public override long GetTimestamp() => base.GetUtcNow().Ticks;

	/// <summary>Timers created through <see cref="CreateTimer"/>.</summary>
	public int TimersCreated => Volatile.Read(ref _created);

	/// <summary>Calls of <c>Change</c> that set a due time, and timers created with one.</summary>
	public int Arms => Volatile.Read(ref _arms);

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		Interlocked.Increment(ref _created);
		if (dueTime != Timeout.InfiniteTimeSpan) Interlocked.Increment(ref _arms);
		var timer = new RecordingTimer(this, base.CreateTimer(callback, state, dueTime, period), callback, state);
		_timers.Add(timer);
		return timer;
	}

	/// <summary>Runs the callback of every live timer now, whatever the clock says.</summary>
	public void FireAllTimersNow()
	{
		foreach (var timer in _timers)
			timer.Fire();
	}

	private sealed class RecordingTimer(RecordingTimeProvider owner, ITimer inner, TimerCallback callback, object? state) : ITimer
	{
		private volatile bool _disposed;

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			if (dueTime != Timeout.InfiniteTimeSpan) Interlocked.Increment(ref owner._arms);
			return inner.Change(dueTime, period);
		}

		public void Fire()
		{
			if (!_disposed) callback(state);
		}

		public void Dispose()
		{
			_disposed = true;
			inner.Dispose();
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return default;
		}
	}
}
