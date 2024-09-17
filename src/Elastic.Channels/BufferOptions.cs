// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Channels;

namespace Elastic.Channels;

/// <summary>
/// Controls how data should be buffered in <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/> implementations
/// </summary>
public class BufferOptions
{
	/// <summary>
	/// The maximum number of in flight instances that can be queued in memory. If this threshold is reached, events will be dropped
	/// <para>Defaults to <c>100_000</c></para>
	/// </summary>
	public int InboundBufferMaxSize { get; set; } = 100_000;

	/// <summary>
	/// The maximum size to export to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/> at once.
	/// <para>Defaults to <c>1_000</c></para>
	/// </summary>
	public int OutboundBufferMaxSize { get; set; } = 1_000;

	private TimeSpan _outboundBufferMaxLifetime = TimeSpan.FromSeconds(5);
	private readonly TimeSpan _outboundBufferMinLifetime = TimeSpan.FromSeconds(1);


	/// <summary>
	/// The maximum lifetime of a buffer to export to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/>.
	/// If a buffer is older then the configured <see cref="OutboundBufferMaxLifetime"/> it will be flushed to
	/// <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/> regardless of it's current size
	/// <para>Defaults to <c>5 seconds</c></para>
	/// <para>Any value less than <c>1 second</c> will be rounded back up to <c>1 second</c></para>
	/// </summary>
	public TimeSpan OutboundBufferMaxLifetime
	{
		get => _outboundBufferMaxLifetime;
		set => _outboundBufferMaxLifetime = value >= _outboundBufferMinLifetime ? value : _outboundBufferMaxLifetime;
	}

	/// <summary>
	/// The maximum number of consumers allowed to poll for new events on the channel.
	/// <para>Defaults to the lesser of:</para>
	/// <list type="bullet">
	///		<item><see cref="InboundBufferMaxSize"/>/<see cref="OutboundBufferMaxSize"/></item>
	///		<item>OR <see cref="Environment.ProcessorCount"/></item>
	/// </list>
	/// <para>, increase to introduce concurrency.</para>
	/// </summary>
	public int? ExportMaxConcurrency { get; set; }

	/// <summary>
	/// The times to retry an export if <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.RetryBuffer"/> yields items to retry.
	/// <para>Whether or not items are selected for retrying depends on the actual channel implementation</para>
	/// <see cref="ExportBackoffPeriod"/> to implement a backoff period of your choosing.
	/// <para>Defaults to <c>3</c>, when <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.RetryBuffer"/> yields any items</para>
	/// </summary>
	public int ExportMaxRetries { get; set; } = 3;


	/// <summary>
	/// A function to calculate the backoff period, gets passed the number of retries attempted starting at 0.
	/// By default backs off in increments of 2 seconds.
	/// </summary>
	public Func<int, TimeSpan> ExportBackoffPeriod { get; set; } = (i) => TimeSpan.FromSeconds(2 * (i + 1));

	/// <summary>
	/// Allows you to inject a <see cref="CountdownEvent"/> to wait for N number of buffers to flush.
	/// </summary>
	public CountdownEvent? WaitHandle { get; set; }

	/// <summary>
	/// <inheritdoc cref="BoundedChannelFullMode" path="summary" />
	/// <para>Defaults to <see cref="BoundedChannelFullMode.Wait"/>, this will use more memory as overproducing will need to wait to enqueue data</para>
	/// <para>Use <see cref="BoundedChannelFullMode.DropWrite"/> to minimize memory consumption at the expense of more  likely to drop data</para>
	/// <para>You might need to tweak <see cref="InboundBufferMaxSize"/> and <see cref="OutboundBufferMaxSize"/> to ensure sufficient allocations are available </para>
	/// <para>The defaults for both <see cref="InboundBufferMaxSize"/> adn <see cref="OutboundBufferMaxSize"/> are quite liberal already though.</para>
	/// </summary>
	public BoundedChannelFullMode BoundedChannelFullMode { get; set; } = BoundedChannelFullMode.Wait;
}
