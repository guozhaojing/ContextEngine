// =============================================================================
// PromptTemplates/PromptTemplate.cs — structured prompt template model
// =============================================================================

namespace Core.Prompting.Models;

public sealed class PromptTemplate
{
    public required string TemplateId { get; init; }
    public required string TemplateName { get; init; }
    public required string Description { get; init; }
    public required string SystemInstruction { get; init; }
    public required IReadOnlyList<TemplateSlot> Slots { get; init; }
    public required string OutputFormat { get; init; }
}

public sealed class TemplateSlot
{
    public required string SlotName { get; init; }
    public required string SlotTitle { get; init; }
    public required string Placeholder { get; init; }
    public bool Required { get; init; } = true;
    public string DefaultContent { get; init; } = "";
}
