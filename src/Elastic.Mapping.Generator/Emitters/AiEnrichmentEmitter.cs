// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Security.Cryptography;
using System.Text;
using Elastic.Mapping.Generator.Model;

namespace Elastic.Mapping.Generator.Emitters;

/// <summary>
/// Emits an <c>IAiEnrichmentProvider</c> implementation from an <see cref="AiEnrichmentModel"/>.
/// </summary>
internal static class AiEnrichmentEmitter
{
	public static string Emit(ContextMappingModel context, AiEnrichmentModel model)
	{
		var sb = new StringBuilder();
		SharedEmitterHelpers.EmitHeader(sb);

		if (!string.IsNullOrEmpty(context.Namespace))
		{
			sb.AppendLine($"namespace {context.Namespace};");
			sb.AppendLine();
		}

		sb.AppendLine($"static partial class {context.ContextTypeName}");
		sb.AppendLine("{");

		EmitProviderClass(sb, model, "\t");

		sb.AppendLine();
		sb.AppendLine($"\t/// <summary>Source-generated AI enrichment provider for {model.DocumentTypeName}.</summary>");
		sb.AppendLine($"\tpublic static {model.DocumentTypeName}AiEnrichmentProvider AiEnrichment {{ get; }} = new();");

		sb.AppendLine("}");
		return sb.ToString();
	}

