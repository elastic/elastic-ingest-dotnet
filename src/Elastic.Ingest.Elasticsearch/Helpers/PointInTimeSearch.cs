// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Provides PIT-based search_after iteration over an Elasticsearch index.
/// Implements <see cref="IAsyncDisposable"/> to close the PIT on disposal.
/// </summary>
public sealed class PointInTimeSearch<TDocument> : IAsyncDisposable
{
	private readonly ITransport _transport;
	private readonly PointInTimeSearchOptions _options;
	private readonly JsonSerializerOptions? _serializerOptions;
	private readonly string _index;
	private string? _pitId;
	private bool _disposed;

	/// <summary>
	/// Creates a new PIT-based search iterator.
	/// </summary>
	/// <param name="transport">The Elasticsearch transport.</param>
	/// <param name="options">Search configuration.</param>
	/// <param name="serializerOptions">Optional JSON serializer options for deserializing TDocument.</param>
	public PointInTimeSearch(ITransport transport, PointInTimeSearchOptions options, JsonSerializerOptions? serializerOptions = null)
	{
		_transport = transport;
		_options = options;
		_serializerOptions = serializerOptions;
		_index = options.Index
			?? (options.TypeContext != null ? options.TypeContext.ResolveReadTarget() : null)
			?? throw new InvalidOperationException("Either Index or TypeContext must be provided on PointInTimeSearchOptions.");
	}

	/// <summary>
	/// Yields pages of documents using search_after pagination with a PIT.
	/// </summary>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Callers provide JsonSerializerOptions with appropriate context")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Callers provide JsonSerializerOptions with appropriate context")]
	public async IAsyncEnumerable<SearchPage<TDocument>> SearchPagesAsync([EnumeratorCancellation] CancellationToken ctx = default)
	{
		_pitId = await OpenPitAsync(ctx).ConfigureAwait(false);

		var slices = ResolveSliceCount(ctx);
		if (slices > 1)
		{
			await foreach (var page in SearchSlicedAsync(slices, ctx).ConfigureAwait(false))
				yield return page;
			yield break;
		}

		string? searchAfter = null;
		while (!ctx.IsCancellationRequested)
		{
			var body = BuildSearchBody(searchAfter, sliceId: null, sliceMax: null);
			var response = await _transport.RequestAsync<StringResponse>(
				HttpMethod.POST, "/_search", PostData.String(body), cancellationToken: ctx
			).ConfigureAwait(false);

			if (response.ApiCallDetails.HttpStatusCode is not 200)
				throw new Exception(
					$"Search request failed: {response}",
					response.ApiCallDetails.OriginalException
				);

			var page = ParseSearchResponse(response.Body, out searchAfter);
			yield return page;

			if (!page.HasMore)
				yield break;
		}
	}

	/// <summary>
	/// Convenience: flattens pages into individual documents.
	/// </summary>
	public async IAsyncEnumerable<TDocument> SearchDocumentsAsync([EnumeratorCancellation] CancellationToken ctx = default)
	{
		await foreach (var page in SearchPagesAsync(ctx).ConfigureAwait(false))
		{
			foreach (var doc in page.Documents)
				yield return doc;
		}
	}

	/// <summary>
	/// Closes the PIT.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_disposed || _pitId == null)
			return;
		_disposed = true;

