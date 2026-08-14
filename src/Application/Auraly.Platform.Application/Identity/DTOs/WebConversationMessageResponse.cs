namespace Auraly.Platform.Application.Identity.DTOs;

public record WebConversationMessageResponse(
    string Response,
    bool EscalatedToHuman,
    bool RequestCompleted);