	private static void EmitProviderClass(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		var className = $"{model.DocumentTypeName}AiEnrichmentProvider";
		var baseName = model.LookupIndexName
			?? (model.WriteAlias != null ? $"{model.WriteAlias}-ai-cache" : $"{model.DocumentTypeName.ToLowerInvariant()}-ai-cache");
		var lookupIndexName = baseName;
		var fieldsHash = ComputeFieldsHash(model);
		var policyName = $"{lookupIndexName}-ai-policy";
		var pipelineName = $"{lookupIndexName}-ai-pipeline";

		sb.AppendLine($"{indent}/// <summary>Generated AI enrichment provider for <see cref=\"global::{model.DocumentTypeFullyQualifiedName}\"/>.</summary>");
		sb.AppendLine($"{indent}public sealed class {className} : global::Elastic.Mapping.IAiEnrichmentProvider");
		sb.AppendLine($"{indent}{{");

		EmitFieldPromptHashes(sb, model, indent + "\t");
		EmitFieldPromptHashFieldNames(sb, model, indent + "\t");
		EmitEnrichmentFields(sb, model, indent + "\t");
		EmitRequiredSourceFields(sb, model, indent + "\t");
		EmitBuildPrompt(sb, model, indent + "\t");
		EmitParseResponse(sb, model, indent + "\t");
		EmitLookupInfrastructure(sb, model, lookupIndexName, policyName, pipelineName, fieldsHash, indent + "\t");

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitFieldPromptHashes(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public global::System.Collections.Generic.IReadOnlyDictionary<string, string> FieldPromptHashes {{ get; }} =");
		sb.AppendLine($"{indent}\tnew global::System.Collections.Generic.Dictionary<string, string>");
		sb.AppendLine($"{indent}\t{{");
		foreach (var o in model.Outputs)
			sb.AppendLine($"{indent}\t\t[\"{o.FieldName}\"] = \"{o.PromptHash}\",");
		sb.AppendLine($"{indent}\t}};");
		sb.AppendLine();
	}

	private static void EmitFieldPromptHashFieldNames(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public global::System.Collections.Generic.IReadOnlyDictionary<string, string> FieldPromptHashFieldNames {{ get; }} =");
		sb.AppendLine($"{indent}\tnew global::System.Collections.Generic.Dictionary<string, string>");
		sb.AppendLine($"{indent}\t{{");
		foreach (var o in model.Outputs)
			sb.AppendLine($"{indent}\t\t[\"{o.FieldName}\"] = \"{o.PromptHashFieldName}\",");
		sb.AppendLine($"{indent}\t}};");
		sb.AppendLine();
	}

	private static void EmitEnrichmentFields(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		var fields = string.Join(", ", model.Outputs.Select(o => $"\"{o.FieldName}\""));
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string[] EnrichmentFields {{ get; }} = [{fields}];");
		sb.AppendLine();
	}

	private static void EmitRequiredSourceFields(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		var fields = string.Join(", ", model.Inputs.Select(i => $"\"{i.FieldName}\""));
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string[] RequiredSourceFields {{ get; }} = [{fields}];");
		sb.AppendLine();
	}

	private static void EmitBuildPrompt(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string? BuildPrompt(global::System.Text.Json.JsonElement source, global::System.Collections.Generic.IReadOnlyCollection<string> staleFields)");
		sb.AppendLine($"{indent}{{");

		// Extract input fields
		for (var i = 0; i < model.Inputs.Length; i++)
		{
			var input = model.Inputs[i];
			sb.AppendLine($"{indent}\tstring? _in{i} = null;");
			sb.AppendLine($"{indent}\tif (source.TryGetProperty(\"{input.FieldName}\", out var _iv{i}) && _iv{i}.ValueKind == global::System.Text.Json.JsonValueKind.String)");
			sb.AppendLine($"{indent}\t\t_in{i} = _iv{i}.GetString();");
		}

		if (model.Inputs.Length > 0)
		{
			var checks = string.Join(" || ", Enumerable.Range(0, model.Inputs.Length).Select(i => $"string.IsNullOrWhiteSpace(_in{i})"));
			sb.AppendLine($"{indent}\tif ({checks}) return null;");
		}

		sb.AppendLine();

		// Build JSON schema properties conditionally per stale field
		sb.AppendLine($"{indent}\tvar properties = new global::System.Collections.Generic.List<string>();");
		sb.AppendLine($"{indent}\tvar required = new global::System.Collections.Generic.List<string>();");
		sb.AppendLine();

		foreach (var output in model.Outputs)
		{
			sb.AppendLine($"{indent}\tif (global::System.Linq.Enumerable.Contains(staleFields, \"{output.FieldName}\"))");
			sb.AppendLine($"{indent}\t{{");
			sb.AppendLine($"{indent}\t\trequired.Add(\"\\\"{output.FieldName}\\\"\");");

			if (output.IsArray)
			{
				var schemaBuilder = new StringBuilder();
				schemaBuilder.Append($"\\\"{output.FieldName}\\\": {{\\\"type\\\":\\\"array\\\",\\\"items\\\":{{\\\"type\\\":\\\"string\\\"}}");
				if (output.MinItems > 0)
					schemaBuilder.Append($",\\\"minItems\\\":{output.MinItems}");
				if (output.MaxItems > 0)
					schemaBuilder.Append($",\\\"maxItems\\\":{output.MaxItems}");
				schemaBuilder.Append($",\\\"description\\\":\\\"{EscapeForStringLiteral(output.Description)}\\\"}}");
				sb.AppendLine($"{indent}\t\tproperties.Add(\"{schemaBuilder}\");");
			}
			else
			{
				sb.AppendLine($"{indent}\t\tproperties.Add(\"\\\"{output.FieldName}\\\": {{\\\"type\\\":\\\"string\\\",\\\"description\\\":\\\"{EscapeForStringLiteral(output.Description)}\\\"}}\");");
			}

			sb.AppendLine($"{indent}\t}}");
		}

		sb.AppendLine();
		sb.AppendLine($"{indent}\tif (properties.Count == 0) return null;");
		sb.AppendLine();

		// Build the prompt string
		var roleSection = !string.IsNullOrEmpty(model.Role)
			? $"<role>\\n{EscapeForStringLiteral(model.Role!)}\\n</role>\\n\\n"
			: "";

		sb.AppendLine($"{indent}\tvar reqJson = string.Join(\",\", required);");
		sb.AppendLine($"{indent}\tvar propJson = string.Join(\",\", properties);");
		sb.AppendLine();

		// Use input variables in the prompt
		var inputSection = new StringBuilder();
		for (var i = 0; i < model.Inputs.Length; i++)
		{
			var input = model.Inputs[i];
			inputSection.Append($"<{input.FieldName}>\" + (_in{i} ?? \"\") + \"</{input.FieldName}>\\n");
		}

		sb.AppendLine($"{indent}\treturn \"{roleSection}<task>\\nReturn a single valid JSON object matching the schema. No markdown, no extra text, no trailing characters.\\n</task>\\n\\n<json-schema>\\n{{\\\"type\\\":\\\"object\\\",\\\"required\\\":[\" + reqJson + \"],\\\"additionalProperties\\\":false,\\\"properties\\\":{{\" + propJson + \"}}}}\\n</json-schema>\\n\\n<rules>\\n- Extract ONLY from provided content. Never hallucinate.\\n- Be specific. Avoid generic phrases.\\n- Output exactly one JSON object.\\n</rules>\\n\\n<document>\\n{inputSection}</document>\";");

		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	private static void EmitParseResponse(StringBuilder sb, AiEnrichmentModel model, string indent)
	{
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string? ParseResponse(string llmResponse, global::System.Collections.Generic.IReadOnlyCollection<string> enrichedFields)");
		sb.AppendLine($"{indent}{{");

		sb.AppendLine($"{indent}\tvar cleaned = llmResponse.Replace(\"```json\", \"\").Replace(\"```\", \"\").Trim().TrimEnd('`');");
		sb.AppendLine($"{indent}\tif (cleaned.EndsWith(\"}}}}\") && !cleaned.Contains(\"{{{{\"))");
		sb.AppendLine($"{indent}\t\tcleaned = cleaned.Substring(0, cleaned.Length - 1);");
		sb.AppendLine();

		sb.AppendLine($"{indent}\tglobal::System.Text.Json.JsonDocument doc;");
		sb.AppendLine($"{indent}\ttry {{ doc = global::System.Text.Json.JsonDocument.Parse(cleaned); }}");
		sb.AppendLine($"{indent}\tcatch (global::System.Text.Json.JsonException) {{ return null; }}");
		sb.AppendLine();

		sb.AppendLine($"{indent}\tusing (doc)");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tvar root = doc.RootElement;");
		sb.AppendLine($"{indent}\t\tif (root.ValueKind != global::System.Text.Json.JsonValueKind.Object) return null;");
		sb.AppendLine();

		sb.AppendLine($"{indent}\t\tvar sb = new global::System.Text.StringBuilder();");
		sb.AppendLine($"{indent}\t\tsb.Append('{{');");
		sb.AppendLine($"{indent}\t\tvar first = true;");
		sb.AppendLine();

		foreach (var output in model.Outputs)
		{
			sb.AppendLine($"{indent}\t\tif (global::System.Linq.Enumerable.Contains(enrichedFields, \"{output.FieldName}\") && root.TryGetProperty(\"{output.FieldName}\", out var _{output.PropertyName}))");
			sb.AppendLine($"{indent}\t\t{{");
			sb.AppendLine($"{indent}\t\t\tif (!first) sb.Append(',');");
			sb.AppendLine($"{indent}\t\t\tfirst = false;");
			sb.AppendLine($"{indent}\t\t\tsb.Append(\"\\\"{output.FieldName}\\\":\");");
			sb.AppendLine($"{indent}\t\t\tsb.Append(_{output.PropertyName}.GetRawText());");
			sb.AppendLine($"{indent}\t\t\tsb.Append(\",\\\"{output.PromptHashFieldName}\\\":\\\"{output.PromptHash}\\\"\");");
			sb.AppendLine($"{indent}\t\t}}");
		}

		sb.AppendLine();
		sb.AppendLine($"{indent}\t\tsb.Append('}}');");

		sb.AppendLine($"{indent}\t\tvar result = sb.ToString();");
		sb.AppendLine($"{indent}\t\treturn result == \"{{}}\" ? null : result;");
		sb.AppendLine($"{indent}\t}}");

		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	private static void EmitLookupInfrastructure(
		StringBuilder sb, AiEnrichmentModel model,
		string lookupIndexName, string policyName, string pipelineName,
		string fieldsHash,
		string indent)
	{
		// LookupIndexName
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string LookupIndexName => \"{lookupIndexName}\";");
		sb.AppendLine();

		// LookupIndexMapping
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string LookupIndexMapping => @\"{{");
		sb.AppendLine($"{indent}  \"\"mappings\"\": {{");
		sb.AppendLine($"{indent}    \"\"properties\"\": {{");
		sb.AppendLine($"{indent}      \"\"{model.MatchFieldName}\"\": {{ \"\"type\"\": \"\"keyword\"\" }},");
		foreach (var output in model.Outputs)
		{
			sb.AppendLine($"{indent}      \"\"{output.FieldName}\"\": {{ \"\"type\"\": \"\"text\"\" }},");
			sb.AppendLine($"{indent}      \"\"{output.PromptHashFieldName}\"\": {{ \"\"type\"\": \"\"keyword\"\" }},");
		}
		sb.AppendLine($"{indent}      \"\"created_at\"\": {{ \"\"type\"\": \"\"date\"\" }}");
		sb.AppendLine($"{indent}    }}");
		sb.AppendLine($"{indent}  }}");
		sb.AppendLine($"{indent}}}\";");
		sb.AppendLine();

		// MatchField
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string MatchField => \"{model.MatchFieldName}\";");
		sb.AppendLine();

		// FieldsHash
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string FieldsHash => \"{fieldsHash}\";");
		sb.AppendLine();

