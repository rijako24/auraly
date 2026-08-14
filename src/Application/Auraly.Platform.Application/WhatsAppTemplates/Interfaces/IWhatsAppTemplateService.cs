using Auraly.Platform.Application.WhatsAppTemplates.DTOs;

namespace Auraly.Platform.Application.WhatsAppTemplates.Interfaces;

public interface IWhatsAppTemplateService
{
    Task<IReadOnlyList<WhatsAppTemplateDto>> GetByBusinessIdAsync(
        Guid businessId,
        bool approvedOnly = true,
        CancellationToken ct = default);
}
