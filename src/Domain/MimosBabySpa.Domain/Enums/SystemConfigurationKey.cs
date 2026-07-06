namespace MimosBabySpa.Domain.Enums;

public enum SystemConfigurationKey
{
    GoogleCalendarPlatformCredentials = 1,           // Credenciales globales de Google Calendar para calendarios administrados por Auraly.
    HumanEscalationErrorThreshold = 2               // Errores consecutivos del orquestador para escalar a humano (string: "2", "3", etc.)
}
