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
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Ingest.Transport;
using Elastic.Mapping;
using static Elastic.Ingest.Elasticsearch.ElasticsearchChannelStatics;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// A composable Elasticsearch channel that delegates behavior to pluggable strategies.
/// <para>Auto-configures from <see cref="ElasticsearchTypeContext"/> when provided, or accepts
/// manually configured strategies for full control.</para>
/// </summary>
public class ElasticsearchChannel<TEvent> : ElasticsearchChannelBase<TEvent, ElasticsearchChannelOptions<TEvent>>
	where TEvent : class
{
	private readonly IDocumentIngestStrategy<TEvent> _ingestStrategy;
	private readonly IBootstrapStrategy _bootstrapStrategy;
	private readonly IIndexProvisioningStrategy _provisioningStrategy;
	private readonly IAliasStrategy _aliasStrategy;
	private readonly IRolloverStrategy? _rolloverStrategy;
	private readonly string _bulkUrl;
	private readonly string _templateName;
	private readonly string _templateWildcard;
	private readonly Func<string>? _getMappingsJson;
	private readonly Func<string>? _getMappingSettings;
	private readonly string? _dataStreamType;

	/// <inheritdoc cref="ElasticsearchChannel{TEvent}"/>
	public ElasticsearchChannel(ElasticsearchChannelOptions<TEvent> options) : this(options, null) { }

	/// <inheritdoc cref="ElasticsearchChannel{TEvent}"/>
	public ElasticsearchChannel(
		ElasticsearchChannelOptions<TEvent> options,
		ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners
	) : base(options, callbackListeners)
	{
		var tc = options.TypeContext;

		// Resolve template names
		_templateName = options.TemplateName ?? ResolveTemplateName(tc);
		_templateWildcard = options.TemplateWildcard ?? ResolveTemplateWildcard(tc);

		// Resolve strategies
		_ingestStrategy = options.IngestStrategy ?? ResolveIngestStrategy(tc, options);
		_bootstrapStrategy = options.BootstrapStrategy ?? ResolveBootstrapStrategy(tc, options);
		_provisioningStrategy = options.ProvisioningStrategy ?? ResolveProvisioningStrategy(tc);
		_aliasStrategy = options.AliasStrategy ?? ResolveAliasStrategy(tc);
		_rolloverStrategy = options.RolloverStrategy;

		// Resolve bootstrap configuration
		_getMappingsJson = options.GetMappingsJson ?? tc?.GetMappingsJson;
		_getMappingSettings = options.GetMappingSettings ?? tc?.GetSettingsJson;
		_dataStreamType = options.DataStreamType ?? tc?.IndexStrategy?.Type;

		_bulkUrl = _ingestStrategy.GetBulkUrl(base.BulkPathAndQuery);
	}

	/// <inheritdoc />
	protected override string TemplateName => _templateName;

	/// <inheritdoc />
	protected override string TemplateWildcard => _templateWildcard;

	/// <inheritdoc />
	protected override string RefreshTargets => _ingestStrategy.RefreshTargets;

	/// <inheritdoc />
	protected override string BulkPathAndQuery => _bulkUrl;

	/// <inheritdoc />
	protected override BulkOperationHeader CreateBulkOperationHeader(TEvent document) =>
		_ingestStrategy.CreateBulkOperationHeader(document, ChannelHash);

	/// <inheritdoc />
	public override async Task<bool> BootstrapElasticsearchAsync(
		BootstrapMethod bootstrapMethod, string? ilmPolicy = null, CancellationToken ctx = default)
	{
		if (bootstrapMethod == BootstrapMethod.None) return true;

		var context = CreateBootstrapContext(bootstrapMethod, ilmPolicy);
		return await _bootstrapStrategy.BootstrapAsync(context, ctx).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod, string? ilmPolicy = null)
	{
		if (bootstrapMethod == BootstrapMethod.None) return true;

		var context = CreateBootstrapContext(bootstrapMethod, ilmPolicy);
		return _bootstrapStrategy.Bootstrap(context);
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
			IndexPattern = _templateWildcard,
			ActiveSearchAlias = Options.TypeContext?.SearchStrategy?.ReadAlias
		};
		return await _aliasStrategy.ApplyAliasesAsync(context, ctx).ConfigureAwait(false);
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
			IndexPattern = _templateWildcard,
			ActiveSearchAlias = Options.TypeContext?.SearchStrategy?.ReadAlias
		};
		return _aliasStrategy.ApplyAliases(context);
	}

	/// <summary>
	/// Gets the provisioning strategy for external use (e.g., by orchestrators).
	/// </summary>
	public IIndexProvisioningStrategy ProvisioningStrategy => _provisioningStrategy;

	/// <summary>
	/// Triggers a manual rollover of the target index or data stream.
	/// Requires <see cref="ElasticsearchChannelOptions{TEvent}.RolloverStrategy"/> to be set.
	/// </summary>
	public async Task<bool> RolloverAsync(string? maxAge = null, string? maxSize = null, long? maxDocs = null, CancellationToken ctx = default)
	{
		if (_rolloverStrategy == null)
			throw new InvalidOperationException("RolloverStrategy is not configured.");

		var context = new RolloverContext
		{
			Transport = Options.Transport,
			Target = _ingestStrategy.RefreshTargets,
			MaxAge = maxAge,
			MaxSize = maxSize,
			MaxDocs = maxDocs
		};
		return await _rolloverStrategy.RolloverAsync(context, ctx).ConfigureAwait(false);
	}

	/// <summary>
	/// Triggers a manual rollover of the target index or data stream.
	/// Requires <see cref="ElasticsearchChannelOptions{TEvent}.RolloverStrategy"/> to be set.
	/// </summary>
	public bool Rollover(string? maxAge = null, string? maxSize = null, long? maxDocs = null)
	{
		if (_rolloverStrategy == null)
			throw new InvalidOperationException("RolloverStrategy is not configured.");

		var context = new RolloverContext
		{
			Transport = Options.Transport,
			Target = _ingestStrategy.RefreshTargets,
			MaxAge = maxAge,
			MaxSize = maxSize,
			MaxDocs = maxDocs
		};
		return _rolloverStrategy.Rollover(context);
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

	private BootstrapContext CreateBootstrapContext(BootstrapMethod bootstrapMethod, string? ilmPolicy)
	{
		return new BootstrapContext
		{
			Transport = Options.Transport,
			BootstrapMethod = bootstrapMethod,
			TemplateName = _templateName,
			TemplateWildcard = _templateWildcard,
			IlmPolicy = ilmPolicy ?? Options.IlmPolicy,
			GetMappingsJson = _getMappingsJson,
			GetMappingSettings = _getMappingSettings,
			DataStreamType = _dataStreamType,
			DataStreamLifecycleRetention = Options.DataStreamLifecycleRetention
		};
	}

	private static string ResolveTemplateName(ElasticsearchTypeContext? tc)
	{
		if (tc == null)
			throw new InvalidOperationException(
				"TemplateName must be set explicitly when ElasticsearchTypeContext is not provided.");

		return tc.EntityTarget switch
		{
			EntityTarget.DataStream when tc.IndexStrategy?.DataStreamName != null =>
				$"{tc.IndexStrategy.Type}-{tc.IndexStrategy.Dataset}",
			EntityTarget.Index when tc.IndexStrategy?.WriteTarget != null =>
				$"{tc.IndexStrategy.WriteTarget}-template",
			_ => tc.MappedType?.Name.ToLowerInvariant() + "-template"
				?? throw new InvalidOperationException("Cannot resolve template name from TypeContext.")
		};
	}

	private static string ResolveTemplateWildcard(ElasticsearchTypeContext? tc)
	{
		if (tc == null)
			throw new InvalidOperationException(
				"TemplateWildcard must be set explicitly when ElasticsearchTypeContext is not provided.");

		return tc.EntityTarget switch
		{
			EntityTarget.DataStream when tc.IndexStrategy?.DataStreamName != null =>
				$"{tc.IndexStrategy.Type}-{tc.IndexStrategy.Dataset}-*",
			EntityTarget.Index when tc.IndexStrategy?.WriteTarget != null =>
				$"{tc.IndexStrategy.WriteTarget}-*",
			_ => tc.MappedType?.Name.ToLowerInvariant() + "-*"
				?? throw new InvalidOperationException("Cannot resolve template wildcard from TypeContext.")
		};
	}

	private IDocumentIngestStrategy<TEvent> ResolveIngestStrategy(
		ElasticsearchTypeContext? tc, ElasticsearchChannelOptions<TEvent> options)
	{
		if (tc == null)
			throw new InvalidOperationException(
				"IngestStrategy must be set explicitly when ElasticsearchTypeContext is not provided.");

		return tc.EntityTarget switch
		{
			EntityTarget.DataStream => new DataStreamIngestStrategy<TEvent>(
				tc.IndexStrategy?.DataStreamName
				?? throw new InvalidOperationException("DataStreamName must be set for DataStream targets."),
				DefaultBulkPathAndQuery),

			EntityTarget.WiredStream => new WiredStreamIngestStrategy<TEvent>(DefaultBulkPathAndQuery),

			EntityTarget.Index => new TypeContextIndexIngestStrategy<TEvent>(
				tc,
				tc.IndexStrategy?.WriteTarget ?? typeof(TEvent).Name.ToLowerInvariant(),
				DefaultBulkPathAndQuery),

			_ => throw new InvalidOperationException($"Unknown EntityTarget: {tc.EntityTarget}")
		};
	}

	private static IBootstrapStrategy ResolveBootstrapStrategy(ElasticsearchTypeContext? tc, ElasticsearchChannelOptions<TEvent> options)
	{
		if (tc == null)
			return new DefaultBootstrapStrategy(new ComponentTemplateStep(), new IndexTemplateStep());

		if (tc.EntityTarget == EntityTarget.WiredStream)
			return new DefaultBootstrapStrategy(new NoopBootstrapStep());

		if (tc.EntityTarget == EntityTarget.DataStream)
		{
			var steps = new List<IBootstrapStep> { new ComponentTemplateStep() };

			if (!string.IsNullOrEmpty(options.DataStreamLifecycleRetention))
				steps.Add(new DataStreamLifecycleStep(options.DataStreamLifecycleRetention!));

			steps.Add(new DataStreamTemplateStep());
			return new DefaultBootstrapStrategy(steps.ToArray());
		}

		return new DefaultBootstrapStrategy(new ComponentTemplateStep(), new IndexTemplateStep());
	}

	private static IIndexProvisioningStrategy ResolveProvisioningStrategy(ElasticsearchTypeContext? tc)
	{
		if (tc?.GetContentHash != null)
			return new HashBasedReuseProvisioning();

		return new AlwaysCreateProvisioning();
	}

	private IAliasStrategy ResolveAliasStrategy(ElasticsearchTypeContext? tc)
	{
		if (tc?.SearchStrategy?.ReadAlias != null && tc.IndexStrategy?.WriteTarget != null)
		{
			var indexFormat = tc.IndexStrategy.DatePattern != null
				? $"{tc.IndexStrategy.WriteTarget}-{{0}}"
				: tc.IndexStrategy.WriteTarget;
			return new LatestAndSearchAliasStrategy(indexFormat);
		}

		return new NoAliasStrategy();
	}
}
