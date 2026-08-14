using Microsoft.Extensions.Logging;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public class LeadService : ILeadService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LeadService> _logger;

    public LeadService(IUnitOfWork unitOfWork, ILogger<LeadService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Domain.Entities.Lead> GetOrCreateLeadAsync(Guid businessId, string userNumber, string? customerName = null)
    {
        try
        {
            var existingLead = await _unitOfWork.Leads.GetByBusinessIdAndUserNumberAsync(businessId, userNumber);
            
            if (existingLead != null)
            {
                if (!string.IsNullOrEmpty(customerName) && string.IsNullOrEmpty(existingLead.CustomerName))
                {
                    existingLead.CustomerName = customerName;
                    await _unitOfWork.Leads.UpdateAsync(existingLead);
                    await _unitOfWork.SaveChangesAsync();
                }
                return existingLead;
            }

            var newLead = new Domain.Entities.Lead
            {
                LeadId = Guid.NewGuid(),
                BusinessId = businessId,
                UserNumber = userNumber,
                CustomerName = customerName,
                Status = "New",
                Timestamp = DateTime.UtcNow
            };

            var created = await _unitOfWork.Leads.CreateAsync(newLead);
            await _unitOfWork.SaveChangesAsync();
            
            _logger.LogDebug("Nuevo lead creado para {UserNumber} en negocio {BusinessId}", userNumber, businessId);
            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener o crear lead para {UserNumber} en negocio {BusinessId}", userNumber, businessId);
            throw;
        }
    }

    public async Task UpdateLeadAsync(Guid leadId, string? status = null, string? notes = null)
    {
        try
        {
            var lead = await _unitOfWork.Leads.GetByIdAsync(leadId);
            if (lead == null)
            {
                _logger.LogWarning("Lead {LeadId} no encontrado", leadId);
                return;
            }
            
            if (!string.IsNullOrEmpty(status))
                lead.Status = status;
            
            if (!string.IsNullOrEmpty(notes))
                lead.Notes = notes;

            await _unitOfWork.Leads.UpdateAsync(lead);
            await _unitOfWork.SaveChangesAsync();
            
            _logger.LogDebug("Lead {LeadId} actualizado", leadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar lead {LeadId}", leadId);
            throw;
        }
    }

    public async Task UpdateQualificationAsync(Guid leadId, LeadQualificationUpdate update, CancellationToken ct = default)
    {
        var lead = await _unitOfWork.Leads.GetByIdAsync(leadId);
        if (lead is null)
        {
            _logger.LogWarning("Lead {LeadId} not found while updating qualification.", leadId);
            return;
        }

        lead.QualificationBand = update.Band;
        lead.QualificationPriority = update.Priority;
        lead.QualificationLabel = update.Label;
        lead.QualificationFlowId = update.FlowId;
        lead.QualificationStageId = update.StageId;
        lead.QualificationUpdatedAt = DateTime.UtcNow;
        if (update.Converted && lead.ConvertedAt is null)
            lead.ConvertedAt = DateTime.UtcNow;

        await _unitOfWork.Leads.UpdateAsync(lead);
        await _unitOfWork.SaveChangesAsync(ct);
    }
    public async Task<Domain.Entities.Lead?> GetLeadByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber)
    {
        return await _unitOfWork.Leads.GetByBusinessIdAndUserNumberAsync(businessId, userNumber);
    }

    public async Task SyncCustomerIdentityAsync(
        Guid businessId,
        string userNumber,
        string? customerName = null,
        string? customerEmail = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerName) && string.IsNullOrWhiteSpace(customerEmail))
            return;

        var lead = await GetOrCreateLeadAsync(businessId, userNumber, customerName);

        var changed = false;
        if (!string.IsNullOrWhiteSpace(customerName)
            && !string.Equals(lead.CustomerName, customerName, StringComparison.Ordinal))
        {
            lead.CustomerName = customerName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(customerEmail)
            && !string.Equals(lead.CustomerEmail, customerEmail, StringComparison.Ordinal))
        {
            lead.CustomerEmail = customerEmail;
            changed = true;
        }

        if (!changed)
            return;

        await _unitOfWork.Leads.UpdateAsync(lead);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
