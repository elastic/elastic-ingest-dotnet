// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// A composable Elasticsearch channel that delegates behavior to a single composed
/// <see cref="IIngestStrategy{TEvent}"/>. Auto-configures from <see cref="Mapping.ElasticsearchTypeContext"/>
/// when provided, or accepts an explicit strategy for full control.
/// </summary>
public class IngestChannel<TEvent> : IngestChannelBase<TEvent, IngestChannelOptions<TEvent>>
	where TEvent : class
{
	private readonly IIngestStrategy<TEvent> _strategy;
	private readonly string _bulkUrl;

	/// <inheritdoc cref="IngestChannel{TEvent}"/>
	public IngestChannel(IngestChannelOptions<TEvent> options) : this(options, null) { }

	/// <inheritdoc cref="IngestChannel{TEvent}"/>
	public IngestChannel(
		IngestChannelOptions<TEvent> options,
		ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners
	) : base(options, callbackListeners)
	{
		_strategy = options.Strategy;
		_bulkUrl = _strategy.DocumentIngest.GetBulkUrl(base.BulkPathAndQuery);
	}

	/// <inheritdoc />
	protected override string TemplateName => _strategy.TemplateName;

	/// <inheritdoc />
	protected override string TemplateWildcard => _strategy.TemplateWildcard;

	/// <inheritdoc />
	protected override string RefreshTargets => _strategy.DocumentIngest.RefreshTargets;

	/// <inheritdoc />
	protected override string BulkPathAndQuery => _bulkUrl;

	/// <inheritdoc />
	protected override BulkOperationHeader CreateBulkOperationHeader(TEvent document) =>
		_strategy.DocumentIngest.CreateBulkOperationHeader(document, ChannelHash);

	/// <inheritdoc />
	public override async Task<bool> BootstrapElasticsearchAsync(
		BootstrapMethod bootstrapMethod, CancellationToken ctx = default)
	{
		if (bootstrapMethod == BootstrapMethod.None) return true;

		var context = CreateBootstrapContext(bootstrapMethod);
		var result = await _strategy.Bootstrap.BootstrapAsync(context, ctx).ConfigureAwait(false);
		ChannelHash = context.ChannelHash;
		return result;
	}

	/// <inheritdoc />
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod)
	{
		if (bootstrapMethod == BootstrapMethod.None) return true;

		var context = CreateBootstrapContext(bootstrapMethod);
		var result = _strategy.Bootstrap.Bootstrap(context);
		ChannelHash = context.ChannelHash;
		return result;
	}

	/// <summary>
	/// Applies alias management after indexing is complete.
	/// </summary>
	public async Task<bool> ApplyAliasesAsync(string indexName, CancellationToken ctx = default)
	{
		var context = new AliasContext
		{
			Transport = Options.Transport,
			IndexName = indexName,
			IndexPattern = _strategy.TemplateWildcard,
			ActiveSearchAlias = Options.TypeContext?.SearchStrategy?.ReadAlias
		};
		return await _strategy.AliasStrategy.ApplyAliasesAsync(context, ctx).ConfigureAwait(false);
	}

	/// <summary>
	/// Applies alias management after indexing is complete.
	/// </summary>
	public bool ApplyAliases(string indexName)
	{
		var context = new AliasContext
		{
			Transport = Options.Transport,
			IndexName = indexName,
			IndexPattern = _strategy.TemplateWildcard,
			ActiveSearchAlias = Options.TypeContext?.SearchStrategy?.ReadAlias
		};
		return _strategy.AliasStrategy.ApplyAliases(context);
	}

	/// <summary>
	/// Gets the provisioning strategy for external use (e.g., by orchestrators).
	/// </summary>
	public IIndexProvisioningStrategy ProvisioningStrategy => _strategy.Provisioning;

	/// <summary>
	/// Triggers a manual rollover of the target index or data stream.
	/// Requires the strategy to include a <see cref="IRolloverStrategy"/>.
	/// </summary>
	public async Task<bool> RolloverAsync(string? maxAge = null, string? maxSize = null, long? maxDocs = null, CancellationToken ctx = default)
	{
		if (_strategy.Rollover == null)
			throw new InvalidOperationException("RolloverStrategy is not configured.");

		var context = new RolloverContext
		{
			Transport = Options.Transport,
			Target = _strategy.DocumentIngest.RefreshTargets,
			MaxAge = maxAge,
			MaxSize = maxSize,
			MaxDocs = maxDocs
		};
		return await _strategy.Rollover.RolloverAsync(context, ctx).ConfigureAwait(false);
	}

	/// <summary>
	/// Triggers a manual rollover of the target index or data stream.
	/// Requires the strategy to include a <see cref="IRolloverStrategy"/>.
	/// </summary>
	public bool Rollover(string? maxAge = null, string? maxSize = null, long? maxDocs = null)
	{
		if (_strategy.Rollover == null)
			throw new InvalidOperationException("RolloverStrategy is not configured.");

		var context = new RolloverContext
		{
			Transport = Options.Transport,
			Target = _strategy.DocumentIngest.RefreshTargets,
			MaxAge = maxAge,
			MaxSize = maxSize,
			MaxDocs = maxDocs
		};
		return _strategy.Rollover.Rollover(context);
	}

	/// <inheritdoc />
	protected override (string, string) GetDefaultIndexTemplate(
		string name, string match, string mappingsName, string settingsName, string hash)
	{
		// Not used in strategy-based bootstrap flow, but required by abstract base
		var indexTemplateBody = @$"{{
                ""index_patterns"": [""{match}""],
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"" ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}"",
                    ""hash"": ""{hash}""
                }}
            }}";
		return (name, indexTemplateBody);
	}

	private BootstrapContext CreateBootstrapContext(BootstrapMethod bootstrapMethod) =>
		new()
		{
			Transport = Options.Transport,
			BootstrapMethod = bootstrapMethod,
			TemplateName = _strategy.TemplateName,
			TemplateWildcard = _strategy.TemplateWildcard,
			GetMappingsJson = _strategy.GetMappingsJson,
			GetMappingSettings = _strategy.GetMappingSettings,
			DataStreamType = _strategy.DataStreamType
		};
}
