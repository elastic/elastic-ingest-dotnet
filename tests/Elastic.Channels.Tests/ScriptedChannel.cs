// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Channels.Tests;

public sealed class Item(int id)
{
	public int Id { get; } = id;
}

public sealed class Response
{
	/// <summary>Ids the server asked to be retried.</summary>
	public HashSet<int> Retry { get; init; } = [];

	/// <summary>The whole request is retried (a 429, say).</summary>
	public bool RetryWhole { get; init; }
}

public sealed class ScriptedOptions : ChannelOptionsBase<Item, Response>;

/// <summary>One call to <see cref="ScriptedChannel.ExportAsync"/>: the attempt number it was, the ids it got and how it ended.</summary>
public sealed record ExportCall(int Number, int[] Ids, bool Threw);

/// <summary>
/// A channel whose export behaviour a test scripts. Every call is recorded, so a test can say which ids were exported
/// how many times and in what order, without the channel's own diagnostics.
/// </summary>
public sealed class ScriptedChannel : BufferedChannelBase<ScriptedOptions, Item, Response>
{
	private readonly Func<int, ArraySegment<Item>, CancellationToken, Task<Response>> _export;
	private int _calls;

	public ScriptedChannel(ScriptedOptions options, Func<int, ArraySegment<Item>, CancellationToken, Task<Response>>? export = null)
		: base(options) =>
		_export = export ?? ((_, _, _) => Task.FromResult(new Response()));

	public ConcurrentQueue<ExportCall> Calls { get; } = new();

	protected override async Task<Response> ExportAsync(ArraySegment<Item> buffer, CancellationToken ctx = default)
	{
		var number = Interlocked.Increment(ref _calls);
		var ids = buffer.Select(i => i.Id).ToArray();
		try
		{
			var response = await _export(number, buffer, ctx).ConfigureAwait(false);
			Calls.Enqueue(new ExportCall(number, ids, false));
			return response;
		}
		catch
		{
			Calls.Enqueue(new ExportCall(number, ids, true));
			throw;
		}
	}

	protected override ArraySegment<Item> RetryBuffer(Response response, ArraySegment<Item> currentBuffer, Buffers.IWriteTrackingBuffer statistics)
	{
		if (response.RetryWhole) return currentBuffer;
		var retry = currentBuffer.Where(i => response.Retry.Contains(i.Id)).ToArray();
		return new ArraySegment<Item>(retry);
	}

	/// <summary>Ids of every export call that returned (did not throw), flattened.</summary>
	public int[] SeenByCompletedExports() => Calls.Where(c => !c.Threw).SelectMany(c => c.Ids).ToArray();

	public static ScriptedOptions Opts(
		int inbound = 100, int outbound = 10, int? concurrency = 1, int maxRetries = 3,
		TimeSpan? lifetime = null, Action<ScriptedOptions>? configure = null, TimeProvider? time = null)
	{
		var o = new ScriptedOptions
		{
			DisableDiagnostics = true,
			BufferOptions = new BufferOptions
			{
				InboundBufferMaxSize = inbound,
				OutboundBufferMaxSize = outbound,
				ExportMaxConcurrency = concurrency,
				ExportMaxRetries = maxRetries,
				ExportBackoffPeriod = _ => TimeSpan.FromMilliseconds(1),
				OutboundBufferMaxLifetime = lifetime ?? TimeSpan.FromMilliseconds(50),
				TimeProvider = time ?? TimeProvider.System
			}
		};
		configure?.Invoke(o);
		return o;
	}
}
