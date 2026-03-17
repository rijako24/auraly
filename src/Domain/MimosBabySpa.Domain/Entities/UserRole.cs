namespace MimosBabySpa.Domain.Entities;

public class UserRole
{
    public Guid UserRoleId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? BusinessId { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }

    public virtual AppUser User { get; set; } = null!;
    public virtual AppRole Role { get; set; } = null!;
    public virtual Business? Business { get; set; }
    public virtual AppUser? AssignedByUser { get; set; }
}
