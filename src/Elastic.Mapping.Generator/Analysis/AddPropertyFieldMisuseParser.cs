// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Walks the body of a <c>ConfigureMappings</c> method and reports misuse of
/// <c>AddField</c> (used on an object/nested parent) or <c>AddProperty</c>
/// (used on a leaf parent) where the parent type is statically known from model attributes.
/// </summary>
internal static class AddPropertyFieldMisuseParser
{
	// Methods we validate (user-facing only; generated AddPropertyField/AddFieldDirect are excluded)
	private static readonly HashSet<string> ValidatedMethods = ["AddField", "AddProperty"];

	/// <summary>
	/// Parses a <c>ConfigureMappings</c> method and returns any misuse findings.
	/// </summary>
	/// <param name="method">The resolved method symbol for <c>ConfigureMappings</c>.</param>
	/// <param name="fieldNameToType">
	/// Map from Elasticsearch field name → Elasticsearch field type, derived from the document model's properties.
	/// </param>
	/// <param name="ct">Cancellation token.</param>
	public static ImmutableArray<MappingMisuseFinding> Parse(
		IMethodSymbol method,
		IReadOnlyDictionary<string, string> fieldNameToType,
		CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
		if (syntaxRef == null)
			return ImmutableArray<MappingMisuseFinding>.Empty;

		var methodSyntax = syntaxRef.GetSyntax(ct);
		var findings = ImmutableArray.CreateBuilder<MappingMisuseFinding>();

		foreach (var invocation in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			ct.ThrowIfCancellationRequested();

			if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
				continue;

			var methodName = memberAccess.Name.Identifier.Text;
			if (!ValidatedMethods.Contains(methodName))
				continue;

			var args = invocation.ArgumentList.Arguments;
			if (args.Count < 1)
				continue;

			var firstArgExpr = args[0].Expression;
			var pathLiteral = ExtractStringLiteral(firstArgExpr);

			// Non-constant or non-dotted path → nothing to validate
			if (pathLiteral == null || !pathLiteral.Contains('.'))
				continue;

			var dotIndex = pathLiteral.IndexOf('.');
			var parentFieldName = pathLiteral.Substring(0, dotIndex);
			var childSegment = pathLiteral.Substring(dotIndex + 1);

			// Parent not in the model → cannot validate
			if (!fieldNameToType.TryGetValue(parentFieldName, out var parentFieldType))
				continue;

			var isObjectLike = FieldTypes.IsObjectLike(parentFieldType);

			string? diagnosticId = null;
			if (methodName == "AddField" && isObjectLike)
				diagnosticId = "EMAP001";
			else if (methodName == "AddProperty" && !isObjectLike)
				diagnosticId = "EMAP002";

			if (diagnosticId == null)
				continue;

			var span = firstArgExpr.Span;
			findings.Add(new MappingMisuseFinding(
				diagnosticId,
				parentFieldName,
				pathLiteral,
				childSegment,
				parentFieldType,
				syntaxRef.SyntaxTree.FilePath,
				span.Start,
				span.Length));
		}

		return findings.ToImmutable();
	}

	internal static string? ExtractStringLiteral(ExpressionSyntax expression)
	{
		// Simple string literal: "name"
		if (expression is LiteralExpressionSyntax
			{
				RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression
			} literal)
			return literal.Token.ValueText;

		// Interpolated string with no interpolations: $"name"
		if (expression is InterpolatedStringExpressionSyntax interpolated &&
			interpolated.Contents.Count == 1 &&
			interpolated.Contents[0] is InterpolatedStringTextSyntax textContent)
			return textContent.TextToken.ValueText;

		return null;
	}
}
