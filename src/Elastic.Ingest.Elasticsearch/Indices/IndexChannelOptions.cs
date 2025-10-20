// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Indices;

/// <summary>
/// For scripted hash bulk upserts returns per operation the field and the hash to use for the scripted upsert
/// </summary>
public record HashedBulkUpdate
{
	/// <summary>
	/// For scripted hash bulk upserts returns per operation the field and the hash to use for the scripted upsert
	/// </summary>
	/// <param name="field">The field to check the previous hash against</param>
	/// <param name="hash">The current hash of the document</param>
	/// <param name="updateScript">The update script</param>
	/// <param name="parameters"></param>
	public HashedBulkUpdate(string field, string hash, string? updateScript, IDictionary<string, string>? parameters)
		: this(field, hash)
	{
		UpdateScript = updateScript;
		Parameters = parameters;
	}

	/// <summary>
	/// For scripted hash bulk upserts returns per operation the field and the hash to use for the scripted upsert
	/// </summary>
	/// <param name="field">The field to check the previous hash against</param>
	/// <param name="hash">The current hash of the document</param>
	public HashedBulkUpdate(string field, string hash)
	{
		Field = field;
		Hash = hash;
	}

	/// <summary>
	/// A short SHA256 hash of the provided <paramref name="components"/>
	/// </summary>
	/// <param name="components"></param>
	/// <returns></returns>
	public static string CreateHash(params string[] components)
	{
#if NET8_0_OR_GREATER
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("", components))))[..16].ToLowerInvariant();
#else
		var sha = SHA256.Create();
		var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("", components)));
		return BitConverter.ToString(hash).Replace("-", "")[..16].ToLowerInvariant();
#endif
	}

	/// <summary>The field to check the previous hash against</summary>
	public string Field { get; init; }

	/// <summary>The current hash of the document</summary>
	public string Hash { get; init; }

	/// <summary>Optional update script if hashes match defaults to ''</summary>
	public string? UpdateScript { get; init; }

	/// <summary> Optional additional parameters for <see cref="UpdateScript"/> </summary>
	public IDictionary<string, string>? Parameters { get; init; }
};

/// <summary>
/// Provides options to <see cref="IndexChannel{TEvent}"/> to control how and where data gets written to Elasticsearch
/// </summary>
/// <typeparam name="TEvent"></typeparam>
public class IndexChannelOptions<TEvent> : ElasticsearchChannelOptionsBase<TEvent>
{
	/// <inheritdoc cref="IndexChannelOptions{TEvent}"/>
	public IndexChannelOptions(ITransport transport) : base(transport) { }

	/// <summary>
	/// Gets or sets the format string for the Elastic search index. The current <c>DateTimeOffset</c> is passed as parameter
	/// 0.
	/// <para> Defaults to "<typeparamref name="TEvent"/>.Name.ToLowerInvariant()-{0:yyyy.MM.dd}"</para>
	/// <para> If no {0} parameter is defined the index name is effectively fixed</para>
	/// </summary>
	public virtual string IndexFormat { get; set; } = $"{typeof(TEvent).Name.ToLowerInvariant()}-{{0:yyyy.MM.dd}}";

	/// <summary>
	/// Gets or sets the offset to use for the index <c>DateTimeOffset</c>. The default value is null, which uses the system local
	/// offset. Use "00:00" for UTC.
	/// </summary>
	public TimeSpan? IndexOffset { get; set; }

	/// <summary>
	/// Provide a per document <c>DateTimeOffset</c> to be used as the date passed as parameter 0 to <see cref="IndexFormat"/>
	/// </summary>
	public Func<TEvent, DateTimeOffset?>? TimestampLookup { get; set; }

	/// <summary>
	/// If the document provides an ID, this allows you to set a per document `_id`.
	/// <para>If an `_id` is defined, an `_index` bulk operation will be created.</para>
	/// <para>Otherwise (the default) `_create` bulk operation will be issued for the document.</para>
	/// <para>Read more about bulk operations here:</para>
	/// <para>https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-bulk.html#bulk-api-request-body</para>
	/// </summary>
	public Func<TEvent, string>? BulkOperationIdLookup { get; set; }

	/// <summary>
	/// Uses the callback provided to <see cref="BulkOperationIdLookup"/> to determine if this is in fact an update operation
	/// <para>If this returns true the document will be sent as an upsert operation</para>
	/// <para>Otherwise (the default) `index` bulk operation will be issued for the document.</para>
	/// <para>Read more about bulk operations here:</para>
	/// <para>https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-bulk.html#bulk-api-request-body</para>
	/// </summary>
	public Func<TEvent, string, bool>? BulkUpsertLookup { get; set; }

	/// <summary>
	/// Uses the callback provided to <see cref="BulkOperationIdLookup"/> to determine if this is in fact an update operation
	/// <para>Returns the field and the hash to use for the scripted upsert </para>
	/// <para>The string passed to the callback is the hash of the current channel options that can be used to hash bust in case of channel option changes</para>
	/// <para>Otherwise (the default) `index` bulk operation will be issued for the document.</para>
	/// <para>Read more about bulk operations here:</para>
	/// <para>https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-bulk.html#bulk-api-request-body</para>
	/// </summary>
	public Func<TEvent, string, HashedBulkUpdate>? ScriptedHashBulkUpsertLookup { get; set; }

	/// <summary>
	/// Control the operation header for each bulk operation.
	/// </summary>
	public OperationMode OperationMode { get; set; }
}
