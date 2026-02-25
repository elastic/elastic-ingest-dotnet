// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using Elastic.Mapping.Generator.Analysis;
using Elastic.Mapping.Generator.Emitters;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

		var registrations = ImmutableArray.CreateBuilder<TypeRegistration>();

		foreach (var attr in contextSymbol.GetAttributes())
		{
			ct.ThrowIfCancellationRequested();

			var attrClassName = attr.AttributeClass?.ToDisplayString();
			if (attrClassName == null)
				continue;

			TypeRegistration? registration = null;
			if (attrClassName.StartsWith(IndexAttributePrefix, StringComparison.Ordinal))
				registration = ProcessIndexAttribute(attr, contextSymbol, stjConfig, ct);
			else if (attrClassName.StartsWith(DataStreamAttributePrefix, StringComparison.Ordinal))
				registration = ProcessDataStreamAttribute(attr, contextSymbol, stjConfig, "DataStream", ct);
			else if (attrClassName.StartsWith(WiredStreamAttributePrefix, StringComparison.Ordinal))
				registration = ProcessDataStreamAttribute(attr, contextSymbol, stjConfig, "WiredStream", ct);

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

			var role = GetNamedArg<string>(attr, "Role");
			var lookupIndexName = GetNamedArg<string>(attr, "LookupIndexName");
			var matchField = GetNamedArg<string>(attr, "MatchField");

			aiEnrichment = AiEnrichmentAnalyzer.Analyze(
				documentType, role, lookupIndexName, matchField, stjConfig, ct);

			break; // Only one AI enrichment per context
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
			GetNamedArg<bool>(attr, "Dynamic", true)
		);

		var ingestProperties = AnalyzeIngestProperties(targetType, stjConfig);
		ExtractConfigAndVariant(attr, out var configClassName, out var configTypeSymbol, out var variant);

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, indexConfig, null, entityConfig, ingestProperties, configClassName, configTypeSymbol, variant, ct);
	}

	private static TypeRegistration? ProcessDataStreamAttribute(
		AttributeData attr,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		string entityTarget,
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
				GetNamedArg<string>(attr, "Namespace")
			);
		}

		var ingestProperties = AnalyzeIngestProperties(targetType, stjConfig);
		ExtractConfigAndVariant(attr, out var configClassName, out var configTypeSymbol, out var variant);

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, null, dataStreamConfig, entityConfig, ingestProperties, configClassName, configTypeSymbol, variant, ct);
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

		foreach (var member in targetType.GetMembers())
		{
			if (member is not IPropertySymbol prop)
				continue;

			foreach (var propAttr in prop.GetAttributes())
			{
				var attrName = propAttr.AttributeClass?.ToDisplayString();
				if (attrName == IdAttributeName)
				{
					idPropName = prop.Name;
					idPropType = prop.Type.ToDisplayString();
				}
				else if (attrName == ContentHashAttributeName)
				{
					contentHashPropName = prop.Name;
					contentHashPropType = prop.Type.ToDisplayString();

					contentHashFieldName = ResolveJsonFieldName(prop, stjConfig);
				}
				else if (attrName == TimestampAttributeName)
				{
					timestampPropName = prop.Name;
					timestampPropType = prop.Type.ToDisplayString();
				}
				else if (attrName == BatchIndexDateAttributeName)
				{
					batchIndexDatePropName = prop.Name;
					batchIndexDateFieldName = ResolveJsonFieldName(prop, stjConfig);
				}
				else if (attrName == LastUpdatedAttributeName)
				{
					lastUpdatedPropName = prop.Name;
					lastUpdatedFieldName = ResolveJsonFieldName(prop, stjConfig);
				}
			}
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
		bool configClassImplementsInterface = false;
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

		if (contextConfigureAnalysis != null)
		{
			configureAnalysisRef = $"global::{contextSymbol.ToDisplayString()}.{contextConfigureAnalysis.Name}";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(contextConfigureAnalysis, ct);
		}
		else if (configClassImplementsInterface)
		{
			configureAnalysisRef = $"_configureElasticsearch_ConfigureAnalysis";
			analysisComponents = configTypeSymbol != null
				? ParseAnalysisFromConfigureMethod(configTypeSymbol, ct)
				: AnalysisComponentsModel.Empty;
		}
		else if (configClassAnalysis != null)
		{
			configureAnalysisRef = $"global::{configTypeSymbol!.ToDisplayString()}.ConfigureAnalysis";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(configClassAnalysis, ct);
		}
		else if (entityImplementsInterface)
		{
			configureAnalysisRef = $"_configureElasticsearch_ConfigureAnalysis";
			analysisComponents = ParseAnalysisFromConfigureMethod(targetType, ct);
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

		// --- Build ConfigureMappings info ---
		bool hasConfigureMappings =
			contextConfigureMappings != null ||
			configClassImplementsInterface ||
			configClassMappings != null ||
			entityImplementsInterface ||
			hasConfigureMappingsOnType;

		// Context-level static method reference (only set for context-level methods)
		string? contextConfigureMappingsRef = contextConfigureMappings != null
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
			indexSettingsRef
		);
	}

	private static AnalysisComponentsModel ParseAnalysisFromConfigureMethod(INamedTypeSymbol typeSymbol, CancellationToken ct)
	{
		var method = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.Parameters.Length == 1);
		return method != null
			? ConfigureAnalysisParser.ParseFromMethod(method, ct)
			: AnalysisComponentsModel.Empty;
	}

	private static void ExecuteAll(SourceProductionContext context, ImmutableArray<ContextMappingModel> models)
	{
		// Track emitted extension classes globally across all contexts to avoid
		// duplicate definitions when the same document type is registered in
		// multiple contexts within the same namespace.
		var emittedExtensionKeys = new HashSet<string>();

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
					var mappingsExtensionsSource = MappingsBuilderEmitter.EmitForContext(model, reg);
					context.AddSource($"{model.ContextTypeName}.{reg.TypeName}MappingsExtensions.g.cs", mappingsExtensionsSource);
				}

				var analysisNamesSource = AnalysisNamesEmitter.EmitForContext(model, reg);
				if (analysisNamesSource != null)
					context.AddSource($"{model.ContextTypeName}.{reg.ResolverName}Analysis.g.cs", analysisNamesSource);
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
