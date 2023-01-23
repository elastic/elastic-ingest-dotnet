// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Linq;

namespace Elastic.Channels;

public static class BufferedChannelExtensions
{
	public static bool TryWriteMany<TEvent>(this IBufferedChannel<TEvent> channel, IEnumerable<TEvent> events) =>
		events.Select(e => channel.TryWrite(e)).All(b => b);

}
