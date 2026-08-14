namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Representa un empleado del negocio con sus capacidades de servicio
/// </summary>
public class Employee
{
    public Guid EmployeeId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? PartyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<EmployeeService> EmployeeServices { get; set; } = new List<EmployeeService>();
    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
