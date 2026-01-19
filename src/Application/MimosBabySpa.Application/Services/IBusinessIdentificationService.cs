using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public interface IBusinessIdentificationService
{
    /// <summary>
    /// Identifica el negocio basado en el número de teléfono de WhatsApp que recibió el mensaje.
    /// En el webhook, el Entry.Id contiene el Phone Number ID de WhatsApp.
    /// </summary>
    Task<BusinessContext?> IdentifyBusinessAsync(string whatsAppPhoneNumberId);
    
    /// <summary>
    /// Identifica el negocio basado en el número de teléfono del usuario (para búsquedas).
    /// </summary>
    Task<BusinessContext?> IdentifyBusinessByUserNumberAsync(string userPhoneNumber);
}
