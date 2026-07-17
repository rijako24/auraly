using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve credenciales de WhatsApp por negocio.
/// Fuente única: BusinessWhatsAppNumbers.
/// </summary>
public interface IWhatsAppCredentialResolver
{
    /// <summary>
    /// Obtiene las credenciales activas para el negocio.
    /// </summary>
    /// <param name="businessId">ID del negocio</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Credenciales o null si el negocio no tiene número configurado</returns>
    Task<WhatsAppCredentials?> ResolveAsync(Guid businessId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene las credenciales del numero receptor exacto dentro del negocio.
    /// </summary>
    Task<WhatsAppCredentials?> ResolveAsync(
        Guid businessId,
        string whatsAppPhoneNumberId,
        CancellationToken cancellationToken = default);
}
