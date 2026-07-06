using MimosBabySpa.Application.WhatsAppTemplates.DTOs;

namespace MimosBabySpa.Application.WhatsAppTemplates.Interfaces;

public interface IWhatsAppTemplateService
{
    Task<IReadOnlyList<WhatsAppTemplateDto>> GetByBusinessIdAsync(
        Guid businessId,
        bool approvedOnly = true,
        CancellationToken ct = default);
}
