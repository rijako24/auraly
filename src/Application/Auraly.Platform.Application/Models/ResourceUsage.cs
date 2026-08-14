namespace Auraly.Platform.Application.Models;

/// <summary>
/// Uso de recursos por un servicio específico
/// </summary>
public class ResourceUsage
{
    public Dictionary<string, int> Resources { get; set; } = new();
}
