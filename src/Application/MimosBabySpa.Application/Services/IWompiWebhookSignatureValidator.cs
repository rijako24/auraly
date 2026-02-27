using System.Text.Json;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Valida la firma de los webhooks de Wompi para asegurar autenticidad.
/// </summary>
public interface IWompiWebhookSignatureValidator
{
    /// <summary>
    /// Valida que el evento sea auténtico según el checksum de Wompi.
    /// </summary>
    /// <param name="root">Raíz del JSON del evento ya parseado.</param>
    /// <param name="eventsSecret">Secreto de eventos configurado.</param>
    /// <returns>true si la firma es válida o si no hay secreto configurado (modo dev).</returns>
    bool Validate(JsonElement root, string eventsSecret);
}
