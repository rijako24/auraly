namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Definición de un atributo de negocio
/// </summary>
public class AttributeDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AttributeType Type { get; set; }
    public bool IsRequired { get; set; }
    public string? ValidationPattern { get; set; }
    public string? DefaultValue { get; set; }
    public List<string>? AllowedValues { get; set; }
    
    /// <summary>
    /// Metadatos adicionales para validaciones (min, max, minLength, maxLength, etc.)
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
