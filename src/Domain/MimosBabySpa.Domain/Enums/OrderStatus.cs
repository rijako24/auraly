namespace MimosBabySpa.Domain.Enums;

public enum OrderStatus
{
    Draft = 0,
    PendingConfirmation = 1,
    Confirmed = 2,
    SyncPending = 3,
    Synced = 4,
    SyncFailed = 5,
    Cancelled = 6,
    AwaitingPayment = 7,
    Expired = 91
}
