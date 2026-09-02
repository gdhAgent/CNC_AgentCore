// Domain/Entities/Machine.cs —— ops.machines
namespace CNC_AgentCore.Domain.Entities;

public sealed class Machine
{
    public long Id { get; set; }
    public string AssetNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Controller { get; set; }
    public string? Location { get; set; }
    public string? Status { get; set; }
    public bool IsDemo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
