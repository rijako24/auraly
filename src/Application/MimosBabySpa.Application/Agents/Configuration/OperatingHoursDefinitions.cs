namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Politica declarativa del agente para responder cuando el negocio esta fuera de horario.
/// </summary>
public sealed class OperatingHoursDefinitions
{
    public bool Enforce { get; set; }

    // Compatibilidad con SettingsJson existentes que usaban operatingHours.enabled.
    public bool Enabled
    {
        get => Enforce;
        set => Enforce = value;
    }
}
