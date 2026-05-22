namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Nombres canónicos de side-effects que una tool puede declarar en su
/// resultado JSON (campo "effects": ["..."]).
///
/// El orquestador los consume sin conocer el nombre de la tool que los produjo,
/// respetando OCP: añadir una tool con efecto observable no requiere tocar el orquestador.
/// </summary>
public static class ToolSideEffectNames
{
    public const string ReservationCreated = "reservation_created";
    public const string EscalatedToHuman   = "escalated_to_human";
}
