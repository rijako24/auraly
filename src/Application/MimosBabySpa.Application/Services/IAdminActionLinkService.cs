namespace MimosBabySpa.Application.Services;

/// <summary>
/// Genera URLs firmadas para acciones administrativas (release, confirmar pago manual).
/// Usa HMAC para evitar links manipulables.
/// </summary>
public interface IAdminActionLinkService
{
    /// <summary>
    /// URL para devolver la conversación al bot.
    /// </summary>
    string? GenerateReleaseUrl(Guid conversationId);

    /// <summary>
    /// URL para que un admin confirme un pago manual (PaymentReferenceId).
    /// </summary>
    string? GeneratePaymentConfirmationUrl(string paymentReferenceId);

    /// <summary>
    /// Valida el token para el link de release.
    /// </summary>
    bool ValidateReleaseToken(Guid conversationId, string token);

    /// <summary>
    /// Valida el token para el link de confirmación de pago.
    /// </summary>
    bool ValidatePaymentConfirmationToken(string paymentReferenceId, string token);
}
