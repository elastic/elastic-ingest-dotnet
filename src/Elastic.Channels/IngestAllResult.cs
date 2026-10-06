// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Channels;

/// <summary>The outcome of pulling a finite sequence through <c>IngestAllAsync</c>.</summary>
public class IngestAllResult
{
	/// <summary>Creates a result.</summary>
	public IngestAllResult(long read, long batches, long retriesExhausted)
	{
		Read = read;
		Batches = batches;
		RetriesExhausted = retriesExhausted;
	}

	/// <summary>The number of items read from the source.</summary>
	public long Read { get; }

	/// <summary>The number of batches that were exported.</summary>
	public long Batches { get; }

	/// <summary>
	/// The number of items that were still failing when the retries ran out
	/// (see <see cref="BufferOptions.ExportMaxRetries"/>), or whose export kept throwing.
	/// </summary>
	public long RetriesExhausted { get; }
}
