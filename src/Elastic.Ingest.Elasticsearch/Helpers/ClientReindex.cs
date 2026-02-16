// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Orchestrates a client-side reindex: reads from a source index using PIT search
/// and writes to a destination channel. The caller owns the channel lifecycle.
/// </summary>
public sealed class ClientReindex<TDocument> where TDocument : class
{
	private readonly ClientReindexOptions<TDocument> _options;

	/// <summary>
	/// Creates a new client-side reindex operation.
	/// </summary>
	public ClientReindex(ClientReindexOptions<TDocument> options) => _options = options;

	/// <summary>
	/// Runs the client-side reindex, yielding progress updates after each page is written.
	/// </summary>
	public async IAsyncEnumerable<ClientReindexProgress> RunAsync([EnumeratorCancellation] CancellationToken ctx = default)
	{
		var sw = Stopwatch.StartNew();
		long documentsRead = 0;
		long documentsWritten = 0;

		var channel = _options.Destination;
		var transport = channel.Options.Transport;
		var serializerOptions = channel.Options.SerializerOptions;

		var pitSearch = new PointInTimeSearch<TDocument>(transport, _options.Source, serializerOptions);

		try
		{
			await foreach (var page in pitSearch.SearchPagesAsync(ctx).ConfigureAwait(false))
			{
				documentsRead += page.Documents.Count;

				IEnumerable<TDocument> documents = page.Documents;
				if (_options.Transform != null)
					documents = page.Documents.Select(_options.Transform);

				await channel.WaitToWriteManyAsync(documents, ctx).ConfigureAwait(false);
				documentsWritten += page.Documents.Count;

				yield return new ClientReindexProgress
				{
					DocumentsRead = documentsRead,
					DocumentsWritten = documentsWritten,
					IsCompleted = false,
					Elapsed = sw.Elapsed
				};
			}

			// Flush remaining writes
			await channel.WaitForDrainAsync(ctx: ctx).ConfigureAwait(false);

			yield return new ClientReindexProgress
			{
				DocumentsRead = documentsRead,
				DocumentsWritten = documentsWritten,
				IsCompleted = true,
				Elapsed = sw.Elapsed
			};
		}
		finally
		{
			await pitSearch.DisposeAsync().ConfigureAwait(false);
		}
	}
}
