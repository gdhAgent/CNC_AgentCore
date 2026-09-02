// Domain/Entities/TermDictEntry.cs —— kb.term_dict
namespace CNC_AgentCore.Domain.Entities;

public sealed class TermDictEntry
{
    public long Id { get; set; }
    public string Canonical { get; set; } = string.Empty;
    public string[] Synonyms { get; set; } = Array.Empty<string>();
    public string? Domain { get; set; }
    public string? Lang { get; set; }
}
