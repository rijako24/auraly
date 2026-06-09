namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Ciclo de vida de un engagement (Conversation) por cliente y negocio.
/// </summary>
public enum ConversationLifecycleStatus
{
    Active = 0,
    Closed = 1
}

public static class ConversationCloseReasons
{
    public const string ReservationConfirmed = "reservation_confirmed";
    public const string DayChanged = "day_changed";
    public const string Timeout = "timeout";
    public const string UserCancelled = "user_cancelled";
    public const string Manual = "manual";
}
