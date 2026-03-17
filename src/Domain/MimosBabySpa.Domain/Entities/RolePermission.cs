namespace MimosBabySpa.Domain.Entities;

public class RolePermission
{
    public Guid RolePermissionId { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public DateTime AssignedAt { get; set; }

    public virtual AppRole Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}
