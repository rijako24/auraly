using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IWhatsAppChannelAdminService
{
    Task<IReadOnlyList<WhatsAppChannelDto>> GetByBusinessAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct = default);
    Task<WhatsAppChannelDto> CreateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CreateWhatsAppChannelRequest request, CancellationToken ct = default);
    Task<WhatsAppChannelDto> UpdateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid channelId, UpdateWhatsAppChannelRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid channelId, CancellationToken ct = default);
    Task<WhatsAppChannelConnectionStatusDto> ValidateAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, Guid channelId, CancellationToken ct = default);
}
