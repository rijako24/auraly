namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Politica declarativa del agente para limitar grupos operativos fuera del horario laboral del negocio.
/// </summary>
public sealed class OperatingHoursDefinitions
{
    public bool Enabled { get; set; }

    public IReadOnlyList<string> GatedGroups { get; set; } = [];
}