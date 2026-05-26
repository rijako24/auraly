namespace MimosBabySpa.Application.Agents.Configuration;

public static class StageCompletionCriteria
{
    /// <summary>El stage se completa al ejecutarse la primera vez (saludos, disclaimers).</summary>
    public const string Always = "always";

    /// <summary>El stage se completa cuando todos los facts de <c>collects</c> están presentes.</summary>
    public const string FactsCollected = "factsCollected";

    /// <summary>El stage se completa cuando su tool execute corrió exitosamente.</summary>
    public const string ToolSucceeded = "toolSucceeded";

    /// <summary>El stage se completa cuando el usuario emite un intent de confirmación explícita.</summary>
    public const string UserConfirms = "userConfirms";
}
