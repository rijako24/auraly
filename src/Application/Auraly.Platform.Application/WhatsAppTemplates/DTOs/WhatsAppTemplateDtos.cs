namespace Auraly.Platform.Application.WhatsAppTemplates.DTOs;

public sealed record WhatsAppTemplateDto(
    string Id,
    string Name,
    string Status,
    string Category,
    string Language,
    int HeaderParameterCount,
    int BodyParameterCount);
