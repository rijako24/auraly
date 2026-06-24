namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Nombres canonicos de side-effects que una tool puede declarar en su resultado JSON.
/// Son senales genericas del runtime, no eventos de negocio del tenant.
/// </summary>
public static class ToolSideEffectNames
{
    public const string RequestCompleted = "request_completed";
    public const string EscalatedToHuman = "escalated_to_human";
}