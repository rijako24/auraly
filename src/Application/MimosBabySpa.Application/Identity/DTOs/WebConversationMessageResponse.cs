namespace MimosBabySpa.Application.Identity.DTOs;

public record WebConversationMessageResponse(
    string Response,
    bool EscalatedToHuman,
    bool ReservationCreated);
