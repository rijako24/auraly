namespace Auraly.Platform.Application.Identity.DTOs;

public sealed record WhatsAppChannelDto(Guid BusinessWhatsAppNumberId, Guid BusinessId, Guid AgentId,
    string AgentName, string PhoneNumber, string WhatsAppPhoneNumberId, string WhatsAppBusinessAccountId,
    bool HasAccessToken, bool IsActive, DateTime CreatedAt);

public sealed record CreateWhatsAppChannelRequest(Guid AgentId, string PhoneNumber,
    string WhatsAppPhoneNumberId, string WhatsAppBusinessAccountId, string AccessToken, bool IsActive = true);

public sealed record UpdateWhatsAppChannelRequest(Guid AgentId, string PhoneNumber,
    string WhatsAppPhoneNumberId, string WhatsAppBusinessAccountId, string? AccessToken, bool IsActive);

public sealed record WhatsAppChannelConnectionStatusDto(bool IsConnected, string Status, string Message,
    string? VerifiedName, string? DisplayPhoneNumber, string? QualityRating,
    string? BusinessAccountName, DateTime CheckedAtUtc);
