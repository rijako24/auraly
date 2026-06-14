namespace MimosBabySpa.Domain.Entities;

public class EmployeeWorkingHour
{
    public Guid EmployeeWorkingHourId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid EmployeeId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Employee Employee { get; set; } = null!;
}
