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

        return new WhatsAppCredentials(
            active.WhatsAppPhoneNumberId,
            active.WhatsAppAccessToken,
            active.WhatsAppBusinessAccountId);
    }

    public async Task<WhatsAppCredentials?> ResolveAsync(
        Guid businessId,
        string whatsAppPhoneNumberId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(whatsAppPhoneNumberId))
            return await ResolveAsync(businessId, cancellationToken);

        var normalizedPhoneNumberId = whatsAppPhoneNumberId.Trim();
        var numbers = await _unitOfWork.BusinessWhatsAppNumbers.GetByBusinessIdAsync(businessId);
        var active = numbers.FirstOrDefault(number =>
            number.IsActive &&
            string.Equals(
                number.WhatsAppPhoneNumberId?.Trim(),
                normalizedPhoneNumberId,
                StringComparison.Ordinal));

        if (active is null)
        {
            _logger.LogWarning(
                "Negocio {BusinessId} no tiene activo el numero receptor WhatsApp {PhoneNumberId}",
                businessId,
                normalizedPhoneNumberId);
            return null;
        }

        return new WhatsAppCredentials(
            active.WhatsAppPhoneNumberId,
            active.WhatsAppAccessToken,
            active.WhatsAppBusinessAccountId);
    }
}
