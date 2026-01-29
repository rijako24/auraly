namespace MimosBabySpa.Application.StateManagement;

/// <summary>
/// Registro de cambio de estado (para auditoría)
/// </summary>
public class StateChangeRecord
{
    public Guid RecordId { get; set; }
    public Guid StateId { get; set; }
    public int Version { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangedBy { get; set; } // "System", "User", "LLM", etc.
}
