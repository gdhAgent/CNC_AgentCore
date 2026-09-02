// Domain/Enums/RouteKind.cs —— 路由标签
namespace CNC_AgentCore.Domain.Enums;

public static class RouteKind
{
    public const string Agent = "agent";
    public const string RagFallback = "rag_fallback";
    public const string Refused = "refused";

    public const string ExactCode = "exact_code";
    public const string Hybrid = "hybrid";
}
