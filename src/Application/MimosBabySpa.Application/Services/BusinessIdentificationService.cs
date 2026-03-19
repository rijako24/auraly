using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class BusinessIdentificationService : IBusinessIdentificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBusinessConfigurationService _configService;
    private readonly ILogger<BusinessIdentificationService> _logger;

    public BusinessIdentificationService(
        IUnitOfWork unitOfWork,
        IBusinessConfigurationService configService,
        ILogger<BusinessIdentificationService> logger)
    {
        _unitOfWork = unitOfWork;
        _configService = configService;
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
            
            // Obtener todas las configuraciones del negocio
            var configuration = await _configService.GetConfigurationAsync(business.BusinessId);
            
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
                },
                Configuration = configuration
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
        if (conversation != null)
        {
            var business = conversation.Business;
            var configuration = await _configService.GetConfigurationAsync(business.BusinessId);
            
            return new BusinessContext
            {
                BusinessId = business.BusinessId,
                TenantId = business.TenantId,
                BusinessName = business.Name,
                Configuration = configuration
            };
        }
        
        return null;
    }
}
