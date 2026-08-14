namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Relación many-to-many entre Employee y Service
/// Define qué servicios puede ofrecer cada empleado
/// </summary>
public class EmployeeService
{
    public Guid EmployeeServiceId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ServiceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Employee Employee { get; set; } = null!;
    public virtual Service Service { get; set; } = null!;
}
