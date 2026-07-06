namespace MimosBabySpa.Application.Agents.Tools;

/// <summary>
/// Capacidades semanticas estables de las tools.
/// A diferencia de Name, estos ids no son parte del contrato de function calling.
/// </summary>
public static class ToolCapabilities
{
    public const string FactWrite = "fact.write";
    public const string HumanEscalate = "human.escalate";
    public const string CheckoutPrepare = "checkout.prepare";
    public const string ReservationCreate = "reservation.create";
    public const string PaidReservationReschedule = "reservation.reschedule_paid";
    public const string ProductSearch = "commerce.product_search";
    public const string OrderDraftUpdate = "commerce.order_draft_update";
    public const string OrderCreate = "commerce.order_create";
}
