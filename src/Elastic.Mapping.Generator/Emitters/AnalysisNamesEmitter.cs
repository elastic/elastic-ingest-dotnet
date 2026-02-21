// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using System.Text;
using Elastic.Mapping.Generator.Model;

namespace Elastic.Mapping.Generator.Emitters;

/// <summary>
/// Emits the Analysis nested class with strongly-typed analyzer/tokenizer/filter accessors.
/// </summary>
internal static class AnalysisNamesEmitter
{
	/// <summary>
	/// Emits analysis names for a type registration within a context.
	/// </summary>
	public static string? EmitForContext(ContextMappingModel context, TypeRegistration reg)
	{
		if (!reg.AnalysisComponents.HasAnyComponents)
			return null;

		var sb = new StringBuilder();

		SharedEmitterHelpers.EmitHeader(sb);

		if (!string.IsNullOrEmpty(context.Namespace))
		{
			sb.AppendLine($"namespace {context.Namespace};");
			sb.AppendLine();
		}

		EmitAnalysisClass(sb, "", reg.ResolverName, reg.AnalysisComponents);

		return sb.ToString();
	}

	private static void EmitAnalysisClass(StringBuilder sb, string indent, string typeName, AnalysisComponentsModel components)
	{
		var className = $"{typeName}Analysis";

		sb.AppendLine($"{indent}/// <summary>Analysis component names and accessors for {typeName}.</summary>");
		sb.AppendLine($"{indent}public static class {className}");
		sb.AppendLine($"{indent}{{");

		EmitAnalyzersAccessorClass(sb, indent + "\t", components.Analyzers);

		sb.AppendLine();
		EmitTokenizersAccessorClass(sb, indent + "\t", components.Tokenizers);

		sb.AppendLine();
		EmitTokenFiltersAccessorClass(sb, indent + "\t", components.TokenFilters);

		sb.AppendLine();
		EmitCharFiltersAccessorClass(sb, indent + "\t", components.CharFilters);

		sb.AppendLine();
		EmitNormalizersAccessorClass(sb, indent + "\t", components.Normalizers);

		sb.AppendLine();

		sb.AppendLine($"{indent}\t/// <summary>Analyzer names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic static readonly AnalyzersAccessor Analyzers = new();");
		sb.AppendLine($"{indent}\t/// <summary>Tokenizer names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic static readonly TokenizersAccessor Tokenizers = new();");
		sb.AppendLine($"{indent}\t/// <summary>Token filter names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic static readonly TokenFiltersAccessor TokenFilters = new();");
		sb.AppendLine($"{indent}\t/// <summary>Char filter names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic static readonly CharFiltersAccessor CharFilters = new();");
		sb.AppendLine($"{indent}\t/// <summary>Normalizer names (custom).</summary>");
		sb.AppendLine($"{indent}\tpublic static readonly NormalizersAccessor Normalizers = new();");

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitAnalyzersAccessorClass(
		StringBuilder sb,
		string indent,
		ImmutableArray<AnalysisComponentModel> components
	)
	{
		sb.AppendLine($"{indent}/// <summary>Analyzer accessor with built-in and custom analyzers.</summary>");
		sb.AppendLine($"{indent}public sealed class AnalyzersAccessor : global::Elastic.Mapping.Analysis.AnalyzersAccessor");
		sb.AppendLine($"{indent}{{");

		foreach (var component in components)
		{
			sb.AppendLine($"{indent}\t/// <summary>Custom analyzer: {component.Value}</summary>");
			sb.AppendLine($"{indent}\tpublic string {component.ConstantName} => \"{component.Value}\";");
		}

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitTokenizersAccessorClass(
		StringBuilder sb,
		string indent,
		ImmutableArray<AnalysisComponentModel> components
	)
	{
		sb.AppendLine($"{indent}/// <summary>Tokenizer accessor with built-in and custom tokenizers.</summary>");
		sb.AppendLine($"{indent}public sealed class TokenizersAccessor : global::Elastic.Mapping.Analysis.TokenizersAccessor");
		sb.AppendLine($"{indent}{{");

		foreach (var component in components)
		{
			sb.AppendLine($"{indent}\t/// <summary>Custom tokenizer: {component.Value}</summary>");
			sb.AppendLine($"{indent}\tpublic string {component.ConstantName} => \"{component.Value}\";");
		}

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitTokenFiltersAccessorClass(
		StringBuilder sb,
		string indent,
		ImmutableArray<AnalysisComponentModel> components
	)
	{
		sb.AppendLine($"{indent}/// <summary>Token filter accessor with built-in and custom token filters.</summary>");
		sb.AppendLine($"{indent}public sealed class TokenFiltersAccessor : global::Elastic.Mapping.Analysis.TokenFiltersAccessor");
		sb.AppendLine($"{indent}{{");

		foreach (var component in components)
		{
			sb.AppendLine($"{indent}\t/// <summary>Custom token filter: {component.Value}</summary>");
			sb.AppendLine($"{indent}\tpublic string {component.ConstantName} => \"{component.Value}\";");
		}

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitCharFiltersAccessorClass(
		StringBuilder sb,
		string indent,
		ImmutableArray<AnalysisComponentModel> components
	)
	{
		sb.AppendLine($"{indent}/// <summary>Char filter accessor with built-in and custom char filters.</summary>");
		sb.AppendLine($"{indent}public sealed class CharFiltersAccessor : global::Elastic.Mapping.Analysis.CharFiltersAccessor");
		sb.AppendLine($"{indent}{{");

		foreach (var component in components)
		{
			sb.AppendLine($"{indent}\t/// <summary>Custom char filter: {component.Value}</summary>");
			sb.AppendLine($"{indent}\tpublic string {component.ConstantName} => \"{component.Value}\";");
		}

		sb.AppendLine($"{indent}}}");
	}

	private static void EmitNormalizersAccessorClass(
		StringBuilder sb,
		string indent,
		ImmutableArray<AnalysisComponentModel> components
	)
	{
		sb.AppendLine($"{indent}/// <summary>Normalizer accessor with custom normalizers.</summary>");
		sb.AppendLine($"{indent}public sealed class NormalizersAccessor : global::Elastic.Mapping.Analysis.NormalizersAccessor");
		sb.AppendLine($"{indent}{{");

		foreach (var component in components)
		{
			sb.AppendLine($"{indent}\t/// <summary>Custom normalizer: {component.Value}</summary>");
			sb.AppendLine($"{indent}\tpublic string {component.ConstantName} => \"{component.Value}\";");
		}

		sb.AppendLine($"{indent}}}");
	}
}
