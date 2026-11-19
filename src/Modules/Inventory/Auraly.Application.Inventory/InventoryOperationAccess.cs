using Auraly.Contracts.Inventory;

namespace Auraly.Application.Inventory;

/// <summary>Authorization of operation history uses the existing operation capabilities.
/// Stock and ledger access remain governed by inventory.read.</summary>
public static class InventoryOperationAccess
{
    public static IReadOnlyList<string> ReadableDocumentTypes(IReadOnlySet<string> permissions)
    {
        var types = new List<string>(7);
        if (permissions.Contains(InventoryPermissionCodes.Count) ||
            permissions.Contains(InventoryPermissionCodes.ManagePhysicalCounts) ||
            permissions.Contains(InventoryPermissionCodes.CapturePhysicalCounts)) types.Add(InventoryDocumentTypes.StockCount);
        if (permissions.Contains(InventoryPermissionCodes.Adjust)) types.Add(InventoryDocumentTypes.Adjustment);
        if (permissions.Contains(InventoryPermissionCodes.DispatchTransfer) ||
            permissions.Contains(InventoryPermissionCodes.ReceiveTransfer) ||
            permissions.Contains(InventoryPermissionCodes.ResolveTransferDifference)) types.Add(InventoryDocumentTypes.Transfer);
        if (permissions.Contains(InventoryPermissionCodes.Convert)) types.Add(InventoryDocumentTypes.Conversion);
        if (permissions.Contains(InventoryPermissionCodes.Produce)) types.Add(InventoryDocumentTypes.Production);
        if (permissions.Contains(InventoryPermissionCodes.Damage)) types.Add(InventoryDocumentTypes.Damage);
        // This history also includes receipts owned and authorized by Purchasing.
        if (permissions.Contains("purchasing.goods-receipts.read")) types.Add("GoodsReceipt");
        return types;
    }

    public static void RequireRead(InventoryUserIdentity user, string documentType)
    {
        if (!ReadableDocumentTypes(user.Permissions).Contains(documentType, StringComparer.Ordinal))
            throw new InventoryForbiddenException("No tienes permiso para consultar esta operación de inventario.");
    }
}
