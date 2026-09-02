// Domain/ValueObjects/QueryResult.cs —— 8 步编排结果
using CNC_AgentCore.Domain.Enums;

namespace CNC_AgentCore.Domain.ValueObjects;

public sealed class QueryResult
{
    public Guid TraceId { get; set; }
    public List<string> DetectedCodes { get; set; } = new();
    public string Route { get; set; } = RouteKind.Hybrid;
    public bool Refused { get; set; }
    public string? RefusedReason { get; set; }
    public List<Hit> Topk { get; set; } = new();
    public List<Hit> SuggestHits { get; set; } = new();
    public QueryTiming Timing { get; set; } = new();
    public List<Dictionary<string, object?>> RetrievedSnapshot { get; set; } = new();
    public List<Dictionary<string, object?>> TraceSteps { get; set; } = new();
}
