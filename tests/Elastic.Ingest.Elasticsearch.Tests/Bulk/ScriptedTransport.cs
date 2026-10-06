// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>What the scripted transport answers for one request.</summary>
/// <param name="HttpStatus">The HTTP status of the response</param>
/// <param name="ItemStatuses">Status per item, null returns an empty body</param>
/// <param name="Throw">Throw this from the transport instead</param>
public record ScriptedResponse(int HttpStatus, int[] ItemStatuses = null, Exception Throw = null, string RawBody = null, bool IncludeErrorsFlag = true)
{
	public static ScriptedResponse Raw(string body, int http = 200) => new(http, null, null, body);
	public static ScriptedResponse WithoutErrorsFlag(params int[] statuses) => new(200, statuses, null, null, false);
	public static ScriptedResponse Items(params int[] statuses) => new(200, statuses);
	public static ScriptedResponse Http(int status) => new(status);
}

/// <summary>A captured request.</summary>
public record CapturedRequest(string PathAndQuery, byte[] Body, int Attempt, TimeSpan RequestTimeout = default)
{
	/// <summary>The query string parameters of the request, in order.</summary>
	public string[] Query => PathAndQuery.Contains('?') ? PathAndQuery[(PathAndQuery.IndexOf('?') + 1)..].Split('&') : System.Array.Empty<string>();

	/// <summary>The path without the query string.</summary>
	public string Path => PathAndQuery.Contains('?') ? PathAndQuery[..PathAndQuery.IndexOf('?')] : PathAndQuery;

	public string BodyText => Encoding.UTF8.GetString(Body);
	public string[] Lines => BodyText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// An in memory transport that records every request body and answers using a script,
/// the script receives the attempt number and the number of operations in the request.
/// </summary>
public sealed class ScriptedTransport
{
	private readonly List<CapturedRequest> _requests = new();
	private int _inflight;
	private int _maxInflight;

	public ScriptedTransport(Func<int, CapturedRequest, ScriptedResponse> script)
	{
		var invoker = new ScriptedInvoker(this, script);
		Transport = new DistributedTransport<TransportConfiguration>(
			new TransportConfiguration(new SingleNodePool(new Uri("http://localhost:9200")), invoker)
			{ DisablePings = true, DebugMode = true });
	}

	/// <summary>Every item succeeds with 201.</summary>
	public static ScriptedTransport AlwaysSucceeds() =>
		new((_, r) => ScriptedResponse.Items(Enumerable.Repeat(201, CountOperations(r)).ToArray()));

	public ITransport Transport { get; }

	public IReadOnlyList<CapturedRequest> Requests { get { lock (_requests) return _requests.ToArray(); } }

	public int MaxInflight => _maxInflight;

	/// <summary>Requests currently being served, must be zero once the code under test returned or threw.</summary>
	public int Inflight => _inflight;

	/// <summary>Number of bulk operations: action lines are the ones starting with an operation name.</summary>
	public static int CountOperations(CapturedRequest request) =>
		request.Lines.Count(l => l.StartsWith("{\"index\":", StringComparison.Ordinal)
			|| l.StartsWith("{\"create\":", StringComparison.Ordinal)
			|| l.StartsWith("{\"update\":", StringComparison.Ordinal)
			|| l.StartsWith("{\"delete\":", StringComparison.Ordinal));

	/// <summary>The _id, _index and require_alias of every action line of the request.</summary>
	public static (string Id, string Index, bool Alias)[] ParseActions(CapturedRequest request) =>
		request.Lines.Where(l => l.StartsWith("{\"index\":", StringComparison.Ordinal) || l.StartsWith("{\"create\":", StringComparison.Ordinal)
				|| l.StartsWith("{\"update\":", StringComparison.Ordinal) || l.StartsWith("{\"delete\":", StringComparison.Ordinal))
			.Select(l =>
			{
				var op = System.Text.Json.JsonDocument.Parse(l).RootElement.EnumerateObject().First().Value;
				return (op.TryGetProperty("_id", out var id) ? id.GetString() : null,
					op.TryGetProperty("_index", out var index) ? index.GetString() : null,
					op.TryGetProperty("require_alias", out var alias) && alias.GetBoolean());
			}).ToArray();

