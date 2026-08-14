namespace Auraly.Platform.Domain.Entities;

public class BusinessAvailabilityBlock
{
    public Guid BusinessAvailabilityBlockId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = "operations";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Employee? Employee { get; set; }
}
