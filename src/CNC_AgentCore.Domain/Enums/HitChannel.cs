// Domain/Enums/HitChannel.cs —— 检索命中通道
namespace CNC_AgentCore.Domain.Enums;

public static class HitChannel
{
    public const string Exact = "exact";
    public const string Vector = "vector";
    public const string Fulltext = "fulltext";
    public const string Rrf = "rrf";
    public const string Rerank = "rerank";
    public const string Suggest = "suggest";
}
