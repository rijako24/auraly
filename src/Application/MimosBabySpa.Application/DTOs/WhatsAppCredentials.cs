namespace MimosBabySpa.Application.DTOs;

/// <summary>
/// Credenciales de WhatsApp Cloud API para un negocio.
/// </summary>
public record WhatsAppCredentials(string PhoneNumberId, string AccessToken, string? BusinessAccountId = null);
