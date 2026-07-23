// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using Elastic.Mapping.Generator.Analysis;
using Elastic.Mapping.Generator.Diagnostics;
using Elastic.Mapping.Generator.Emitters;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Elastic.Mapping.Generator;

/// <summary>
/// Incremental source generator that generates Elasticsearch mapping resolver classes
/// from context classes annotated with <c>[ElasticsearchMappingContext]</c>.
/// </summary>
[Generator]
public class MappingSourceGenerator : IIncrementalGenerator
{
	private const string ElasticsearchMappingContextAttributeName = "Elastic.Mapping.ElasticsearchMappingContextAttribute";
	private const string IndexAttributePrefix = "Elastic.Mapping.IndexAttribute<";
	private const string DataStreamAttributePrefix = "Elastic.Mapping.DataStreamAttribute<";
	private const string WiredStreamAttributePrefix = "Elastic.Mapping.WiredStreamAttribute<";
	private const string ConfigureElasticsearchInterfacePrefix = "Elastic.Mapping.IConfigureElasticsearch<";
	private const string AiEnrichmentAttributePrefix = "Elastic.Mapping.AiEnrichmentAttribute<";

	// Ingest property attribute names
	private const string IdAttributeName = "Elastic.Mapping.IdAttribute";
	private const string ContentHashAttributeName = "Elastic.Mapping.ContentHashAttribute";
	private const string TimestampAttributeName = "Elastic.Mapping.TimestampAttribute";
	private const string BatchIndexDateAttributeName = "Elastic.Mapping.BatchIndexDateAttribute";
	private const string LastUpdatedAttributeName = "Elastic.Mapping.LastUpdatedAttribute";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var contextDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (node, _) => IsCandidateContextClass(node),
				transform: static (ctx, ct) => GetContextModel(ctx, ct)
			)
			.Where(static model => model != null)
			.Select(static (model, _) => model!);

		// Collect all contexts so we can deduplicate extension method generation
		// across contexts that register the same document type in the same namespace.
		var allContexts = contextDeclarations.Collect();
		context.RegisterSourceOutput(allContexts, static (ctx, models) => ExecuteAll(ctx, models));
	}

	private static bool IsCandidateContextClass(SyntaxNode node)
	{
		if (node is not TypeDeclarationSyntax typeDecl)
			return false;

		if (!typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
			return false;

		if (typeDecl.AttributeLists.Count == 0)
			return false;

		foreach (var attrList in typeDecl.AttributeLists)
		{
			foreach (var attr in attrList.Attributes)
			{
				var name = attr.Name.ToString();
				if (name.Contains("ElasticsearchMappingContext"))
					return true;
			}
		}

		return false;
	}

	private static ContextMappingModel? GetContextModel(GeneratorSyntaxContext context, CancellationToken ct)
	{
		var typeDecl = (TypeDeclarationSyntax)context.Node;
		var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, ct);

		if (symbol is not INamedTypeSymbol contextSymbol)
			return null;

		var contextAttr = contextSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ElasticsearchMappingContextAttributeName);

		if (contextAttr == null)
			return null;

		INamedTypeSymbol? jsonContextSymbol = null;
		var jsonContextArg = contextAttr.NamedArguments
			.FirstOrDefault(a => a.Key == "JsonContext");
		if (jsonContextArg.Key != null && jsonContextArg.Value.Value is INamedTypeSymbol jcs)
			jsonContextSymbol = jcs;

		var stjConfig = StjContextAnalyzer.Analyze(jsonContextSymbol);

		// Capture Compilation once at transform time — used transiently to follow delegated
		// ConfigureAnalysis calls across files. Never stored on model records.
		var compilation = context.SemanticModel.Compilation;

		var registrations = ImmutableArray.CreateBuilder<TypeRegistration>();

		foreach (var attr in contextSymbol.GetAttributes())
		{
			ct.ThrowIfCancellationRequested();

			var attrClassName = attr.AttributeClass?.ToDisplayString();
			if (attrClassName == null)
				continue;

			TypeRegistration? registration = null;
			if (attrClassName.StartsWith(IndexAttributePrefix, StringComparison.Ordinal))
				registration = ProcessIndexAttribute(attr, contextSymbol, stjConfig, compilation, ct);
			else if (attrClassName.StartsWith(DataStreamAttributePrefix, StringComparison.Ordinal))
				registration = ProcessDataStreamAttribute(attr, contextSymbol, stjConfig, "DataStream", compilation, ct);
			else if (attrClassName.StartsWith(WiredStreamAttributePrefix, StringComparison.Ordinal))
				registration = ProcessDataStreamAttribute(attr, contextSymbol, stjConfig, "WiredStream", compilation, ct);

			if (registration != null)
				registrations.Add(registration);
		}

		if (registrations.Count == 0)
			return null;

		// Detect [AiEnrichment<T>]
		AiEnrichmentModel? aiEnrichment = null;
		foreach (var attr in contextSymbol.GetAttributes())
		{
			ct.ThrowIfCancellationRequested();

			var attrClassName = attr.AttributeClass?.ToDisplayString();
			if (attrClassName == null || !attrClassName.StartsWith(AiEnrichmentAttributePrefix, StringComparison.Ordinal))
				continue;

			var typeArg = attr.AttributeClass?.TypeArguments.FirstOrDefault();
			if (typeArg is not INamedTypeSymbol documentType)
				continue;

			if (aiEnrichment != null)
				break; // Only one AI enrichment per context (enforced via diagnostic below)

			var role = GetNamedArg<string>(attr, "Role");
			var lookupIndexName = GetNamedArg<string>(attr, "LookupIndexName");
			var matchField = GetNamedArg<string>(attr, "MatchField");
			var indexVariant = GetNamedArg<string>(attr, "IndexVariant");

			// Resolve the WriteAlias from the matching [Index<T>] registration for this document type.
			// When IndexVariant is specified, only consider the registration with that variant.
			string? writeAlias = null;
			foreach (var reg in registrations)
			{
				if (reg.TypeFullyQualifiedName != documentType.ToDisplayString())
					continue;
				if (indexVariant != null && reg.Variant != indexVariant)
					continue;
				if (reg.IndexConfig?.WriteAlias != null)
				{
					writeAlias = reg.IndexConfig.WriteAlias;
					break;
				}
			}

			aiEnrichment = AiEnrichmentAnalyzer.Analyze(
				documentType, role, lookupIndexName, writeAlias, matchField, indexVariant, stjConfig, ct);
		}

		return new ContextMappingModel(
			contextSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
			contextSymbol.Name,
			stjConfig,
			registrations.ToImmutable(),
			aiEnrichment
		);
	}

	private static TypeRegistration? ProcessIndexAttribute(
		AttributeData attr,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		Compilation compilation,
		CancellationToken ct)
	{
		var typeArg = attr.AttributeClass?.TypeArguments.FirstOrDefault();
		if (typeArg is not INamedTypeSymbol targetType)
			return null;

		var entityConfig = new EntityConfigModel("Index", "Default");

		var indexConfig = new IndexConfigModel(
			GetNamedArg<string>(attr, "Name"),
			GetNamedArg<string>(attr, "NameTemplate"),
			GetNamedArg<string>(attr, "WriteAlias"),
			GetNamedArg<string>(attr, "ReadAlias"),
			GetNamedArg<string>(attr, "DatePattern"),
			GetNamedArg<int>(attr, "Shards", -1),
			GetNamedArg<int>(attr, "Replicas", -1),
			GetNamedArg<string>(attr, "RefreshInterval"),
			GetNamedArg<bool>(attr, "Dynamic", true),
			GetNamedArg<string>(attr, "MappingVersion")
		);

		var ingestProperties = AnalyzeIngestProperties(targetType, stjConfig);
		ExtractConfigAndVariant(attr, out var configClassName, out var configTypeSymbol, out var variant);

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, indexConfig, null, entityConfig, ingestProperties, configClassName, configTypeSymbol, variant, compilation, ct);
	}

	private static TypeRegistration? ProcessDataStreamAttribute(
		AttributeData attr,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		string entityTarget,
		Compilation compilation,
		CancellationToken ct)
	{
		var typeArg = attr.AttributeClass?.TypeArguments.FirstOrDefault();
		if (typeArg is not INamedTypeSymbol targetType)
			return null;

		var dataStreamModeValue = GetNamedEnumArg(attr, "DataStreamMode", "Default");
		var entityConfig = new EntityConfigModel(entityTarget, dataStreamModeValue);

		var type = GetNamedArg<string>(attr, "Type");
		var dataset = GetNamedArg<string>(attr, "Dataset");

		DataStreamConfigModel? dataStreamConfig = null;
		if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(dataset))
		{
			dataStreamConfig = new DataStreamConfigModel(
				type!,
				dataset!,
				GetNamedArg<string>(attr, "Namespace"),
				GetNamedArg<string>(attr, "MappingVersion")
			);
		}

		var ingestProperties = AnalyzeIngestProperties(targetType, stjConfig);
		ExtractConfigAndVariant(attr, out var configClassName, out var configTypeSymbol, out var variant);

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, null, dataStreamConfig, entityConfig, ingestProperties, configClassName, configTypeSymbol, variant, compilation, ct);
	}

	private static void ExtractConfigAndVariant(
		AttributeData attr,
		out string? configClassName,
		out INamedTypeSymbol? configTypeSymbol,
		out string? variant)
	{
		configClassName = null;
		configTypeSymbol = null;
		var configArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Configuration");
		if (configArg.Key != null && configArg.Value.Value is INamedTypeSymbol configType)
		{
			configClassName = configType.ToDisplayString();
			configTypeSymbol = configType;
		}
		variant = GetNamedArg<string>(attr, "Variant");
	}

	private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";

	private static IngestPropertyModel AnalyzeIngestProperties(INamedTypeSymbol targetType, StjContextConfig? stjConfig = null)
	{
		string? idPropName = null, idPropType = null;
		string? contentHashPropName = null, contentHashPropType = null, contentHashFieldName = null;
		string? timestampPropName = null, timestampPropType = null;
		string? batchIndexDatePropName = null, batchIndexDateFieldName = null;
		string? lastUpdatedPropName = null, lastUpdatedFieldName = null;

		// Walk the full inheritance chain so attributes declared on a base class
		// (e.g. [BatchIndexDate] on a property in a parent type) are visible to derived types.
		// GetMembers() returns only directly declared members, so we must traverse BaseType
		// ourselves. Most-derived wins: once a field is found we stop searching for it.
		var type = targetType;
		while (type != null && type.SpecialType == SpecialType.None)
		{
			foreach (var member in type.GetMembers())
			{
				if (member is not IPropertySymbol prop)
					continue;

				foreach (var propAttr in prop.GetAttributes())
				{
					var attrName = propAttr.AttributeClass?.ToDisplayString();
					if (attrName == IdAttributeName && idPropName == null)
					{
						idPropName = prop.Name;
						idPropType = prop.Type.ToDisplayString();
					}
					else if (attrName == ContentHashAttributeName && contentHashPropName == null)
					{
						contentHashPropName = prop.Name;
						contentHashPropType = prop.Type.ToDisplayString();
						contentHashFieldName = ResolveJsonFieldName(prop, stjConfig);
					}
					else if (attrName == TimestampAttributeName && timestampPropName == null)
					{
						timestampPropName = prop.Name;
						timestampPropType = prop.Type.ToDisplayString();
					}
					else if (attrName == BatchIndexDateAttributeName && batchIndexDatePropName == null)
					{
						batchIndexDatePropName = prop.Name;
						batchIndexDateFieldName = ResolveJsonFieldName(prop, stjConfig);
					}
					else if (attrName == LastUpdatedAttributeName && lastUpdatedPropName == null)
					{
						lastUpdatedPropName = prop.Name;
						lastUpdatedFieldName = ResolveJsonFieldName(prop, stjConfig);
					}
				}
			}

			type = type.BaseType;
		}

		return new IngestPropertyModel(
			idPropName, idPropType,
			contentHashPropName, contentHashPropType, contentHashFieldName,
			timestampPropName, timestampPropType,
			batchIndexDatePropName, batchIndexDateFieldName,
			lastUpdatedPropName, lastUpdatedFieldName
		);
	}

	private static string ResolveJsonFieldName(IPropertySymbol prop, StjContextConfig? stjConfig) =>
		prop.GetAttributes()
			.Where(a => a.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeName)
			.Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
			.FirstOrDefault()
		?? Analysis.TypeAnalyzer.ApplyNamingPolicy(
			prop.Name,
			stjConfig?.PropertyNamingPolicy ?? Model.NamingPolicy.Unspecified);

	/// <summary>
	/// Checks whether the given type implements IConfigureElasticsearch&lt;TDocument&gt;
	/// for the specified document type.
	/// </summary>
	private static bool ImplementsConfigureElasticsearch(INamedTypeSymbol typeSymbol, INamedTypeSymbol documentType)
	{
		var expectedInterface = $"{ConfigureElasticsearchInterfacePrefix}{documentType.ToDisplayString()}>";
		return typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == expectedInterface);
	}

	private static TypeRegistration? BuildTypeRegistration(
		INamedTypeSymbol targetType,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		IndexConfigModel? indexConfig,
		DataStreamConfigModel? dataStreamConfig,
		EntityConfigModel entityConfig,
		IngestPropertyModel ingestProperties,
		string? configClassName,
		INamedTypeSymbol? configTypeSymbol,
		string? variant,
		Compilation compilation,
		CancellationToken ct)
	{
		var typeModel = TypeAnalyzer.Analyze(targetType, stjConfig, indexConfig, dataStreamConfig, ct);
		if (typeModel == null)
			return null;

		// Resolve the IConfigureElasticsearch<T> implementation.
		// Priority:
		//   1. Context-level methods (Configure{ResolverName}Analysis / Configure{ResolverName}Mappings)
		//   2. Configuration class (from attribute) implementing IConfigureElasticsearch<T>
		//   3. Entity type itself implementing IConfigureElasticsearch<T>

		var resolverName = string.IsNullOrEmpty(variant) ? targetType.Name : $"{targetType.Name}{variant}";

		// --- Priority 1: Context-level methods ---
		var contextConfigureAnalysis = contextSymbol
			.GetMembers($"Configure{resolverName}Analysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		if (contextConfigureAnalysis == null && !string.IsNullOrEmpty(variant))
		{
			contextConfigureAnalysis = contextSymbol
				.GetMembers($"Configure{targetType.Name}Analysis")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
		}

		var contextConfigureMappings = contextSymbol
			.GetMembers($"Configure{resolverName}Mappings")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		if (contextConfigureMappings == null && !string.IsNullOrEmpty(variant))
		{
			contextConfigureMappings = contextSymbol
				.GetMembers($"Configure{targetType.Name}Mappings")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
		}

		// --- Priority 2: Configuration class implementing IConfigureElasticsearch<T> ---
		var configClassImplementsInterface = false;
		if (configTypeSymbol != null)
			configClassImplementsInterface = ImplementsConfigureElasticsearch(configTypeSymbol, targetType);

		// Legacy fallback: static methods on the config class (for backward compat)
		IMethodSymbol? configClassAnalysis = null;
		IMethodSymbol? configClassMappings = null;
		if (configTypeSymbol != null && !configClassImplementsInterface)
		{
			configClassAnalysis = configTypeSymbol.GetMembers("ConfigureAnalysis")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

			configClassMappings = configTypeSymbol.GetMembers("ConfigureMappings")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
		}

		// --- Priority 3: Entity type itself implementing IConfigureElasticsearch<T> ---
		var entityImplementsInterface = ImplementsConfigureElasticsearch(targetType, targetType);

		// --- Detect Configure* methods on the type itself (legacy fallback) ---
		var hasConfigureAnalysisOnType = typeModel.HasConfigureAnalysis;
		var hasConfigureMappingsOnType = typeModel.HasConfigureMappings;

		// --- Build ConfigureAnalysis reference ---
		// Priority: context > config class (interface) > config class (legacy static) > entity (interface) > entity (legacy static)
		string? configureAnalysisRef = null;
		AnalysisComponentsModel analysisComponents;
		// When analysis is discovered via delegation (the ConfigureAnalysis method calls a shared
		// factory), we record the anchor so emitters can produce a base-type-anchored accessor.
		bool analysisViaDelegate = false;

		if (contextConfigureAnalysis != null)
		{
			configureAnalysisRef = $"global::{contextSymbol.ToDisplayString()}.{contextConfigureAnalysis.Name}";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(contextConfigureAnalysis, compilation, ct);
		}
		else if (configClassImplementsInterface)
		{
			configureAnalysisRef = $"_configureElasticsearch_ConfigureAnalysis";
			analysisComponents = configTypeSymbol != null
				? ParseAnalysisFromConfigureMethod(configTypeSymbol, compilation, ct, out analysisViaDelegate)
				: AnalysisComponentsModel.Empty;
		}
		else if (configClassAnalysis != null)
		{
			configureAnalysisRef = $"global::{configTypeSymbol!.ToDisplayString()}.ConfigureAnalysis";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(configClassAnalysis, compilation, ct);
		}
		else if (entityImplementsInterface)
		{
			configureAnalysisRef = $"_configureElasticsearch_ConfigureAnalysis";
			analysisComponents = ParseAnalysisFromConfigureMethod(targetType, compilation, ct, out analysisViaDelegate);
		}
		else if (hasConfigureAnalysisOnType)
		{
			configureAnalysisRef = $"global::{targetType.ToDisplayString()}.ConfigureAnalysis";
			analysisComponents = typeModel.AnalysisComponents;
		}
		else
		{
			analysisComponents = AnalysisComponentsModel.Empty;
		}

		// --- Determine analysis anchor ---
		// When analysis was found via delegation to a shared factory, anchor the generated keys
		// to the nearest user-defined base type so generic code can reference them without
		// knowing the concrete document type. This mirrors the #185 base-type-extension pattern.
		AnalysisAnchorModel? analysisAnchor = null;
		if (analysisViaDelegate && analysisComponents.HasAnyComponents)
		{
			var baseType = targetType.BaseType;
			while (baseType != null && baseType.SpecialType != SpecialType.None)
				baseType = baseType.BaseType;

			if (baseType != null && baseType.SpecialType == SpecialType.None)
			{
				analysisAnchor = new AnalysisAnchorModel(
					baseType.Name,
					baseType.ContainingNamespace?.ToDisplayString() ?? string.Empty,
					baseType.ToDisplayString()
				);
			}
		}

		// --- Build ConfigureMappings info ---
		var hasConfigureMappings =
			contextConfigureMappings != null ||
			configClassImplementsInterface ||
			configClassMappings != null ||
			entityImplementsInterface ||
			hasConfigureMappingsOnType;

		// Context-level static method reference (only set for context-level methods)
		var contextConfigureMappingsRef = contextConfigureMappings != null
			? $"global::{contextSymbol.ToDisplayString()}.{contextConfigureMappings.Name}"
			: null;

		// --- Detect IndexSettings ---
		// Priority: context > configuration class (interface) > configuration class (legacy) > entity
		string? indexSettingsRef = null;

		var contextIndexSettings = contextSymbol
			.GetMembers($"{resolverName}IndexSettings")
			.OfType<IPropertySymbol>()
			.FirstOrDefault(p => p.IsStatic);

		if (contextIndexSettings == null && !string.IsNullOrEmpty(variant))
		{
			contextIndexSettings = contextSymbol
				.GetMembers($"{targetType.Name}IndexSettings")
				.OfType<IPropertySymbol>()
				.FirstOrDefault(p => p.IsStatic);
		}

		if (contextIndexSettings != null)
		{
			indexSettingsRef = $"global::{contextSymbol.ToDisplayString()}.{contextIndexSettings.Name}";
		}
		else if (configClassImplementsInterface || entityImplementsInterface)
		{
			indexSettingsRef = "_configureElasticsearch_IndexSettings";
		}
		else if (configTypeSymbol != null)
		{
			var configIndexSettings = configTypeSymbol.GetMembers("IndexSettings")
				.OfType<IPropertySymbol>()
				.FirstOrDefault(p => p.IsStatic);
			if (configIndexSettings != null)
				indexSettingsRef = $"global::{configTypeSymbol.ToDisplayString()}.IndexSettings";
		}

		if (indexSettingsRef == null)
		{
			var typeIndexSettings = targetType.GetMembers("IndexSettings")
				.OfType<IPropertySymbol>()
				.FirstOrDefault(p => p.IsStatic);
			if (typeIndexSettings != null)
				indexSettingsRef = $"global::{targetType.ToDisplayString()}.IndexSettings";
		}

		// Determine the actual configuration class name for the interface path.
		// When the entity itself implements the interface and no explicit Configuration is set,
		// use the entity type as the configuration class.
		var resolvedConfigClassName = configClassName;
		if (resolvedConfigClassName == null && entityImplementsInterface)
			resolvedConfigClassName = targetType.ToDisplayString();

		// --- Detect AddField/AddProperty misuse in ConfigureMappings ---
		var misuseFindings = DetectMappingMisuse(
			typeModel, targetType, contextConfigureMappings, configTypeSymbol,
			configClassImplementsInterface, configClassMappings, entityImplementsInterface, ct);

		return new TypeRegistration(
			targetType.Name,
			targetType.ToDisplayString(),
			typeModel,
			indexConfig,
			dataStreamConfig,
			entityConfig,
			ingestProperties,
			resolvedConfigClassName,
			configureAnalysisRef,
			hasConfigureMappings,
			contextConfigureMappingsRef,
			analysisComponents,
			variant,
			indexSettingsRef,
			misuseFindings,
			analysisAnchor
		);
	}

	/// <summary>
	/// Builds a field-name → field-type map from the top-level properties of the type model
	/// and runs the misuse parser against the applicable ConfigureMappings method(s).
	/// </summary>
	private static ImmutableArray<MappingMisuseFinding> DetectMappingMisuse(
		TypeMappingModel typeModel,
		INamedTypeSymbol targetType,
		IMethodSymbol? contextConfigureMappings,
		INamedTypeSymbol? configTypeSymbol,
		bool configClassImplementsInterface,
		IMethodSymbol? configClassMappings,
		bool entityImplementsInterface,
		CancellationToken ct)
	{
		// Build the field-name → field-type map from known model properties.
		var fieldNameToType = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var prop in typeModel.Properties)
		{
			if (!prop.IsIgnored)
				fieldNameToType[prop.FieldName] = prop.FieldType;
		}

		if (fieldNameToType.Count == 0)
			return ImmutableArray<MappingMisuseFinding>.Empty;

		var allFindings = ImmutableArray.CreateBuilder<MappingMisuseFinding>();

		// Context-level static method (Priority 1)
		if (contextConfigureMappings != null)
		{
			allFindings.AddRange(AddPropertyFieldMisuseParser.Parse(contextConfigureMappings, fieldNameToType, ct));
			return allFindings.ToImmutable();
		}

		// Configuration class via interface (Priority 2a)
		if (configClassImplementsInterface && configTypeSymbol != null)
		{
			var method = configTypeSymbol.GetMembers("ConfigureMappings")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.Parameters.Length == 1);
			if (method != null)
				allFindings.AddRange(AddPropertyFieldMisuseParser.Parse(method, fieldNameToType, ct));
			return allFindings.ToImmutable();
		}

		// Configuration class legacy static method (Priority 2b)
		if (configClassMappings != null)
		{
			allFindings.AddRange(AddPropertyFieldMisuseParser.Parse(configClassMappings, fieldNameToType, ct));
			return allFindings.ToImmutable();
		}

		// Entity type via interface (Priority 3)
		if (entityImplementsInterface)
		{
			var method = targetType.GetMembers("ConfigureMappings")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.Parameters.Length == 1);
			if (method != null)
				allFindings.AddRange(AddPropertyFieldMisuseParser.Parse(method, fieldNameToType, ct));
		}

		return allFindings.ToImmutable();
	}

	private static AnalysisComponentsModel ParseAnalysisFromConfigureMethod(
		INamedTypeSymbol typeSymbol,
		Compilation compilation,
		CancellationToken ct,
		out bool viaDelegate)
	{
		viaDelegate = false;
		var method = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.Parameters.Length == 1);

		if (method == null)
			return AnalysisComponentsModel.Empty;

		// Detect delegation: the method delegates when it has at least one invocation that
		// resolves to a user-authored method with source (i.e. not just returning the parameter).
		viaDelegate = MethodDelegatesAnalysis(method, compilation, ct);

		return ConfigureAnalysisParser.ParseFromMethod(method, compilation, ct);
	}

	/// <summary>
	/// Returns true when the method contains an invocation that resolves to a user-authored
	/// method with source code (i.e. it delegates rather than authoring analysis inline).
	/// A pass-through (<c>=> analysis</c>) or a purely-inline fluent chain has no such calls.
	/// </summary>
	private static bool MethodDelegatesAnalysis(IMethodSymbol method, Compilation compilation, CancellationToken ct)
	{
		const string analysisBuilderTypeName = "Elastic.Mapping.Analysis.AnalysisBuilder";

		foreach (var syntaxRef in method.DeclaringSyntaxReferences)
		{
			ct.ThrowIfCancellationRequested();
			var methodSyntax = syntaxRef.GetSyntax(ct);
			var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);

			foreach (var invocation in methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				var invokedSymbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
				if (invokedSymbolInfo.Symbol is not IMethodSymbol invokedMethod)
					continue;

				// Skip AnalysisBuilder fluent methods (they are registration calls, not delegation)
				if (invokedMethod.ContainingType?.ToDisplayString() == analysisBuilderTypeName)
					continue;

				// Any user method with source = delegation
				if (invokedMethod.DeclaringSyntaxReferences.Length > 0)
					return true;
			}
		}

		return false;
	}

	private static readonly DiagnosticDescriptor DuplicateAiEnrichmentDiagnostic = new(
		"ELASTIC001",
		"Duplicate AI enrichment for entity",
		"Only one [AiEnrichment<{0}>] is allowed across all mapping contexts. Remove the duplicate from '{1}'.",
		"Elastic.Mapping",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static void ExecuteAll(SourceProductionContext context, ImmutableArray<ContextMappingModel> models)
	{
		// Track emitted extension classes globally across all contexts to avoid
		// duplicate definitions when the same document type is registered in
		// multiple contexts within the same namespace.
		var emittedExtensionKeys = new HashSet<string>();

		// Track emitted nested builder classes globally to avoid duplicate
		// definitions when multiple document types in the same namespace
		// reference the same nested type (e.g. IndexedProduct).
		var emittedNestedBuilders = new HashSet<string>();

		// A nested/object-typed property can be analyzed differently by different contexts
		// (e.g. per-context DefaultIgnoreCondition), even when it's the same CLR type reached
		// via inheritance or via two unrelated document types. Merge every observation of a
		// given nested type name into one canonical shape up front, so whichever registration
		// "wins" the emittedNestedBuilders race always emits the full, complete member surface.
		var mergedNestedTypes = new Dictionary<string, NestedTypeModel>();
		foreach (var model in models)
		{
			foreach (var reg in model.TypeRegistrations)
			{
				foreach (var prop in reg.TypeModel.Properties)
				{
					if (prop.IsIgnored || prop.NestedType == null)
						continue;

					mergedNestedTypes[prop.NestedType.TypeName] = mergedNestedTypes.TryGetValue(prop.NestedType.TypeName, out var existing)
						? existing.MergeWith(prop.NestedType)
						: prop.NestedType;
				}
			}
		}

		// Track emitted generic-constrained base-type extension classes globally.
		// Keyed by "{declaringNamespace}::{declaringTypeFullyQualifiedName}" so each
		// base type's methods are emitted exactly once regardless of how many concrete
		// derived types are registered.
		var emittedBaseExtensionKeys = new HashSet<string>();

		// Accumulate analysis components per anchor across ALL registrations before emitting.
		// Multiple registrations sharing the same anchor may each contribute different component
		// names (e.g. one delegates to BuildBaseAnalysis, another to BuildExtendedAnalysis which
		// adds synonym analyzers). We merge so the single emitted accessor covers the union.
		// Keyed by "{anchorNamespace}::{anchorFullyQualifiedName}".
		var anchorComponents = new Dictionary<string, (AnalysisAnchorModel Anchor, AnalysisComponentsModel Components)>();
		foreach (var model in models)
		{
			foreach (var reg in model.TypeRegistrations)
			{
				if (reg.AnalysisAnchor == null || !reg.AnalysisComponents.HasAnyComponents)
					continue;

				var anchorKey = $"{reg.AnalysisAnchor.Namespace}::{reg.AnalysisAnchor.FullyQualifiedName}";
				if (anchorComponents.TryGetValue(anchorKey, out var existing))
					anchorComponents[anchorKey] = (existing.Anchor, existing.Components.Merge(reg.AnalysisComponents));
				else
					anchorComponents[anchorKey] = (reg.AnalysisAnchor, reg.AnalysisComponents);
			}
		}

		// Emit the merged anchor-anchored accessor and extensions once per anchor type.
		foreach (var kvp in anchorComponents)
		{
			var anchor = kvp.Value.Anchor;
			var mergedComponents = kvp.Value.Components;

			var anchoredAnalysisSource = AnalysisNamesEmitter.EmitBaseAnchored(
				anchor.Namespace, anchor.Name, mergedComponents);
			context.AddSource($"{anchor.Name}Analysis.g.cs", anchoredAnalysisSource);

			var anchoredExtSource = MappingsBuilderEmitter.EmitBaseAnchoredAnalysisExtensions(
				anchor.Namespace, anchor.Name, anchor.FullyQualifiedName, mergedComponents);
			context.AddSource($"{anchor.Name}AnalysisExtensions.g.cs", anchoredExtSource);
		}

		// Enforce: only one [AiEnrichment<T>] per document type across all contexts
		var aiEnrichmentTypes = new Dictionary<string, string>();
		foreach (var model in models)
		{
			if (model.AiEnrichment != null)
			{
				var docFqn = model.AiEnrichment.DocumentTypeFullyQualifiedName;
				if (aiEnrichmentTypes.ContainsKey(docFqn))
				{
					context.ReportDiagnostic(Diagnostic.Create(
						DuplicateAiEnrichmentDiagnostic,
						Location.None,
						model.AiEnrichment.DocumentTypeName,
						model.ContextTypeName));
				}
				else
				{
					aiEnrichmentTypes[docFqn] = model.ContextTypeName;
				}
			}
		}

		// Report EMAP001 / EMAP002: AddField/AddProperty misuse in ConfigureMappings
		foreach (var model in models)
		{
			foreach (var reg in model.TypeRegistrations)
			{
				foreach (var finding in reg.MisuseFindings)
				{
					var descriptor = finding.DiagnosticId == "EMAP001"
						? MappingDiagnostics.AddFieldOnObjectParent
						: MappingDiagnostics.AddPropertyOnLeafParent;

					// Reconstruct the source location from the serialised span so the IDE
					// can point at the string-literal argument in the ConfigureMappings method.
					var location = !string.IsNullOrEmpty(finding.FilePath)
						? Location.Create(
							finding.FilePath,
							new TextSpan(finding.SpanStart, finding.SpanLength),
							new LinePositionSpan())
						: Location.None;

					context.ReportDiagnostic(Diagnostic.Create(
						descriptor,
						location,
						finding.ParentFieldName,
						finding.FullPath,
						finding.ChildSegment,
						finding.ParentFieldType));
				}
			}
		}

		foreach (var model in models)
		{
			var contextSource = ContextEmitter.Emit(model);
			context.AddSource($"{model.ContextTypeName}.g.cs", contextSource);

			foreach (var reg in model.TypeRegistrations)
			{
				// Key by namespace + type FQN to deduplicate across contexts in the same namespace
				var extensionKey = $"{model.Namespace}.{reg.TypeFullyQualifiedName}";
				if (emittedExtensionKeys.Add(extensionKey))
				{
					var mappingsExtensionsSource = MappingsBuilderEmitter.EmitForContext(model, reg, emittedNestedBuilders, mergedNestedTypes);
					context.AddSource($"{model.ContextTypeName}.{reg.TypeName}MappingsExtensions.g.cs", mappingsExtensionsSource);
				}

				// Emit generic-constrained extension methods for base-type properties.
				// Group by declaring type FQN so each base type's class is emitted exactly once globally.
				var baseGroups = reg.TypeModel.Properties
					.Where(p => p.DeclaringTypeName != null && !p.IsIgnored)
					.GroupBy(p => p.DeclaringTypeFullyQualifiedName!);
				foreach (var group in baseGroups)
				{
					var declaringFqn = group.Key;
					var declaringName = group.First().DeclaringTypeName!;
					var declaringNs = group.First().DeclaringTypeNamespace ?? string.Empty;
					var baseKey = $"{declaringNs}::{declaringFqn}";
					if (emittedBaseExtensionKeys.Add(baseKey))
					{
						var baseExtSource = MappingsBuilderEmitter.EmitBaseExtensionsClass(
							declaringNs, declaringName, declaringFqn, group.ToList(), emittedNestedBuilders, mergedNestedTypes);
						context.AddSource($"{declaringName}MappingsExtensions.g.cs", baseExtSource);
					}
				}

				// Analysis accessor emission:
				// - When analysis is anchored to a base type (delegation path), the shared accessor
				//   and generic-constrained extensions are already emitted in the pre-pass above.
				//   Skip per-context emission so there's no competing {ResolverName}Analysis class.
				// - When analysis is inline (no anchor), emit the per-context accessor as before.
				if (reg.AnalysisAnchor == null)
				{
					var analysisNamesSource = AnalysisNamesEmitter.EmitForContext(model, reg);
					if (analysisNamesSource != null)
						context.AddSource($"{model.ContextTypeName}.{reg.ResolverName}Analysis.g.cs", analysisNamesSource);
				}
			}

			if (model.AiEnrichment != null)
			{
				var aiSource = AiEnrichmentEmitter.Emit(model, model.AiEnrichment);
				context.AddSource($"{model.ContextTypeName}.AiEnrichment.g.cs", aiSource);
			}
		}
	}

	private static T? GetNamedArg<T>(AttributeData attr, string name, T? defaultValue = default)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key == null)
			return defaultValue;

		return arg.Value.Value is T value ? value : defaultValue;
	}

	private static string GetNamedEnumArg(AttributeData attr, string name, string defaultValue)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key == null)
			return defaultValue;

		if (arg.Value.Value is int intValue)
		{
			var enumType = arg.Value.Type;
			if (enumType != null)
			{
				foreach (var member in enumType.GetMembers())
				{
					if (member is IFieldSymbol field && field.HasConstantValue && field.ConstantValue is int fieldVal && fieldVal == intValue)
						return field.Name;
				}
			}
			return intValue.ToString();
		}

		return defaultValue;
	}
}
