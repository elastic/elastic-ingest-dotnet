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
	private const string EntityAttributePrefix = "Elastic.Mapping.EntityAttribute<";

	// Ingest property attribute names
	private const string IdAttributeName = "Elastic.Mapping.IdAttribute";
	private const string ContentHashAttributeName = "Elastic.Mapping.ContentHashAttribute";
	private const string TimestampAttributeName = "Elastic.Mapping.TimestampAttribute";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var contextDeclarations = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (node, _) => IsCandidateContextClass(node),
				transform: static (ctx, ct) => GetContextModel(ctx, ct)
			)
			.Where(static model => model != null)
			.Select(static (model, _) => model!);

		context.RegisterSourceOutput(contextDeclarations, static (ctx, model) => ExecuteContext(ctx, model));
	}

	private static bool IsCandidateContextClass(SyntaxNode node)
	{
		if (node is not TypeDeclarationSyntax typeDecl)
			return false;

		// Must be partial
		if (!typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
			return false;

		// Must have at least one attribute
		if (typeDecl.AttributeLists.Count == 0)
			return false;

		// Quick check for the mapping context attribute
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

		// Verify it has [ElasticsearchMappingContext]
		var contextAttr = contextSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ElasticsearchMappingContextAttributeName);

		if (contextAttr == null)
			return null;

		// Extract JsonContext type from the attribute
		INamedTypeSymbol? jsonContextSymbol = null;
		var jsonContextArg = contextAttr.NamedArguments
			.FirstOrDefault(a => a.Key == "JsonContext");
		if (jsonContextArg.Key != null && jsonContextArg.Value.Value is INamedTypeSymbol jcs)
			jsonContextSymbol = jcs;

		// Analyze STJ context if provided
		var stjConfig = StjContextAnalyzer.Analyze(jsonContextSymbol);

		// Collect type registrations from [Entity<T>] attributes
		var registrations = ImmutableArray.CreateBuilder<TypeRegistration>();

		foreach (var attr in contextSymbol.GetAttributes())
		{
			ct.ThrowIfCancellationRequested();

			var attrClassName = attr.AttributeClass?.ToDisplayString();
			if (attrClassName == null)
				continue;

			if (attrClassName.StartsWith(EntityAttributePrefix, StringComparison.Ordinal))
			{
				var registration = ProcessEntityAttribute(attr, contextSymbol, stjConfig, ct);
				if (registration != null)
					registrations.Add(registration);
			}
		}

		if (registrations.Count == 0)
			return null;

		return new ContextMappingModel(
			contextSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
			contextSymbol.Name,
			stjConfig,
			registrations.ToImmutable()
		);
	}

	private static TypeRegistration? ProcessEntityAttribute(
		AttributeData attr,
		INamedTypeSymbol contextSymbol,
		StjContextConfig? stjConfig,
		CancellationToken ct)
	{
		// Extract T from EntityAttribute<T>
		var typeArg = attr.AttributeClass?.TypeArguments.FirstOrDefault();
		if (typeArg is not INamedTypeSymbol targetType)
			return null;

		// Read EntityTarget enum value
		var targetValue = GetNamedEnumArg(attr, "Target", "Index");
		var dataStreamModeValue = GetNamedEnumArg(attr, "DataStreamMode", "Default");

		var entityConfig = new EntityConfigModel(targetValue, dataStreamModeValue);

		// Build IndexConfig or DataStreamConfig based on target
		IndexConfigModel? indexConfig = null;
		DataStreamConfigModel? dataStreamConfig = null;

		if (targetValue == "DataStream" || targetValue == "WiredStream")
		{
			var type = GetNamedArg<string>(attr, "Type");
			var dataset = GetNamedArg<string>(attr, "Dataset");

			if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(dataset))
			{
				dataStreamConfig = new DataStreamConfigModel(
					type!,
					dataset!,
					GetNamedArg<string>(attr, "Namespace")
				);
			}

			// DataStream targets can still have index-like settings
			indexConfig = new IndexConfigModel(
				GetNamedArg<string>(attr, "Name"),
				GetNamedArg<string>(attr, "WriteAlias"),
				GetNamedArg<string>(attr, "ReadAlias"),
				GetNamedArg<string>(attr, "DatePattern"),
				GetNamedArg<string>(attr, "SearchPattern"),
				GetNamedArg<int>(attr, "Shards", -1),
				GetNamedArg<int>(attr, "Replicas", -1),
				GetNamedArg<string>(attr, "RefreshInterval"),
				GetNamedArg<bool>(attr, "Dynamic", true)
			);
		}
		else
		{
			// Index target
			indexConfig = new IndexConfigModel(
				GetNamedArg<string>(attr, "Name"),
				GetNamedArg<string>(attr, "WriteAlias"),
				GetNamedArg<string>(attr, "ReadAlias"),
				GetNamedArg<string>(attr, "DatePattern"),
				GetNamedArg<string>(attr, "SearchPattern"),
				GetNamedArg<int>(attr, "Shards", -1),
				GetNamedArg<int>(attr, "Replicas", -1),
				GetNamedArg<string>(attr, "RefreshInterval"),
				GetNamedArg<bool>(attr, "Dynamic", true)
			);
		}

		// Detect [Id], [ContentHash], [Timestamp] on target type properties
		var ingestProperties = AnalyzeIngestProperties(targetType, stjConfig);

		// Extract Configuration class reference
		string? configClassName = null;
		INamedTypeSymbol? configTypeSymbol = null;
		var configArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Configuration");
		if (configArg.Key != null && configArg.Value.Value is INamedTypeSymbol configType)
		{
			configClassName = configType.ToDisplayString();
			configTypeSymbol = configType;
		}

		// Extract Variant suffix
		var variant = GetNamedArg<string>(attr, "Variant");

		return BuildTypeRegistration(targetType, contextSymbol, stjConfig, indexConfig, dataStreamConfig, entityConfig, ingestProperties, configClassName, configTypeSymbol, variant, ct);
	}

	private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";

	private static IngestPropertyModel AnalyzeIngestProperties(INamedTypeSymbol targetType, StjContextConfig? stjConfig = null)
	{
		string? idPropName = null, idPropType = null;
		string? contentHashPropName = null, contentHashPropType = null, contentHashFieldName = null;
		string? timestampPropName = null, timestampPropType = null;

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

					// Resolve the JSON field name from [JsonPropertyName] or naming policy
					contentHashFieldName = prop.GetAttributes()
						.Where(a => a.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeName)
						.Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
						.FirstOrDefault()
						?? Analysis.TypeAnalyzer.ApplyNamingPolicy(
							prop.Name,
							stjConfig?.PropertyNamingPolicy ?? Model.NamingPolicy.Unspecified);
				}
				else if (attrName == TimestampAttributeName)
				{
					timestampPropName = prop.Name;
					timestampPropType = prop.Type.ToDisplayString();
				}
			}
		}

		return new IngestPropertyModel(
			idPropName, idPropType,
			contentHashPropName, contentHashPropType, contentHashFieldName,
			timestampPropName, timestampPropType
		);
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

		// Check configuration class for methods (priority 2)
		IMethodSymbol? configClassAnalysis = null;
		IMethodSymbol? configClassMappings = null;
		if (configTypeSymbol != null)
		{
			configClassAnalysis = configTypeSymbol.GetMembers("ConfigureAnalysis")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

			configClassMappings = configTypeSymbol.GetMembers("ConfigureMappings")
				.OfType<IMethodSymbol>()
				.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
		}

		// Check for Configure{ResolverName}Analysis/Configure{ResolverName}Mappings on the context class (priority 1)
		// When a Variant is set, try the variant-suffixed name first, then fall back to TypeName
		var resolverName = string.IsNullOrEmpty(variant) ? targetType.Name : $"{targetType.Name}{variant}";

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

		// Check for ConfigureAnalysis/ConfigureMappings on the target type itself (priority 3 / fallback)
		var hasConfigureAnalysisOnType = typeModel.HasConfigureAnalysis;
		var hasConfigureMappingsOnType = typeModel.HasConfigureMappings;

		// Build ConfigureAnalysis reference and parse analysis components
		// Priority: context > configuration class > type
		string? configureAnalysisRef = null;
		AnalysisComponentsModel analysisComponents;

		if (contextConfigureAnalysis != null)
		{
			configureAnalysisRef = $"global::{contextSymbol.ToDisplayString()}.{contextConfigureAnalysis.Name}";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(contextConfigureAnalysis, ct);
		}
		else if (configClassAnalysis != null)
		{
			configureAnalysisRef = $"global::{configTypeSymbol!.ToDisplayString()}.ConfigureAnalysis";
			analysisComponents = ConfigureAnalysisParser.ParseFromMethod(configClassAnalysis, ct);
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

		// Determine if any source has ConfigureMappings
		var hasConfigureMappings = contextConfigureMappings != null
			|| configClassMappings != null
			|| hasConfigureMappingsOnType;

		return new TypeRegistration(
			targetType.Name,
			targetType.ToDisplayString(),
			typeModel,
			indexConfig,
			dataStreamConfig,
			entityConfig,
			ingestProperties,
			configClassName,
			configureAnalysisRef,
			hasConfigureMappings,
			analysisComponents,
			variant
		);
	}

	private static void ExecuteContext(SourceProductionContext context, ContextMappingModel model)
	{
		// Generate the context class with nested resolvers
		var contextSource = ContextEmitter.Emit(model);
		context.AddSource($"{model.ContextTypeName}.g.cs", contextSource);

		// Generate per-type MappingsBuilder classes
		foreach (var reg in model.TypeRegistrations)
		{
			var mappingsBuilderSource = MappingsBuilderEmitter.EmitForContext(model, reg);
			context.AddSource($"{model.ContextTypeName}.{reg.ResolverName}MappingsBuilder.g.cs", mappingsBuilderSource);

			// Generate analysis names if there are analysis components
			var analysisNamesSource = AnalysisNamesEmitter.EmitForContext(model, reg);
			if (analysisNamesSource != null)
				context.AddSource($"{model.ContextTypeName}.{reg.ResolverName}Analysis.g.cs", analysisNamesSource);
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

		// Enum values come as int, need to get the field name
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
