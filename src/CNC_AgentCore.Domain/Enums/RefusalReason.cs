// Domain/Enums/RefusalReason.cs —— 拒答原因
namespace CNC_AgentCore.Domain.Enums;

public static class RefusalReason
{
    public const string NoGrounding = "no_grounding";
    public const string InsufficientMaterial = "insufficient_material";
    public const string NoContent = "no_content";
    public const string NoCandidates = "no_candidates";
    public const string EmptyQuery = "empty_query";
}
