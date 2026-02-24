// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.RegularExpressions;
using Elastic.Mapping.Generator.Model;

namespace Elastic.Mapping.Generator.Emitters;

/// <summary>
/// Emits the mapping context partial class with nested resolver classes.
/// </summary>
internal static class ContextEmitter
{
	private static readonly HashSet<string> WellKnownPlaceholders = new(StringComparer.OrdinalIgnoreCase)
	{
		"env", "environment", "namespace"
	};

	private record TemplatePlaceholder(string Name, bool IsWellKnown);

	private static List<TemplatePlaceholder> ParseTemplatePlaceholders(string template)
	{
		var placeholders = new List<TemplatePlaceholder>();
		foreach (Match match in Regex.Matches(template, @"\{(\w+)\}"))
		{
			var name = match.Groups[1].Value;
			var isWellKnown = WellKnownPlaceholders.Contains(name);
			if (placeholders.All(p => !p.Name.Equals(name, StringComparison.Ordinal)))
				placeholders.Add(new TemplatePlaceholder(name, isWellKnown));
		}
		return placeholders.OrderBy(p => p.IsWellKnown).ThenBy(p => p.Name).ToList();
	}

	private static bool IsTemplated(TypeRegistration reg) =>
		!string.IsNullOrEmpty(reg.IndexConfig?.NameTemplate);

	public static string Emit(ContextMappingModel model)
	{
		var sb = new StringBuilder();

		SharedEmitterHelpers.EmitHeader(sb);
		EmitNamespace(sb, model);

		return sb.ToString();
	}

	private static void EmitNamespace(StringBuilder sb, ContextMappingModel model)
	{
		if (!string.IsNullOrEmpty(model.Namespace))
		{
			sb.AppendLine($"namespace {model.Namespace};");
			sb.AppendLine();
		}

		EmitContextClass(sb, model);
	}

	private static void EmitContextClass(StringBuilder sb, ContextMappingModel model)
	{
		sb.AppendLine($"static partial class {model.ContextTypeName}");
		sb.AppendLine("{");

		// Emit nested resolver classes
		foreach (var reg in model.TypeRegistrations)
		{
			EmitResolverClass(sb, model, reg, "\t");
			sb.AppendLine();
		}

		// Emit static resolver properties
		foreach (var reg in model.TypeRegistrations)
		{
			sb.AppendLine($"\t/// <summary>Elasticsearch resolver for {reg.ResolverName}.</summary>");
			sb.AppendLine($"\tpublic static {reg.ResolverName}Resolver {reg.ResolverName} {{ get; }} = new();");
			sb.AppendLine();
		}

		// Emit All as IReadOnlyDictionary<Type, TypeFieldMetadata>
		// Deduplicate by type — variants of the same type share identical field metadata
		sb.AppendLine("\t/// <summary>Type field metadata for all registered document types.</summary>");
		sb.AppendLine("\tpublic static global::System.Collections.Generic.IReadOnlyDictionary<global::System.Type, global::Elastic.Mapping.TypeFieldMetadata> All { get; } =");
		sb.AppendLine("\t\tnew global::System.Collections.Generic.Dictionary<global::System.Type, global::Elastic.Mapping.TypeFieldMetadata>");
		sb.AppendLine("\t\t{");
		var emittedTypes = new HashSet<string>();
		foreach (var reg in model.TypeRegistrations)
		{
			if (emittedTypes.Add(reg.TypeFullyQualifiedName))
				sb.AppendLine($"\t\t\t[typeof(global::{reg.TypeFullyQualifiedName})] = {reg.ResolverName}.GetTypeFieldMetadata(),");
		}
		sb.AppendLine("\t\t};");
		sb.AppendLine();

		// Emit Instance property
		sb.AppendLine("\t/// <summary>Mapping context instance for DI registration.</summary>");
		sb.AppendLine("\tpublic static global::Elastic.Mapping.IElasticsearchMappingContext Instance { get; } = new _MappingContext();");
		sb.AppendLine();

		// Emit _MappingContext nested class
		EmitMappingContextClass(sb, model, "\t");

		sb.AppendLine("}");
	}

