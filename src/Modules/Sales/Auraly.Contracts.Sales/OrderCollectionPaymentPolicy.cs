namespace Auraly.Contracts.Sales;

// Shared custody rule for online and Edge. Financing belongs to the native
// customer-credit contract, not a separate pending payment instrument.
public static class OrderCollectionPaymentPolicy
{
    // A previously confirmed transaction is already collected money. Delivery
    // must not collect it again; this fact is independent of account settings.
    public static SaleCollectionMode ForNewSale(Guid? sourceOrderId, bool paymentAlreadyConfirmed = false)
        =>sourceOrderId is null || paymentAlreadyConfirmed ? SaleCollectionMode.Session : SaleCollectionMode.Dispatch;

    public static string? Validate(Guid? sourceOrderId, SaleCollectionMode collectionMode)
    {
        if(!Enum.IsDefined(collectionMode)||collectionMode==SaleCollectionMode.Dispatch&&(sourceOrderId is null||sourceOrderId==Guid.Empty))
            return "La atribución del recaudo no corresponde al origen de la venta.";
        return null;
    }
}


[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SaleCollectionMode>))]
public enum SaleCollectionMode
{
    Session=0,
    Dispatch=1
}
