// Domain/Entities/MaintenanceLog.cs —— ops.maintenance_logs
namespace CNC_AgentCore.Domain.Entities;

public sealed class MaintenanceLog
{
    public long Id { get; set; }
    public long MachineId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string? AlarmCode { get; set; }
    public string? FaultType { get; set; }
    public string? Symptom { get; set; }
    public string? RootCause { get; set; }
    public string? ActionTaken { get; set; }
    public string? Engineer { get; set; }
    public int? DowntimeMin { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public bool IsDemo { get; set; }
}
