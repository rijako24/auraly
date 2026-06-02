using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve una secuencia nombrada del catálogo del agente a mensajes outbound listos para enviar.
/// No envía WhatsApp: solo expande placeholders y resuelve URLs de adjuntos (SAS).
/// </summary>
public interface IMessageSequenceResolver
{
    Task<IReadOnlyList<OutboundMessage>> ResolveAsync(
        Guid businessId,
        string sequenceName,
        MessageSequenceCatalog catalog,
        MessageSequenceContext context,
        CancellationToken ct = default);
}
