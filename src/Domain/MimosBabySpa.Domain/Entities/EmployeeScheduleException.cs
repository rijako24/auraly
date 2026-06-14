namespace MimosBabySpa.Domain.Entities;

public class EmployeeScheduleException
{
    public Guid EmployeeScheduleExceptionId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public bool IsClosed { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Employee Employee { get; set; } = null!;
}
