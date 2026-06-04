// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Collections.Immutable;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Parses the ConfigureAnalysis static method to extract analyzer, tokenizer,
/// token filter, char filter, and normalizer names.
/// Supports delegation: if ConfigureAnalysis calls another method (e.g. a shared factory),
/// the parser follows the call graph transitively and accumulates names from all reachable methods.
/// </summary>
internal static class ConfigureAnalysisParser
{
	// The Elastic.Mapping fluent analysis builder — we skip recursing into its own methods
	// since they are the declaration site, not a user-authored delegation target.
	private const string AnalysisBuilderTypeName = "Elastic.Mapping.Analysis.AnalysisBuilder";

	public static AnalysisComponentsModel Parse(INamedTypeSymbol typeSymbol, Compilation compilation, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		// Find the ConfigureAnalysis method
		var configureAnalysisMethod = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		if (configureAnalysisMethod == null)
			return AnalysisComponentsModel.Empty;

		return ParseFromMethod(configureAnalysisMethod, compilation, ct);
	}

	/// <summary>
	/// Legacy overload used by <c>TypeAnalyzer</c> when a type has its own inline
	/// <c>ConfigureAnalysis</c> method. Delegation-following is not needed here since
	/// the type authors its own analysis; only the direct method body is parsed.
	/// </summary>
	public static AnalysisComponentsModel Parse(INamedTypeSymbol typeSymbol, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		var configureAnalysisMethod = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		if (configureAnalysisMethod == null)
			return AnalysisComponentsModel.Empty;

		// Inline parse: single-method body walk only, no semantic model / delegation.
		var syntaxRef = configureAnalysisMethod.DeclaringSyntaxReferences.FirstOrDefault();
		if (syntaxRef == null)
			return AnalysisComponentsModel.Empty;

		var methodSyntax = syntaxRef.GetSyntax(ct);

		var analyzers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var tokenizers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var tokenFilters = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var charFilters = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var normalizers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();

		foreach (var invocation in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			ct.ThrowIfCancellationRequested();
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
				continue;

			var methodName = memberAccess.Name.Identifier.Text;
			var args = invocation.ArgumentList.Arguments;
			if (args.Count < 1)
				continue;

			// No SemanticModel available — literals only (no const resolution).
			var componentName = ExtractStringLiteralOnly(args[0].Expression);
			if (string.IsNullOrEmpty(componentName))
				continue;

			var constantName = ToConstantName(componentName!);
			var component = new AnalysisComponentModel(constantName, componentName!);

			switch (methodName)
			{
				case "Analyzer":
					if (!analyzers.Any(a => a.Value == componentName)) analyzers.Add(component);
					break;
				case "Tokenizer":
					if (!tokenizers.Any(t => t.Value == componentName)) tokenizers.Add(component);
					break;
				case "TokenFilter":
					if (!tokenFilters.Any(f => f.Value == componentName)) tokenFilters.Add(component);
					break;
				case "CharFilter":
					if (!charFilters.Any(f => f.Value == componentName)) charFilters.Add(component);
					break;
				case "Normalizer":
					if (!normalizers.Any(n => n.Value == componentName)) normalizers.Add(component);
					break;
			}
		}

		return new AnalysisComponentsModel(
			analyzers.ToImmutable(), tokenizers.ToImmutable(),
			tokenFilters.ToImmutable(), charFilters.ToImmutable(), normalizers.ToImmutable());
	}

