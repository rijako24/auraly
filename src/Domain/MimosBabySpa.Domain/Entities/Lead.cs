namespace MimosBabySpa.Domain.Entities;

public class Lead
{
    public Guid LeadId { get; set; }
    public Guid BusinessId { get; set; }
    public string UserNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "New"; // New, Contacted, Closed
    public DateTime Timestamp { get; set; }
    public string? CustomerName { get; set; }
    public string? Notes { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
}
