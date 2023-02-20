// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq;
using System.Threading;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Diagnostics;

/// <summary>
/// A very rudimentary diagnostics object tracking various important metrics to provide insights into the
/// machinery of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>.
/// <para>This will be soon be replaced by actual metrics</para>
/// </summary>
// ReSharper disable once UnusedType.Global
public class ElasticsearchChannelListener<TEvent> : ChannelListener<TEvent, BulkResponse>
{
	private int _rejectedItems;
	private string? _firstItemError;
	private int _serverRejections;

	/// <inheritdoc cref="ChannelListener{TEvent,TResponse}.PublishSuccess"/>
	public override bool PublishSuccess => base.PublishSuccess && string.IsNullOrEmpty(_firstItemError);

	// ReSharper disable once UnusedMember.Global
	/// <inheritdoc cref="ChannelListener{TEvent,TResponse}.Register"/>
	public ElasticsearchChannelListener<TEvent> Register(ResponseItemsChannelOptionsBase<TEvent, BulkResponse, BulkResponseItem> options)
	{
		base.Register(options);

		options.ServerRejectionCallback = r =>
		{
			Interlocked.Add(ref _rejectedItems, r.Count);
			if (r.Count > 0)
			{
				var error = r.Select(e => e.Item2).FirstOrDefault(i=>i.Error != null);
				if (error != null)
					_firstItemError ??= error.Error?.ToString();
			}
			Interlocked.Increment(ref _serverRejections);
		};
		return this;
	}

	/// <inheritdoc cref="ChannelListener{TEvent,TResponse}.AdditionalData"/>
	protected override string AdditionalData => $@"Server Rejected Calls: {_serverRejections:N0}
Server Rejected Items: {_rejectedItems:N0}
First Error: {_firstItemError}
";
}
