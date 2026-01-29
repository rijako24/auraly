namespace MimosBabySpa.Application.Models;

/// <summary>
/// Regla de coexistencia entre servicios
/// </summary>
public class CoexistenceRule
{
    /// <summary>
    /// Servicios que pueden coexistir (si uno está en la lista, puede coexistir con el otro)
    /// </summary>
    public List<string> Services { get; set; } = new();
}
