using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Infrastructure.Services;

/// <summary>
/// Resuelve credenciales de WhatsApp desde BusinessWhatsAppNumbers.
/// Fuente única por negocio.
/// </summary>
public class WhatsAppCredentialResolver : IWhatsAppCredentialResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WhatsAppCredentialResolver> _logger;

    public WhatsAppCredentialResolver(IUnitOfWork unitOfWork, ILogger<WhatsAppCredentialResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<WhatsAppCredentials?> ResolveAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var numbers = await _unitOfWork.BusinessWhatsAppNumbers.GetByBusinessIdAsync(businessId);
        var active = numbers.FirstOrDefault();
        if (active == null)
        {
            _logger.LogWarning("Negocio {BusinessId} no tiene número WhatsApp activo", businessId);
            return null;
        }

        return new WhatsAppCredentials(active.WhatsAppPhoneNumberId, active.WhatsAppAccessToken);
    }
}
