namespace Auraly.Contracts.Sales;

// Validate the persisted custody contract, including already-issued queued documents.
// New online and Edge issuance uses the Session default of PosSaleUploadRequest.
public static class OrderCollectionPaymentPolicy
{
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
