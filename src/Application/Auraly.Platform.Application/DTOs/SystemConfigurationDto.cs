using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.DTOs;

public class SystemConfigurationDto
{
    public Dictionary<SystemConfigurationKey, string> Configurations { get; set; } = new();
    
    // Métodos helper para facilitar el acceso
    public string? GetValue(SystemConfigurationKey key)
    {
        return Configurations.TryGetValue(key, out var value) ? value : null;
    }
    
    public bool HasKey(SystemConfigurationKey key)
    {
        return Configurations.ContainsKey(key);
    }
}
