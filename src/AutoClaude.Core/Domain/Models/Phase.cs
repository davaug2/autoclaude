using AutoClaude.Core.Domain.Enums;

namespace AutoClaude.Core.Domain.Models;

public class Phase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkModelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public PhaseType PhaseType { get; set; }
    public int Ordinal { get; set; }
    public string? Description { get; set; }
    public string? PromptTemplate { get; set; }
    public string? SystemPrompt { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Once;
}
