// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Mapping.Generator.Model;

namespace Elastic.Mapping.Generator.Emitters;

/// <summary>
/// Emits the mapping context partial class with nested resolver classes.
/// </summary>
internal static class ContextEmitter
{
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

		// Emit All property
		sb.AppendLine("\t/// <summary>All registered Elasticsearch type contexts.</summary>");
		sb.Append("\tpublic static global::System.Collections.Generic.IReadOnlyList<global::Elastic.Mapping.ElasticsearchTypeContext> All { get; } =\n\t\t[");
		var typeNames = model.TypeRegistrations.Select(r => $"{r.ResolverName}.Context").ToList();
		sb.Append(string.Join(", ", typeNames));
		sb.AppendLine("];");
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
		sb.AppendLine($"{indent}public sealed class {reg.ResolverName}Resolver");
		sb.AppendLine($"{indent}{{");

		// Hashes as instance properties backed by constants
		sb.AppendLine($"{indent}\tprivate const string _hash = \"{combinedHash}\";");
		sb.AppendLine($"{indent}\tprivate const string _settingsHash = \"{settingsHash}\";");
		sb.AppendLine($"{indent}\tprivate const string _mappingsHash = \"{mappingsHash}\";");
		sb.AppendLine();

		// Context instance - now includes EntityTarget, DataStreamMode, and accessor delegates
		sb.AppendLine($"{indent}\t/// <summary>Elasticsearch context metadata.</summary>");
		sb.AppendLine($"{indent}\tpublic global::Elastic.Mapping.ElasticsearchTypeContext Context {{ get; }} = new(");
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
		sb.AppendLine($"{indent}\t\tMappedType: typeof(global::{typeFqn})");

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
		EmitJsonMethods(sb, settingsJson, mappingsJson, indexJson, indent + "\t");

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
		sb.AppendLine($"{indent}\t\tSearchStrategy.Pattern,");
		sb.AppendLine($"{indent}\t\tGetPropertyMap,");
		sb.AppendLine($"{indent}\t\tTextFields: TextFields");
		sb.AppendLine($"{indent}\t);");

		sb.AppendLine($"{indent}}}");
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
		sb.AppendLine($"{indent}\tpublic global::System.Collections.Generic.IReadOnlyList<global::Elastic.Mapping.ElasticsearchTypeContext> All => {model.ContextTypeName}.All;");
		sb.AppendLine();
		sb.AppendLine($"{indent}\tpublic global::Elastic.Mapping.TypeFieldMetadata? GetTypeMetadata(global::System.Type type)");
		sb.AppendLine($"{indent}\t{{");

		foreach (var reg in model.TypeRegistrations)
		{
			sb.AppendLine($"{indent}\t\tif (type == typeof(global::{reg.TypeFullyQualifiedName}))");
			sb.AppendLine($"{indent}\t\t\treturn {model.ContextTypeName}.{reg.ResolverName}.GetTypeFieldMetadata();");
		}

		sb.AppendLine($"{indent}\t\treturn null;");
		sb.AppendLine($"{indent}\t}}");
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
		if (!string.IsNullOrEmpty(idx.SearchPattern))
			sb.AppendLine($"{indent}\tPattern = \"{idx.SearchPattern}\",");

		if (!string.IsNullOrEmpty(idx.ReadAlias))
			sb.AppendLine($"{indent}\tReadAlias = \"{idx.ReadAlias}\",");
	}

	private static void EmitJsonMethods(StringBuilder sb, string settingsJson, string mappingsJson, string indexJson, string indent)
	{
		// Static backing methods — referenced by the Context field initializer
		sb.AppendLine($"{indent}private static string _GetSettingsJson() =>");
		SharedEmitterHelpers.EmitRawStringLiteral(sb, settingsJson, indent + "\t");
		sb.AppendLine();

		sb.AppendLine($"{indent}private static string _GetMappingJson() =>");
		SharedEmitterHelpers.EmitRawStringLiteral(sb, mappingsJson, indent + "\t");
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
