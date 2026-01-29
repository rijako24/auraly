namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Información de un servicio
/// </summary>
public class ServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
