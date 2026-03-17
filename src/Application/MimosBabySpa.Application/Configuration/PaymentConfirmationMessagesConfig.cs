namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de mensajes enviados al cliente cuando se confirma el pago.
/// Se envían en orden secuencial. Soporta texto, adjuntos (imagen/documento) y placeholders.
/// </summary>
public class PaymentConfirmationMessagesConfig
{
    /// <summary>
    /// Mensajes enviados al cliente al confirmar el pago, en orden.
    /// </summary>
    public List<ConfirmationMessageItem> Messages { get; set; } = new();
}

/// <summary>
/// Un mensaje individual en la secuencia de confirmación de pago.
/// Body (texto). AttachmentId opcional, referencia BusinessAttachments.
/// </summary>
public class ConfirmationMessageItem
{
    /// <summary>
    /// Texto del mensaje. Si hay adjunto, se usa como caption.
    /// Soporta placeholders: {CustomerName}, {Service}, {Date}, {Time}, {Total}.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// ID del adjunto en BusinessAttachments. Opcional.
    /// </summary>
    public Guid? AttachmentId { get; set; }
}