		// EnrichPolicyName
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string EnrichPolicyName => \"{policyName}\";");
		sb.AppendLine();

		// EnrichPolicyBody
		var enrichFields = new List<string>();
		foreach (var output in model.Outputs)
		{
			enrichFields.Add($"\"\"{output.FieldName}\"\"");
			enrichFields.Add($"\"\"{output.PromptHashFieldName}\"\"");
		}
		var enrichFieldsJson = string.Join(", ", enrichFields);

		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string EnrichPolicyBody => @\"{{");
		sb.AppendLine($"{indent}  \"\"match\"\": {{");
		sb.AppendLine($"{indent}    \"\"indices\"\": \"\"{lookupIndexName}\"\",");
		sb.AppendLine($"{indent}    \"\"match_field\"\": \"\"{model.MatchFieldName}\"\",");
		sb.AppendLine($"{indent}    \"\"enrich_fields\"\": [{enrichFieldsJson}]");
		sb.AppendLine($"{indent}  }}");
		sb.AppendLine($"{indent}}}\";");
		sb.AppendLine();

		// PipelineName
		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string PipelineName => \"{pipelineName}\";");
		sb.AppendLine();

		// PipelineBody — per-field copy script, with fields_hash in description for conditional update
		var scriptParts = new List<string>();
		foreach (var output in model.Outputs)
		{
			scriptParts.Add($"if (e.{output.FieldName} != null) {{ ctx.{output.FieldName} = e.{output.FieldName}; ctx.{output.PromptHashFieldName} = e.{output.PromptHashFieldName}; }}");
		}
		var script = $"def e = ctx._enrich; {string.Join(" ", scriptParts)} ctx.remove('_enrich');";

		sb.AppendLine($"{indent}/// <inheritdoc />");
		sb.AppendLine($"{indent}public string PipelineBody => @\"{{");
		sb.AppendLine($"{indent}  \"\"description\"\": \"\"AI enrichment pipeline [fields_hash:{fieldsHash}]\"\",");
		sb.AppendLine($"{indent}  \"\"processors\"\": [");
		sb.AppendLine($"{indent}    {{");
		sb.AppendLine($"{indent}      \"\"enrich\"\": {{");
		sb.AppendLine($"{indent}        \"\"policy_name\"\": \"\"{policyName}\"\",");
		sb.AppendLine($"{indent}        \"\"field\"\": \"\"{model.MatchFieldName}\"\",");
		sb.AppendLine($"{indent}        \"\"target_field\"\": \"\"_enrich\"\",");
		sb.AppendLine($"{indent}        \"\"max_matches\"\": 1,");
		sb.AppendLine($"{indent}        \"\"ignore_missing\"\": true");
		sb.AppendLine($"{indent}      }}");
		sb.AppendLine($"{indent}    }},");
		sb.AppendLine($"{indent}    {{");
		sb.AppendLine($"{indent}      \"\"script\"\": {{");
		sb.AppendLine($"{indent}        \"\"if\"\": \"\"ctx._enrich != null\"\",");
		sb.AppendLine($"{indent}        \"\"source\"\": \"\"{EscapeForVerbatim(script)}\"\"");
		sb.AppendLine($"{indent}      }}");
		sb.AppendLine($"{indent}    }}");
		sb.AppendLine($"{indent}  ]");
		sb.AppendLine($"{indent}}}\";");
		sb.AppendLine();
	}

	// ── Helpers ──

	private static string ComputeFieldsHash(AiEnrichmentModel model)
	{
		var fieldsString = string.Join(",",
			model.Outputs.SelectMany(o => new[] { o.FieldName, o.PromptHashFieldName }));
		using var sha = SHA256.Create();
		var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(fieldsString));
		return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant().Substring(0, 8);
	}

	private static string EscapeForStringLiteral(string value) =>
		value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

	private static string EscapeForVerbatim(string value) =>
		value.Replace("\"", "\"\"");
}
