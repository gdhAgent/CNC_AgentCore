// Domain/Entities/Chunk.cs —— kb.chunks（父子分块：level=1 父块；level=2 子块）
namespace CNC_AgentCore.Domain.Entities;

public sealed class Chunk
{
    public long Id { get; set; }
    public long DocId { get; set; }
    public int Level { get; set; }                       // 1=父块 2=子块
    public long? ParentId { get; set; }
    public string? HeadingPath { get; set; }
    public string Content { get; set; } = string.Empty;
    public int? PageFrom { get; set; }
    public int? PageTo { get; set; }
    public float[]? Embedding { get; set; }             // pgvector(1024)
    public string? Tsv { get; set; }                    // tsvector
}
