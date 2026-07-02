namespace MimosBabySpa.Domain.Entities;

public class BusinessSchedulingSettings
{
    public Guid BusinessSchedulingSettingsId { get; set; }
    public Guid BusinessId { get; set; }
    public int SlotIntervalMinutes { get; set; } = 60;
    public int BufferBetweenAppointmentsMinutes { get; set; }
    public int MinimumLeadTimeMinutes { get; set; }
    public bool RequireEmployee { get; set; } = true;
    public string EmployeeStrategy { get; set; } = "least_versatile";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
}
