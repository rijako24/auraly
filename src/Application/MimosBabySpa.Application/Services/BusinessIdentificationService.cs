using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class BusinessIdentificationService : IBusinessIdentificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BusinessIdentificationService> _logger;

    public BusinessIdentificationService(
        IUnitOfWork unitOfWork,
        ILogger<BusinessIdentificationService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<BusinessContext?> IdentifyBusinessAsync(string whatsAppPhoneNumberId)
    {
        try
        {
            var whatsAppNumber = await _unitOfWork.BusinessWhatsAppNumbers
                .GetByWhatsAppPhoneNumberIdAsync(whatsAppPhoneNumberId);

            if (whatsAppNumber == null || !whatsAppNumber.Business.IsActive)
            {
                _logger.LogWarning("No se encontró negocio activo para WhatsAppPhoneNumberId: {PhoneNumberId}",
                    whatsAppPhoneNumberId);
                return null;
            }

            var business = whatsAppNumber.Business;

            return new BusinessContext
            {
                BusinessId = business.BusinessId,
                TenantId = business.TenantId,
                BusinessName = business.Name,
                AgentId = whatsAppNumber.AgentId,
                WhatsAppNumber = new BusinessWhatsAppNumberDto
                {
                    PhoneNumber = whatsAppNumber.PhoneNumber,
                    WhatsAppPhoneNumberId = whatsAppNumber.WhatsAppPhoneNumberId,
                    WhatsAppAccessToken = whatsAppNumber.WhatsAppAccessToken
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error identificando negocio para WhatsAppPhoneNumberId: {PhoneNumberId}",
                whatsAppPhoneNumberId);
            return null;
        }
    }

    public async Task<BusinessContext?> IdentifyBusinessByUserNumberAsync(string userPhoneNumber)
    {
        var conversation = await _unitOfWork.Conversations.GetByUserNumberAsync(userPhoneNumber);
        if (conversation == null)
            return null;

        var business = conversation.Business;

        return new BusinessContext
        {
            BusinessId = business.BusinessId,
            TenantId = business.TenantId,
            BusinessName = business.Name
        };
    }
}
