namespace Auraly.Platform.Domain.Entities;

public class Permission
{
    public Guid PermissionId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
