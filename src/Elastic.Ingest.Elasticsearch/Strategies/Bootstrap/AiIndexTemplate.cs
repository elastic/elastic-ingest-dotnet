// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// The parts of an index template body that change when the type is an AI index
/// (<see cref="BootstrapContext.AiIndex"/>).
/// <para>
/// With <c>AiIndex</c> off, every member returns the value the template body had before this
/// existed, so the generated template and its hash do not change.
/// </para>
/// </summary>
internal static class AiIndexTemplate
{
	private const int DefaultPriority = 201;

	/// <summary>
	/// Wraps the generated component templates in the AI index ones. Later entries win, so
	/// <c>ai-index@mappings</c> comes first (the generated mapping can refine it) and
	/// <c>ai-index@custom</c> last (customers can refine everything), as in Elasticsearch's own templates.
	/// </summary>
	public static string ComposedOf(BootstrapContext context, string generatedComponents) =>
		context.AiIndex
			? $"\"{AiIndexMapping.MappingsComponentTemplate}\", {generatedComponents}, \"{AiIndexMapping.CustomComponentTemplate}\""
			: generatedComponents;

	/// <summary>
	/// Both AI index component templates may be absent: <c>ai-index@custom</c> is optional by design,
	/// and <c>ai-index@mappings</c> only exists from Elasticsearch 9.6. The generated mapping already
	/// contains the base fields, so a missing component template loses nothing.
	/// </summary>
	public static string IgnoreMissingFragment(BootstrapContext context) =>
		context.AiIndex
			? $@"
                ""ignore_missing_component_templates"": [ ""{AiIndexMapping.MappingsComponentTemplate}"", ""{AiIndexMapping.CustomComponentTemplate}"" ],"
			: string.Empty;

	public static int Priority(BootstrapContext context) =>
		context.AiIndex ? AiIndexMapping.TemplatePriority : DefaultPriority;
}
