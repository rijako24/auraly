namespace Auraly.Contracts.Sales;

// Shared acceptance rule for online and Edge. This is a logical collection
// instrument, restricted to a whole order receipt; it is never received money.
public static class OrderCollectionPaymentPolicy
{
    public const string PendingMethod = "PendingCollection";

    // A previously confirmed transaction is already collected money. Delivery
    // must not collect it again; this fact is independent of account settings.
    public static SaleCollectionMode ForNewSale(Guid? sourceOrderId, bool paymentAlreadyConfirmed = false)
        =>sourceOrderId is null || paymentAlreadyConfirmed ? SaleCollectionMode.Session : SaleCollectionMode.Dispatch;

    public static string? Validate(Guid? sourceOrderId, string documentType, bool hasCredit,
        IReadOnlyCollection<SalePaymentPurpose> payments, SaleCollectionMode collectionMode=SaleCollectionMode.Session)
    {
        if(!Enum.IsDefined(collectionMode)||collectionMode==SaleCollectionMode.Dispatch&&(sourceOrderId is null||sourceOrderId==Guid.Empty))
            return "La atribución del recaudo no corresponde al origen de la venta.";
        if (!payments.Any(payment => payment.MethodCode == PendingMethod)) return null;
        return sourceOrderId is null || sourceOrderId==Guid.Empty || documentType != PosSaleDocumentTypes.Receipt || hasCredit ||
            collectionMode!=SaleCollectionMode.Dispatch || payments.Count != 1 || payments.Any(payment => !payment.HasNativeForm || payment.HasReceivedEvidence)
            ? "Por recaudar requiere una forma configurada y el valor completo de un comprobante de pedido, sin crédito ni dinero recibido."
            : null;
    }
}

public readonly record struct SalePaymentPurpose(string MethodCode, bool HasNativeForm, bool HasReceivedEvidence);


[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SaleCollectionMode>))]
public enum SaleCollectionMode
{
    Session=0,
    Dispatch=1
}
