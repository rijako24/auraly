namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Fábrica para instanciar herramientas basándose en el tipo.
/// Patrón Factory para gestión centralizada de tools.
/// </summary>
public interface IToolFactory
{
    /// <summary>
    /// Obtiene una herramienta por tipo
    /// </summary>
    /// <param name="toolType">Tipo de herramienta requerida</param>
    /// <returns>Instancia del handler correspondiente</returns>
    IToolHandler GetTool(ToolType toolType);
}