	/// <summary>The _id of every action line of the request, in order.</summary>
	public static string[] IdsOf(CapturedRequest request) =>
		request.Lines.Where(l => l.StartsWith("{\"index\":", StringComparison.Ordinal) || l.StartsWith("{\"create\":", StringComparison.Ordinal)
				|| l.StartsWith("{\"update\":", StringComparison.Ordinal) || l.StartsWith("{\"delete\":", StringComparison.Ordinal))
			.Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement.EnumerateObject().First().Value.GetProperty("_id").GetString())
			.ToArray();

	private sealed class ScriptedInvoker(ScriptedTransport owner, Func<int, CapturedRequest, ScriptedResponse> script) : IRequestInvoker
	{
		private readonly InMemoryRequestInvoker _inner = new();

		public ResponseFactory ResponseFactory => _inner.ResponseFactory;

		public void Dispose() { }

		public TResponse Request<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData postData)
			where TResponse : TransportResponse, new() =>
			throw new NotSupportedException();

		public async Task<TResponse> RequestAsync<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData postData, CancellationToken cancellationToken)
			where TResponse : TransportResponse, new()
		{
			var inflight = Interlocked.Increment(ref owner._inflight);
			try
			{
				int max;
				while (inflight > (max = owner._maxInflight))
					if (Interlocked.CompareExchange(ref owner._maxInflight, inflight, max) == max) break;

				using var ms = new MemoryStream();
				if (postData is not null)
					await postData.WriteAsync(ms, boundConfiguration.ConnectionSettings, false, cancellationToken).ConfigureAwait(false);

				CapturedRequest captured;
				int attempt;
				lock (owner._requests)
				{
					attempt = owner._requests.Count;
					captured = new CapturedRequest(endpoint.PathAndQuery, ms.ToArray(), attempt, boundConfiguration.RequestTimeout);
					owner._requests.Add(captured);
				}

				var response = script(attempt, captured);
				if (response.Throw is not null) throw response.Throw;

				// give concurrent requests a chance to overlap
				await Task.Yield();

				var body = response.RawBody is not null ? Encoding.UTF8.GetBytes(response.RawBody)
					: response.ItemStatuses is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(BuildBody(response.ItemStatuses, response.IncludeErrorsFlag, captured, attempt));
				return await _inner.BuildResponseAsync<TResponse>(endpoint, boundConfiguration, postData, cancellationToken, body, response.HttpStatus, "application/json").ConfigureAwait(false);
			}
			finally { Interlocked.Decrement(ref owner._inflight); }
		}

		/// <summary>
		/// Builds the response body like the server does: when the request asked for it through filter_path, every item
		/// reports _id (echoing an explicit id, otherwise generated as "gen-{attempt}-{position}") and _index
		/// (the action's index or the path target, resolved to "{name}-000001" for require_alias actions).
		/// </summary>
		private static string BuildBody(int[] statuses, bool includeErrorsFlag, CapturedRequest request, int attempt)
		{
			var identity = request.PathAndQuery.Contains("items.*._id", StringComparison.Ordinal);
			var actions = identity ? ParseActions(request) : System.Array.Empty<(string Id, string Index, bool Alias)>();
			var target = request.Path.EndsWith("/_bulk", StringComparison.Ordinal) ? request.Path[..^"/_bulk".Length] : null;

			var sb = new StringBuilder("{");
			if (includeErrorsFlag) sb.Append("\"errors\":").Append(statuses.Any(s => s is < 200 or > 299) ? "true" : "false").Append(',');
			sb.Append("\"items\":[");
			for (var i = 0; i < statuses.Length; i++)
			{
				if (i > 0) sb.Append(',');
				var s = statuses[i];
				sb.Append("{\"index\":{");
				if (identity && i < actions.Length)
				{
					var (id, index, alias) = actions[i];
					var resolved = index ?? target ?? "default-index";
					if (alias) resolved += "-000001";
					// ids and index names are arbitrary text in the property tests, so they must be escaped like a server does
					sb.Append("\"_index\":").Append(System.Text.Json.JsonSerializer.Serialize(resolved))
						.Append(",\"_id\":").Append(System.Text.Json.JsonSerializer.Serialize(id ?? $"gen-{attempt}-{i}")).Append(',');
				}
				sb.Append("\"status\":").Append(s);
				if (s is < 200 or > 299)
					sb.Append(",\"error\":{\"type\":\"t").Append(s).Append("\",\"reason\":\"r").Append(s).Append("\"}");
				sb.Append("}}");
			}
			return sb.Append("]}").ToString();
		}
	}
}