	// Literal-only extraction — used when no SemanticModel is available.
	private static string? ExtractStringLiteralOnly(ExpressionSyntax expression)
	{
		if (expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
			return literal.Token.ValueText;

		if (expression is InterpolatedStringExpressionSyntax interpolated &&
			interpolated.Contents.Count == 1 &&
			interpolated.Contents[0] is InterpolatedStringTextSyntax textContent)
			return textContent.TextToken.ValueText;

		return null;
	}

	/// <summary>
	/// Parses analysis components from a specific method symbol, following delegated calls
	/// transitively so names defined in shared factory methods are discovered automatically.
	/// </summary>
	public static AnalysisComponentsModel ParseFromMethod(IMethodSymbol method, Compilation compilation, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		var analyzers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var tokenizers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var tokenFilters = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var charFilters = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var normalizers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();

		// Visited set keyed on string (never a symbol) to break cycles and diamond delegation.
		var visited = new HashSet<string>();

		Walk(method, compilation, visited, analyzers, tokenizers, tokenFilters, charFilters, normalizers, ct);

		return new AnalysisComponentsModel(
			analyzers.ToImmutable(),
			tokenizers.ToImmutable(),
			tokenFilters.ToImmutable(),
			charFilters.ToImmutable(),
			normalizers.ToImmutable()
		);
	}

	private static void Walk(
		IMethodSymbol method,
		Compilation compilation,
		HashSet<string> visited,
		ImmutableArray<AnalysisComponentModel>.Builder analyzers,
		ImmutableArray<AnalysisComponentModel>.Builder tokenizers,
		ImmutableArray<AnalysisComponentModel>.Builder tokenFilters,
		ImmutableArray<AnalysisComponentModel>.Builder charFilters,
		ImmutableArray<AnalysisComponentModel>.Builder normalizers,
		CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		// Use OriginalDefinition to normalise generic instantiations (e.g. BuildAnalysis<T> → BuildAnalysis).
		var visitKey = method.OriginalDefinition.ToDisplayString();
		if (!visited.Add(visitKey))
			return;

		foreach (var syntaxRef in method.DeclaringSyntaxReferences)
		{
			ct.ThrowIfCancellationRequested();

			var methodSyntax = syntaxRef.GetSyntax(ct);

			// Obtain the semantic model for the specific syntax tree this method lives in.
			// SharedAnalysisFactory may live in a different file than the context class.
			var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);

			var invocations = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();
			foreach (var invocation in invocations)
			{
				ct.ThrowIfCancellationRequested();

				if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
					continue;

				var methodName = memberAccess.Name.Identifier.Text;
				var args = invocation.ArgumentList.Arguments;

				// AnalysisBuilder component registrations always take exactly (name, configure) = 2 args.
				// Inner builder calls like .Tokenizer("standard") (1 arg) or .Filters("a", "b") (2+ args, wrong name)
				// are excluded by requiring exactly 2 arguments, preventing false positives.
				if (args.Count == 2)
				{
					var firstArg = args[0].Expression;
					var componentName = ExtractStringLiteral(firstArg, semanticModel);

					if (!string.IsNullOrEmpty(componentName))
					{
						var constantName = ToConstantName(componentName!);
						var component = new AnalysisComponentModel(constantName, componentName!);

						switch (methodName)
						{
							case "Analyzer":
								if (!analyzers.Any(a => a.Value == componentName))
									analyzers.Add(component);
								break;
							case "Tokenizer":
								if (!tokenizers.Any(t => t.Value == componentName))
									tokenizers.Add(component);
								break;
							case "TokenFilter":
								if (!tokenFilters.Any(f => f.Value == componentName))
									tokenFilters.Add(component);
								break;
							case "CharFilter":
								if (!charFilters.Any(f => f.Value == componentName))
									charFilters.Add(component);
								break;
							case "Normalizer":
								if (!normalizers.Any(n => n.Value == componentName))
									normalizers.Add(component);
								break;
						}
					}
				}

				// Follow delegated method calls — recurse into any user-authored method we have source for.
				var invokedSymbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
				if (invokedSymbolInfo.Symbol is not IMethodSymbol invokedMethod)
					continue;

				// Skip the AnalysisBuilder fluent surface itself (its methods ARE the registration calls).
				var containingTypeName = invokedMethod.ContainingType?.ToDisplayString();
				if (containingTypeName == AnalysisBuilderTypeName)
					continue;

				// Only recurse when we have the source to parse.
				if (invokedMethod.DeclaringSyntaxReferences.Length == 0)
					continue;

				Walk(invokedMethod.OriginalDefinition, compilation, visited, analyzers, tokenizers, tokenFilters, charFilters, normalizers, ct);
			}
		}
	}

	/// <summary>
	/// Extracts a compile-time string value from an expression.
	/// Handles string literals, constant interpolated strings, and field/property constant references.
	/// </summary>
	private static string? ExtractStringLiteral(ExpressionSyntax expression, SemanticModel semanticModel)
	{
		// Handle simple string literals: "name"
		if (expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
			return literal.Token.ValueText;

		// Handle interpolated strings without interpolations: $"name"
		if (expression is InterpolatedStringExpressionSyntax interpolated &&
			interpolated.Contents.Count == 1 &&
			interpolated.Contents[0] is InterpolatedStringTextSyntax textContent)
			return textContent.TextToken.ValueText;

		// Handle const references: MyConst, SomeClass.MyConst, etc.
		// Resolved via the semantic model — works even when the const lives in another file.
		var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
		if (symbol is IFieldSymbol { HasConstantValue: true } field && field.ConstantValue is string constValue)
			return constValue;

		return null;
	}

	private static string ToConstantName(string value)
	{
		// Convert "product_name_analyzer" to "ProductNameAnalyzer" (preserving suffixes)
		// Convert snake_case to PascalCase
		var parts = value.Split('_');
		return string.Concat(parts.Select(p =>
			string.IsNullOrEmpty(p) ? "" : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()
		));
	}
}
