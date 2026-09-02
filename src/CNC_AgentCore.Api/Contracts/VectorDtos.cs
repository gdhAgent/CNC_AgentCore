// Api/Contracts/VectorDtos.cs —— 向量端点契约（#10/#11）。
// #10 OverviewResponseDto + OverviewTableDto
// #11 UnvectorizedItemDto + UnvectorizedResponseDto
using System.Text.Json.Serialization;

namespace CNC_AgentCore.Api.Contracts;

public sealed class OverviewTableDto
{
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("designed_skip")] public bool DesignedSkip { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("with_embedding")] public long WithEmbedding { get; set; }
    [JsonPropertyName("without")] public long Without { get; set; }
    [JsonPropertyName("dim_min")] public int? DimMin { get; set; }
    [JsonPropertyName("dim_max")] public int? DimMax { get; set; }
}

public sealed class OverviewResponseDto
{
    [JsonPropertyName("tables")] public List<OverviewTableDto> Tables { get; set; } = new();
}

/// <summary>无向量清单单条（#11）；全局 WhenWritingNull 已开启，缺失字段自然省略。</summary>
public sealed class UnvectorizedItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }       // alarms / maintenance_logs
    [JsonPropertyName("level")] public int? Level { get; set; }        // chunks only
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("detail")] public string? Detail { get; set; }
}

public sealed class UnvectorizedResponseDto
{
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("items")] public List<UnvectorizedItemDto> Items { get; set; } = new();
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

/// <summary>触发后台补跑响应（#12）。fire-and-forget，立即返回 started=true。</summary>
public sealed class VectorizeResponseDto
{
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("started")] public bool Started { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

/// <summary>embedding-map 单条（#13）：id + label + group + 服务端 PCA 2D 坐标。</summary>
public sealed class EmbeddingMapItemDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
}

public sealed class EmbeddingMapResponseDto
{
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("group_by")] public string GroupBy { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("explained_variance")] public double[] ExplainedVariance { get; set; } = new double[2];
    [JsonPropertyName("items")] public List<EmbeddingMapItemDto> Items { get; set; } = new();
}