		try
		{
			var body = $@"{{""id"":""{EscapeJson(_pitId)}""}}";
			await _transport.RequestAsync<StringResponse>(
				HttpMethod.DELETE, "/_pit", PostData.String(body)
			).ConfigureAwait(false);
		}
		catch
		{
			// Best-effort cleanup; PIT will expire on its own.
		}
	}

	private async Task<string> OpenPitAsync(CancellationToken ctx)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST,
			$"/{_index}/_pit?keep_alive={_options.KeepAlive}",
			cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to open PIT for index '{_index}': {response}",
				response.ApiCallDetails.OriginalException
			);

		return response.Get<string>("id")
			?? throw new Exception("PIT response did not contain an 'id' field.");
	}

	private int ResolveSliceCount(CancellationToken ctx)
	{
		var slices = _options.Slices;
		if (slices is 0 or 1) return 1;
		if (slices > 1) return slices.Value;

		// Auto-detect: serverless gets no slicing, otherwise use shard count
		try
		{
			if (ElasticsearchServerDetection.IsServerless(_transport))
				return 1;
			return ElasticsearchServerDetection.GetShardCount(_transport, _index);
		}
		catch
		{
			return 1;
		}
	}

	private async IAsyncEnumerable<SearchPage<TDocument>> SearchSlicedAsync(
		int sliceMax, [EnumeratorCancellation] CancellationToken ctx)
	{
		var channel = Channel.CreateBounded<SearchPage<TDocument>>(new BoundedChannelOptions(sliceMax * 2)
		{
			SingleWriter = false,
			SingleReader = true
		});

		var tasks = new Task[sliceMax];
		for (var i = 0; i < sliceMax; i++)
		{
			var sliceId = i;
			tasks[i] = Task.Run(async () =>
			{
				try
				{
					string? searchAfter = null;
					while (!ctx.IsCancellationRequested)
					{
						var body = BuildSearchBody(searchAfter, sliceId, sliceMax);
						var response = await _transport.RequestAsync<StringResponse>(
							HttpMethod.POST, "/_search", PostData.String(body), cancellationToken: ctx
						).ConfigureAwait(false);

						if (response.ApiCallDetails.HttpStatusCode is not 200)
							throw new Exception(
								$"Sliced search request failed (slice {sliceId}/{sliceMax}): {response}",
								response.ApiCallDetails.OriginalException
							);

						var page = ParseSearchResponse(response.Body, out searchAfter);
						await channel.Writer.WriteAsync(page, ctx).ConfigureAwait(false);

						if (!page.HasMore)
							break;
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					channel.Writer.TryComplete(ex);
					throw;
				}
			}, ctx);
		}

		// Complete the channel when all slice tasks finish
		_ = Task.WhenAll(tasks).ContinueWith(t =>
		{
			if (t.IsFaulted)
				channel.Writer.TryComplete(t.Exception?.InnerException ?? t.Exception);
			else
				channel.Writer.TryComplete();
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

		await foreach (var page in channel.Reader.ReadAllAsync(ctx).ConfigureAwait(false))
			yield return page;
	}

	private string BuildSearchBody(string? searchAfter, int? sliceId, int? sliceMax)
	{
		var sb = new StringBuilder(256);
		sb.Append("{\"pit\":{\"id\":\"").Append(EscapeJson(_pitId!)).Append("\",\"keep_alive\":\"").Append(_options.KeepAlive).Append("\"}");

		sb.Append(",\"size\":").Append(_options.Size);

		var sort = _options.Sort ?? "\"_shard_doc\"";
		sb.Append(",\"sort\":[").Append(sort).Append(']');

		if (_options.QueryBody != null)
			sb.Append(",\"query\":").Append(_options.QueryBody);

		if (searchAfter != null)
			sb.Append(",\"search_after\":").Append(searchAfter);

		if (sliceId.HasValue && sliceMax.HasValue)
			sb.Append(",\"slice\":{\"id\":").Append(sliceId.Value).Append(",\"max\":").Append(sliceMax.Value).Append('}');

		sb.Append('}');
		return sb.ToString();
	}

	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Callers provide JsonSerializerOptions with appropriate context")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Callers provide JsonSerializerOptions with appropriate context")]
	private SearchPage<TDocument> ParseSearchResponse(string responseBody, out string? searchAfter)
	{
		searchAfter = null;
		var documents = new List<TDocument>();
		long totalDocuments = 0;
		string? lastSort = null;

		using var doc = JsonDocument.Parse(responseBody);
		var root = doc.RootElement;

		// Extract hits.total.value
		if (root.TryGetProperty("hits", out var hits))
		{
			if (hits.TryGetProperty("total", out var total) && total.TryGetProperty("value", out var totalValue))
				totalDocuments = totalValue.GetInt64();

			// Extract hits.hits[]
			if (hits.TryGetProperty("hits", out var hitsArray))
			{
				foreach (var hit in hitsArray.EnumerateArray())
				{
					// Extract _source
					if (hit.TryGetProperty("_source", out var source))
					{
						var sourceJson = source.GetRawText();
						var document = _serializerOptions != null
							? JsonSerializer.Deserialize<TDocument>(sourceJson, _serializerOptions)
							: JsonSerializer.Deserialize<TDocument>(sourceJson);
						if (document != null)
							documents.Add(document);
					}

					// Track sort values from the last hit for search_after
					if (hit.TryGetProperty("sort", out var sort))
						lastSort = sort.GetRawText();
				}
			}
		}

		// Update PIT ID if returned in the response
		if (root.TryGetProperty("pit_id", out var newPitId))
			_pitId = newPitId.GetString() ?? _pitId;

		searchAfter = lastSort;

		return new SearchPage<TDocument>
		{
			Documents = documents,
			TotalDocuments = totalDocuments,
			HasMore = documents.Count >= _options.Size
		};
	}

	private static string EscapeJson(string value) =>
		value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
