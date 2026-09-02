// Application/Retrieval/TraceRecorder.cs —— 全链路步骤采集。
namespace CNC_AgentCore.Application.Retrieval;

public sealed class TraceRecorder
{
    public static readonly IReadOnlyList<string> ValidSteps = new[]
    {
        "normalize", "code_extract", "exact_match", "vector_recall", "fulltext_recall",
        "rrf_fusion", "rerank", "threshold_gate", "tool_call", "llm_generate", "post_check",
    };

    public static readonly IReadOnlyList<string> ValidStatus = new[] { "ok", "skipped", "failed", "timeout" };

    public List<Dictionary<string, object?>> Steps { get; } = new();

    public Dictionary<string, object?> Add(string step, string status = "ok", int ms = 0,
        Dictionary<string, object?>? input = null, Dictionary<string, object?>? output = null, string? note = null)
    {
        if (!ValidSteps.Contains(step))
            throw new ArgumentException($"trace step 非法: '{step}'; 可选: [{string.Join(", ", ValidSteps)}]");
        if (!ValidStatus.Contains(status))
            throw new ArgumentException($"trace status 非法: '{status}'; 可选: [{string.Join(", ", ValidStatus)}]");

        var d = new Dictionary<string, object?>
        {
            ["step"] = step,
            ["status"] = status,
            ["ms"] = ms,
            ["input"] = input ?? new(),
            ["output"] = output ?? new(),
            ["note"] = note,
            ["started_at"] = DateTimeOffset.UtcNow.ToString("o"),
        };
        Steps.Add(d);
        return d;
    }

    public void Merge(IEnumerable<Dictionary<string, object?>>? other)
    {
        if (other is null) return;
        foreach (var d in other) Steps.Add(d);
    }
}
