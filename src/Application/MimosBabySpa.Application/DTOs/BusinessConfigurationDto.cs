using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.DTOs;

public class BusinessConfigurationDto
{
    public Dictionary<BusinessConfigurationKey, string> Configurations { get; set; } = new();
    
    // Métodos helper para facilitar el acceso
    public string? GetValue(BusinessConfigurationKey key)
    {
        return Configurations.TryGetValue(key, out var value) ? value : null;
    }
    
    public bool HasKey(BusinessConfigurationKey key)
    {
        return Configurations.ContainsKey(key);
    }
}
