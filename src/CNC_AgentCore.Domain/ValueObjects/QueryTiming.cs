// Domain/ValueObjects/QueryTiming.cs —— 8 阶段耗时
namespace CNC_AgentCore.Domain.ValueObjects;

public sealed class QueryTiming
{
    public int Embed { get; set; }
    public int CodeExtract { get; set; }
    public int ExactMatch { get; set; }
    public int VectorRecall { get; set; }
    public int FulltextRecall { get; set; }
    public int RrfFusion { get; set; }
    public int Rerank { get; set; }
    public int ThresholdGate { get; set; }
    public int Total { get; set; }

    public Dictionary<string, int> AsDict() => new()
    {
        ["embed"] = Embed,
        ["code_extract"] = CodeExtract,
        ["exact_match"] = ExactMatch,
        ["vector_recall"] = VectorRecall,
        ["fulltext_recall"] = FulltextRecall,
        ["rrf_fusion"] = RrfFusion,
        ["rerank"] = Rerank,
        ["threshold_gate"] = ThresholdGate,
        ["total"] = Total,
    };
}
