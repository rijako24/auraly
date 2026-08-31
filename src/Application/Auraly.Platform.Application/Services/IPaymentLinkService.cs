namespace Auraly.Platform.Application.Services;

/// <summary>
/// Abstracción para generar links de pago por anticipo.
/// Implementación concreta en Infrastructure (Wompi u otro proveedor).
/// </summary>
public interface IPaymentLinkService
{
    Task<PaymentLinkResult> GenerateAnticipoLinkAsync(
        PaymentLinkRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Consulta el estado del pago asociado a un payment link.
    /// Usado para verificación en caliente cuando el usuario dice "ya pagué".
    /// </summary>
    Task<PaymentStatusResult> CheckPaymentStatusAsync(
        string paymentReferenceId,
        Guid businessId,
        CancellationToken ct = default,
        int? merchantConfigurationVersion = null);

    /// <summary>
    /// Verifica una transacción directamente en el proveedor por su ID.
    /// Usado por el webhook para validar el pago de forma independiente (no confiar en el payload).
    /// Retorna PaymentLinkId para correlacionar con la conversación.
    /// </summary>
    Task<VerifiedTransactionResult> VerifyTransactionAsync(
        string transactionId,
        Guid businessId,
        CancellationToken ct = default,
        int? merchantConfigurationVersion = null);

    /// <summary>
    /// Prepara los datos firmados que el frontend entrega al widget oficial de Wompi.
    /// La clave de integridad nunca sale del servidor.
    /// </summary>
    Task<WompiWidgetCheckoutResult> PrepareWidgetCheckoutAsync(
        WompiWidgetCheckoutRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Resultado de la verificación de una transacción por ID (GET /transactions/{id}).
/// Incluye PaymentLinkId para correlacionar webhook → conversación.
/// </summary>
public record VerifiedTransactionResult(
    bool IsApproved,
    string? TransactionId,
    long? AmountInCents,
    string? PaymentLinkId,
    string? Reference,
    string? ErrorMessage);

public sealed record WompiWidgetCheckoutRequest(
    Guid BusinessId,
    string Reference,
    long AmountInCents,
    string Currency,
    DateTimeOffset? ExpiresAt = null,
    string? RedirectUrl = null);

public sealed record WompiWidgetCheckoutResult(
    bool Success,
    string? PublicKey,
    string? Reference,
    long? AmountInCents,
    string? Currency,
    string? IntegritySignature,
    string? ExpirationTime,
    string? RedirectUrl,
    string? ErrorMessage,
    int MerchantConfigurationVersion = 1);

/// <summary>
/// Resultado de la consulta de estado de pago.
/// </summary>
public record PaymentStatusResult(
    bool IsApproved,
    string? TransactionId,
    long? AmountInCents,
    string? ErrorMessage);

/// <summary>
/// Request para generar un link de pago.
/// </summary>
public record PaymentLinkRequest(
    Guid BusinessId,
    Guid ConversationId,
    string CustomerPhone,
    string ServiceDescription,
    long AmountInCents,
    string Currency,
    int ExpirationMinutes);

/// <summary>
/// Resultado de la generación del link.
/// </summary>
public record PaymentLinkResult(
    bool Success,
    string? PaymentLinkUrl,
    string? PaymentReferenceId,
    DateTime? ExpiresAt,
    string? ErrorMessage,
    int MerchantConfigurationVersion = 1);
