namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Resultado estructurado del orquestador.
///
/// El llamador usa <see cref="ReservationCreated"/> para decisiones (ej. actualizar Lead)
/// sin parsear el texto de la respuesta — que es frágil y no multitenant.
/// </summary>
public record OrchestratorResult(
    string Response,
    bool ReservationCreated);