	private static void EmitResolverClass(StringBuilder sb, ContextMappingModel model, TypeRegistration reg, string indent)
	{
		var typeModel = reg.TypeModel;
		var typeFqn = reg.TypeFullyQualifiedName;

		var settingsJson = SharedEmitterHelpers.GenerateSettingsJson(typeModel);
		var mappingsJson = SharedEmitterHelpers.GenerateMappingsJson(typeModel);
		var indexJson = SharedEmitterHelpers.GenerateIndexJson(settingsJson, mappingsJson);

		var settingsHash = SharedEmitterHelpers.ComputeHash(settingsJson);
		var mappingsHash = SharedEmitterHelpers.ComputeHash(mappingsJson);
		var combinedHash = SharedEmitterHelpers.ComputeHash(indexJson);

		sb.AppendLine($"{indent}/// <summary>Generated Elasticsearch resolver for {reg.ResolverName}.</summary>");
		sb.AppendLine($"{indent}public sealed class {reg.ResolverName}Resolver : global::Elastic.Mapping.IStaticMappingResolver<global::{typeFqn}>");
		sb.AppendLine($"{indent}{{");

		// Hashes as instance properties backed by constants
		sb.AppendLine($"{indent}\tprivate const string _hash = \"{combinedHash}\";");
		sb.AppendLine($"{indent}\tprivate const string _settingsHash = \"{settingsHash}\";");
		sb.AppendLine($"{indent}\tprivate const string _mappingsHash = \"{mappingsHash}\";");
		sb.AppendLine();

		var templated = IsTemplated(reg);
		var contextFieldName = templated ? "_baseContext" : "Context";
		var contextVisibility = templated ? "private" : "public";

		if (templated)
			sb.AppendLine($"{indent}\t/// <summary>Base context with mappings and settings. Use <see cref=\"CreateContext\"/> to resolve the name template.</summary>");
		else
			sb.AppendLine($"{indent}\t/// <summary>Elasticsearch context metadata.</summary>");
		sb.AppendLine($"{indent}\t{contextVisibility} global::Elastic.Mapping.ElasticsearchTypeContext {contextFieldName} {{ get; }} = new(");
		sb.AppendLine($"{indent}\t\t_GetSettingsJson,");
		sb.AppendLine($"{indent}\t\t_GetMappingJson,");
		sb.AppendLine($"{indent}\t\t_GetIndexJson,");
		sb.AppendLine($"{indent}\t\t_hash,");
		sb.AppendLine($"{indent}\t\t_settingsHash,");
		sb.AppendLine($"{indent}\t\t_mappingsHash,");
		sb.AppendLine($"{indent}\t\tnew global::Elastic.Mapping.IndexStrategy()");
		EmitIndexStrategyInit(sb, reg, indent + "\t\t");
		sb.AppendLine(",");
		sb.AppendLine($"{indent}\t\tnew global::Elastic.Mapping.SearchStrategy()");
		EmitSearchStrategyInit(sb, reg, indent + "\t\t");
		sb.AppendLine(",");

		// EntityTarget (required)
		sb.AppendLine($"{indent}\t\tglobal::Elastic.Mapping.EntityTarget.{reg.EntityConfig.EntityTarget},");

		// DataStreamMode
		sb.AppendLine($"{indent}\t\tDataStreamMode: global::Elastic.Mapping.DataStreamMode.{reg.EntityConfig.DataStreamMode},");

		// Accessor delegates for [Id], [ContentHash], [Timestamp]
		EmitAccessorDelegate(sb, reg.IngestProperties.IdPropertyName, typeFqn, "GetId", false, indent + "\t\t");
		EmitAccessorDelegate(sb, reg.IngestProperties.ContentHashPropertyName, typeFqn, "GetContentHash", false, indent + "\t\t");

		// ContentHashFieldName
		if (reg.IngestProperties.ContentHashFieldName != null)
			sb.AppendLine($"{indent}\t\tContentHashFieldName: \"{reg.IngestProperties.ContentHashFieldName}\",");
		else
			sb.AppendLine($"{indent}\t\tContentHashFieldName: null,");

		EmitTimestampDelegate(sb, reg.IngestProperties.TimestampPropertyName, reg.IngestProperties.TimestampPropertyType, typeFqn, indent + "\t\t");

		// ConfigureAnalysis delegate
		if (reg.ConfigureAnalysisReference != null)
			sb.AppendLine($"{indent}\t\tConfigureAnalysis: {reg.ConfigureAnalysisReference},");
		else
			sb.AppendLine($"{indent}\t\tConfigureAnalysis: null,");

		// MappedType
		sb.AppendLine($"{indent}\t\tMappedType: typeof(global::{typeFqn}),");

		// IndexSettings
		if (reg.IndexSettingsReference != null)
			sb.AppendLine($"{indent}\t\tIndexSettings: {reg.IndexSettingsReference}");
		else
			sb.AppendLine($"{indent}\t\tIndexSettings: null");

		sb.AppendLine($"{indent}\t);");
		sb.AppendLine();

		// Instance accessors for hashes
		sb.AppendLine($"{indent}\t/// <summary>Combined hash of settings and mappings.</summary>");
		sb.AppendLine($"{indent}\tpublic string Hash => _hash;");
		sb.AppendLine();
		sb.AppendLine($"{indent}\t/// <summary>Hash of settings JSON only.</summary>");
		sb.AppendLine($"{indent}\tpublic string SettingsHash => _settingsHash;");
		sb.AppendLine();
		sb.AppendLine($"{indent}\t/// <summary>Hash of mappings JSON only.</summary>");
		sb.AppendLine($"{indent}\tpublic string MappingsHash => _mappingsHash;");
		sb.AppendLine();

		// Strategies as instance properties
		EmitIndexStrategy(sb, reg, indent + "\t");
		EmitSearchStrategy(sb, reg, indent + "\t");

		// JSON methods as instance methods (static backing)
		EmitJsonMethods(sb, settingsJson, mappingsJson, indexJson, reg, indent + "\t");

		// Fields, FieldMapping, IgnoredProperties, GetPropertyMap as instance-accessible
		SharedEmitterHelpers.EmitFieldsClass(sb, typeModel, indent + "\t");
		SharedEmitterHelpers.EmitFieldMappingClass(sb, typeModel, indent + "\t");
		SharedEmitterHelpers.EmitIgnoredProperties(sb, typeModel, indent + "\t");
		SharedEmitterHelpers.EmitGetPropertyMap(sb, typeModel, typeFqn, indent + "\t");
		SharedEmitterHelpers.EmitTextFieldsSet(sb, typeModel, indent + "\t");

		// GetTypeFieldMetadata method
		sb.AppendLine();
		sb.AppendLine($"{indent}\t/// <summary>Gets type field metadata for this resolver.</summary>");
		sb.AppendLine($"{indent}\tpublic global::Elastic.Mapping.TypeFieldMetadata GetTypeFieldMetadata() => new(");
		sb.AppendLine($"{indent}\t\tFieldMapping.PropertyToField,");
		sb.AppendLine($"{indent}\t\tIgnoredProperties,");
		sb.AppendLine($"{indent}\t\tGetPropertyMap,");
		sb.AppendLine($"{indent}\t\tTextFields: TextFields");
		sb.AppendLine($"{indent}\t);");

		// IStaticMappingResolver<T> members
		EmitBatchTrackingMembers(sb, reg, typeFqn, indent + "\t");

		// CreateContext method for templated names
		if (IsTemplated(reg))
			EmitCreateContextMethod(sb, reg, indent + "\t");

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitCreateContextMethod(StringBuilder sb, TypeRegistration reg, string indent)
	{
		var template = reg.IndexConfig!.NameTemplate!;
		var placeholders = ParseTemplatePlaceholders(template);

		// Build parameter list: required params first, well-known optional params last
		var parameters = new List<string>();
		foreach (var p in placeholders)
		{
			var paramName = p.Name;
			// Escape C# keywords
			if (paramName is "namespace" or "environment")
				paramName = "@" + paramName;

			parameters.Add(p.IsWellKnown
				? $"string? {paramName} = null"
				: $"string {paramName}");
		}

		sb.AppendLine();
		sb.AppendLine($"{indent}/// <summary>");
		sb.AppendLine($"{indent}/// Resolves the name template <c>\"{template}\"</c> and returns a concrete");
		sb.AppendLine($"{indent}/// <see cref=\"global::Elastic.Mapping.ElasticsearchTypeContext\"/>.");
		sb.AppendLine($"{indent}/// </summary>");
		sb.AppendLine($"{indent}public global::Elastic.Mapping.ElasticsearchTypeContext CreateContext({string.Join(", ", parameters)})");
		sb.AppendLine($"{indent}{{");

		// Resolve well-known placeholders
		foreach (var p in placeholders.Where(p => p.IsWellKnown))
		{
			var paramName = p.Name is "namespace" or "environment" ? "@" + p.Name : p.Name;
			sb.AppendLine($"{indent}\t{paramName} ??= global::Elastic.Mapping.ElasticsearchTypeContext.ResolveDefaultNamespace();");
		}

		// Build the interpolated string
		var interpolated = template;
		foreach (var p in placeholders)
		{
			var paramName = p.Name is "namespace" or "environment" ? "@" + p.Name : p.Name;
			interpolated = interpolated.Replace($"{{{p.Name}}}", $"{{{paramName}}}");
		}

		sb.AppendLine($"{indent}\tvar name = $\"{interpolated}\";");

		// Build the new context with resolved name
		var datePattern = reg.IndexConfig.DatePattern;
		var readAlias = reg.IndexConfig.ReadAlias;

		sb.AppendLine($"{indent}\treturn _baseContext with");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tIndexStrategy = new global::Elastic.Mapping.IndexStrategy");
		sb.AppendLine($"{indent}\t\t{{");
		sb.AppendLine($"{indent}\t\t\tWriteTarget = name,");
		if (!string.IsNullOrEmpty(datePattern))
			sb.AppendLine($"{indent}\t\t\tDatePattern = \"{datePattern}\",");
		sb.AppendLine($"{indent}\t\t}},");
		sb.AppendLine($"{indent}\t\tSearchStrategy = new global::Elastic.Mapping.SearchStrategy");
		sb.AppendLine($"{indent}\t\t{{");
		if (!string.IsNullOrEmpty(readAlias))
			sb.AppendLine($"{indent}\t\t\tReadAlias = \"{readAlias}\",");
		if (!string.IsNullOrEmpty(datePattern))
			sb.AppendLine($"{indent}\t\t\tPattern = name + \"-*\",");
		sb.AppendLine($"{indent}\t\t}},");
		sb.AppendLine($"{indent}\t}};");
		sb.AppendLine($"{indent}}}");
	}

	private static void EmitBatchTrackingMembers(StringBuilder sb, TypeRegistration reg, string typeFqn, string indent)
	{
		var ingest = reg.IngestProperties;
		sb.AppendLine();

		// SetBatchIndexDate
		if (ingest.BatchIndexDatePropertyName != null)
		{
			sb.AppendLine($"{indent}/// <inheritdoc />");
			sb.AppendLine($"{indent}public global::System.Action<global::{typeFqn}, global::System.DateTimeOffset>? SetBatchIndexDate {{ get; }} =");
			sb.AppendLine($"{indent}\tstatic (obj, val) => obj.{ingest.BatchIndexDatePropertyName} = val;");
		}
		else
		{
			sb.AppendLine($"{indent}/// <inheritdoc />");
			sb.AppendLine($"{indent}public global::System.Action<global::{typeFqn}, global::System.DateTimeOffset>? SetBatchIndexDate => null;");
		}
		sb.AppendLine();

		// SetLastUpdated
		if (ingest.LastUpdatedPropertyName != null)
		{
			sb.AppendLine($"{indent}/// <inheritdoc />");
			sb.AppendLine($"{indent}public global::System.Action<global::{typeFqn}, global::System.DateTimeOffset>? SetLastUpdated {{ get; }} =");
			sb.AppendLine($"{indent}\tstatic (obj, val) => obj.{ingest.LastUpdatedPropertyName} = val;");
		}
		else
		{
			sb.AppendLine($"{indent}/// <inheritdoc />");
			sb.AppendLine($"{indent}public global::System.Action<global::{typeFqn}, global::System.DateTimeOffset>? SetLastUpdated => null;");
		}
		sb.AppendLine();

		// BatchIndexDateFieldName
		if (ingest.BatchIndexDateFieldName != null)
			sb.AppendLine($"{indent}/// <inheritdoc />\n{indent}public string? BatchIndexDateFieldName => \"{ingest.BatchIndexDateFieldName}\";");
		else
			sb.AppendLine($"{indent}/// <inheritdoc />\n{indent}public string? BatchIndexDateFieldName => null;");
		sb.AppendLine();

		// LastUpdatedFieldName
		if (ingest.LastUpdatedFieldName != null)
			sb.AppendLine($"{indent}/// <inheritdoc />\n{indent}public string? LastUpdatedFieldName => \"{ingest.LastUpdatedFieldName}\";");
		else
			sb.AppendLine($"{indent}/// <inheritdoc />\n{indent}public string? LastUpdatedFieldName => null;");
	}

	private static void EmitAccessorDelegate(StringBuilder sb, string? propertyName, string typeFqn, string paramName, bool isLast, string indent)
	{
		if (propertyName != null)
		{
			var comma = isLast ? "" : ",";
			sb.AppendLine($"{indent}{paramName}: static (obj) => ((global::{typeFqn})obj).{propertyName}?.ToString(){comma}");
		}
		else
		{
			var comma = isLast ? "" : ",";
			sb.AppendLine($"{indent}{paramName}: null{comma}");
		}
	}

	private static void EmitTimestampDelegate(StringBuilder sb, string? propertyName, string? propertyType, string typeFqn, string indent)
	{
		if (propertyName != null && propertyType != null)
		{
			// Handle different timestamp types - convert to DateTimeOffset?
			if (propertyType == "System.DateTimeOffset" || propertyType == "System.DateTimeOffset?")
			{
				sb.AppendLine($"{indent}GetTimestamp: static (obj) => ((global::{typeFqn})obj).{propertyName},");
			}
			else if (propertyType == "System.DateTime" || propertyType == "System.DateTime?")
			{
				sb.AppendLine($"{indent}GetTimestamp: static (obj) => {{ var v = ((global::{typeFqn})obj).{propertyName}; return new global::System.DateTimeOffset(v); }},");
			}
			else
			{
				sb.AppendLine($"{indent}GetTimestamp: null,");
			}
		}
		else
		{
			sb.AppendLine($"{indent}GetTimestamp: null,");
		}
	}

	private static void EmitMappingContextClass(StringBuilder sb, ContextMappingModel model, string indent)
	{
		sb.AppendLine($"{indent}private sealed class _MappingContext : global::Elastic.Mapping.IElasticsearchMappingContext");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tpublic global::System.Collections.Generic.IReadOnlyDictionary<global::System.Type, global::Elastic.Mapping.TypeFieldMetadata> All => {model.ContextTypeName}.All;");
		sb.AppendLine($"{indent}}}");
	}

	private static void EmitIndexStrategy(StringBuilder sb, TypeRegistration reg, string indent)
	{
		sb.AppendLine($"{indent}/// <summary>Write target configuration.</summary>");
		sb.AppendLine($"{indent}public global::Elastic.Mapping.IndexStrategy IndexStrategy => new()");
		sb.AppendLine($"{indent}{{");

		if (reg.DataStreamConfig != null)
		{
			var ds = reg.DataStreamConfig;
			EmitDataStreamProperties(sb, ds, indent);
		}
		else if (reg.IndexConfig != null)
		{
			EmitIndexProperties(sb, reg.IndexConfig, indent);
		}

		sb.AppendLine($"{indent}}};");
		sb.AppendLine();
	}

	private static void EmitSearchStrategy(StringBuilder sb, TypeRegistration reg, string indent)
	{
		sb.AppendLine($"{indent}/// <summary>Search target configuration.</summary>");
		sb.AppendLine($"{indent}public global::Elastic.Mapping.SearchStrategy SearchStrategy => new()");
		sb.AppendLine($"{indent}{{");

		if (reg.DataStreamConfig != null)
		{
			sb.AppendLine($"{indent}\tPattern = \"{reg.DataStreamConfig.SearchPattern}\",");
		}
		else if (reg.IndexConfig != null)
		{
			EmitSearchProperties(sb, reg.IndexConfig, indent);
		}

		sb.AppendLine($"{indent}}};");
		sb.AppendLine();
	}

	private static void EmitIndexStrategyInit(StringBuilder sb, TypeRegistration reg, string indent)
	{
		sb.AppendLine($"{indent}{{");

		if (reg.DataStreamConfig != null)
		{
			var ds = reg.DataStreamConfig;
			EmitDataStreamProperties(sb, ds, indent);
		}
		else if (reg.IndexConfig != null)
		{
			EmitIndexProperties(sb, reg.IndexConfig, indent);
		}

		sb.Append($"{indent}}}");
	}

	private static void EmitSearchStrategyInit(StringBuilder sb, TypeRegistration reg, string indent)
	{
		sb.AppendLine($"{indent}{{");

		if (reg.DataStreamConfig != null)
		{
			sb.AppendLine($"{indent}\tPattern = \"{reg.DataStreamConfig.SearchPattern}\",");
		}
		else if (reg.IndexConfig != null)
		{
			EmitSearchProperties(sb, reg.IndexConfig, indent);
		}

		sb.Append($"{indent}}}");
	}

	private static void EmitDataStreamProperties(StringBuilder sb, DataStreamConfigModel ds, string indent)
	{
		if (ds.FullName != null)
			sb.AppendLine($"{indent}\tDataStreamName = \"{ds.FullName}\",");

		sb.AppendLine($"{indent}\tType = \"{ds.Type}\",");
		sb.AppendLine($"{indent}\tDataset = \"{ds.Dataset}\",");

		if (ds.Namespace != null)
			sb.AppendLine($"{indent}\tNamespace = \"{ds.Namespace}\",");
	}

	private static void EmitIndexProperties(StringBuilder sb, IndexConfigModel idx, string indent)
	{
		if (!string.IsNullOrEmpty(idx.WriteAlias))
			sb.AppendLine($"{indent}\tWriteTarget = \"{idx.WriteAlias}\",");
		else if (!string.IsNullOrEmpty(idx.Name))
			sb.AppendLine($"{indent}\tWriteTarget = \"{idx.Name}\",");

		if (!string.IsNullOrEmpty(idx.DatePattern))
			sb.AppendLine($"{indent}\tDatePattern = \"{idx.DatePattern}\",");
	}

	private static void EmitSearchProperties(StringBuilder sb, IndexConfigModel idx, string indent)
	{
		// Auto-derive search pattern from write target and date pattern
		var writeTarget = idx.WriteAlias ?? idx.Name;
		if (!string.IsNullOrEmpty(writeTarget) && !string.IsNullOrEmpty(idx.DatePattern))
			sb.AppendLine($"{indent}\tPattern = \"{writeTarget}-*\",");

		if (!string.IsNullOrEmpty(idx.ReadAlias))
			sb.AppendLine($"{indent}\tReadAlias = \"{idx.ReadAlias}\",");
	}

	private static void EmitJsonMethods(StringBuilder sb, string settingsJson, string mappingsJson, string indexJson, TypeRegistration reg, string indent)
	{
		// Static backing methods — referenced by the Context field initializer
		sb.AppendLine($"{indent}private static string _GetSettingsJson() =>");
		SharedEmitterHelpers.EmitRawStringLiteral(sb, settingsJson, indent + "\t");
		sb.AppendLine();

		if (reg.ConfigureMappingsReference != null && reg.ConfigureMappingsBuilderType != null)
		{
			// Emit base JSON as a static field, then merge overrides from ConfigureMappings at static init
			sb.AppendLine($"{indent}private static string _GetBaseMappingJson() =>");
			SharedEmitterHelpers.EmitRawStringLiteral(sb, mappingsJson, indent + "\t");
			sb.AppendLine();

			sb.AppendLine($"{indent}private static readonly string _mergedMappingJson = _ApplyMappingOverrides();");
			sb.AppendLine();
			sb.AppendLine($"{indent}private static string _ApplyMappingOverrides()");
			sb.AppendLine($"{indent}{{");
			sb.AppendLine($"{indent}\tvar builder = new {reg.ConfigureMappingsBuilderType}();");
			sb.AppendLine($"{indent}\tbuilder = {reg.ConfigureMappingsReference}(builder);");
			sb.AppendLine($"{indent}\treturn builder.Build().MergeIntoMappings(_GetBaseMappingJson());");
			sb.AppendLine($"{indent}}}");
			sb.AppendLine();
			sb.AppendLine($"{indent}private static string _GetMappingJson() => _mergedMappingJson;");
		}
		else
		{
			sb.AppendLine($"{indent}private static string _GetMappingJson() =>");
			SharedEmitterHelpers.EmitRawStringLiteral(sb, mappingsJson, indent + "\t");
		}
		sb.AppendLine();

		sb.AppendLine($"{indent}private static string _GetIndexJson() =>");
		SharedEmitterHelpers.EmitRawStringLiteral(sb, indexJson, indent + "\t");
		sb.AppendLine();

		// Instance accessors for public API
		sb.AppendLine($"{indent}/// <summary>Returns the index settings JSON.</summary>");
		sb.AppendLine($"{indent}public string GetSettingsJson() => _GetSettingsJson();");
		sb.AppendLine();
		sb.AppendLine($"{indent}/// <summary>Returns the mappings JSON.</summary>");
		sb.AppendLine($"{indent}public string GetMappingJson() => _GetMappingJson();");
		sb.AppendLine();
		sb.AppendLine($"{indent}/// <summary>Returns the complete index JSON (settings + mappings).</summary>");
		sb.AppendLine($"{indent}public string GetIndexJson() => _GetIndexJson();");
	}
}
