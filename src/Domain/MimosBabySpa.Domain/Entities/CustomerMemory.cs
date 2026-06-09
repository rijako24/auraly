namespace MimosBabySpa.Domain.Entities;

public class CustomerMemory
{
    public Guid CustomerMemoryId { get; set; }
    public Guid BusinessId { get; set; }
    public string UserNumber { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
}
