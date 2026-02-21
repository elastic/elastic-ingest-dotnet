// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Parses the ConfigureAnalysis static method to extract analyzer, tokenizer,
/// token filter, char filter, and normalizer names.
/// </summary>
internal static class ConfigureAnalysisParser
{
	public static AnalysisComponentsModel Parse(INamedTypeSymbol typeSymbol, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		// Find the ConfigureAnalysis method
		var configureAnalysisMethod = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		if (configureAnalysisMethod == null)
			return AnalysisComponentsModel.Empty;

		return ParseFromMethod(configureAnalysisMethod, ct);
	}

	/// <summary>
	/// Parses analysis components from a specific method symbol.
	/// Used when the method lives on a context class or configuration class.
	/// </summary>
	public static AnalysisComponentsModel ParseFromMethod(IMethodSymbol method, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		var analyzers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var tokenizers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var tokenFilters = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var charFilters = ImmutableArray.CreateBuilder<AnalysisComponentModel>();
		var normalizers = ImmutableArray.CreateBuilder<AnalysisComponentModel>();

		// Get the syntax for the method
		var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
		if (syntaxRef == null)
			return AnalysisComponentsModel.Empty;

		var methodSyntax = syntaxRef.GetSyntax(ct);

		// Find all invocation expressions in the method body
		var invocations = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

		foreach (var invocation in invocations)
		{
			ct.ThrowIfCancellationRequested();

			// Check if it's a method call like .Analyzer("name", ...) or .Tokenizer("name", ...)
			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
				continue;

			var methodName = memberAccess.Name.Identifier.Text;
			var args = invocation.ArgumentList.Arguments;

			// Must have at least one argument (the name)
			if (args.Count < 1)
				continue;

			// Get the first argument as the component name
			var firstArg = args[0].Expression;
			var componentName = ExtractStringLiteral(firstArg);

			if (string.IsNullOrEmpty(componentName))
				continue;

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

		return new AnalysisComponentsModel(
			analyzers.ToImmutable(),
			tokenizers.ToImmutable(),
			tokenFilters.ToImmutable(),
			charFilters.ToImmutable(),
			normalizers.ToImmutable()
		);
	}

	private static string? ExtractStringLiteral(ExpressionSyntax expression)
	{
		// Handle simple string literals: "name"
		if (expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
			return literal.Token.ValueText;

		// Handle interpolated strings without interpolations: $"name"
		if (expression is InterpolatedStringExpressionSyntax interpolated &&
			interpolated.Contents.Count == 1 &&
			interpolated.Contents[0] is InterpolatedStringTextSyntax textContent)
			return textContent.TextToken.ValueText;

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
