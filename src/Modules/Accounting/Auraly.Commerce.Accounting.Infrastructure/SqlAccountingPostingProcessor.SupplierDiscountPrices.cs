using System.Data;
using System.Text.Json;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private sealed record SupplierDiscountPriceLine(Guid PayableId, decimal Adjustment,
        Guid ReceiptId, int ReceiptLineNumber, Guid ProductId, Guid ParentProductId,
        decimal PriceFactor, decimal EligibleAmount, decimal EligibleQuantity, decimal CurrentCost);

    // Price preparation is part of the accepted payment transaction. Inventory
    // balances, their valuation and historical movements are never written here.
    private async Task PrepareSupplierDiscountPricesAsync(SqlConnection connection,
        SqlTransaction transaction, SupplierPaymentDocumentPayload payment, CancellationToken token)
        => await PrepareSupplierDiscountPricesAsync(connection, transaction, payment.BusinessId,
            payment.ConfirmedByUserId, [payment.PaymentId], token);

    private async Task PrepareSupplierDiscountPricesAsync(SqlConnection connection,
        SqlTransaction transaction, Guid businessId, Guid userId, IReadOnlyList<Guid> paymentIds,
        CancellationToken token)
    {
        if (paymentIds.Count == 0) return;
        await using var read = new SqlCommand("""
            DECLARE @PaymentKeys TABLE(PaymentId uniqueidentifier PRIMARY KEY);
            INSERT @PaymentKeys SELECT DISTINCT PaymentId FROM OPENJSON(@PaymentIds)
              WITH(PaymentId uniqueidentifier '$');
            IF (SELECT COUNT(*) FROM @PaymentKeys)<>@PaymentCount
              THROW 51607,'The discounted payment batch is incomplete.',1;
            SELECT COUNT(*) FROM dbo.SupplierPaymentApplications application
            JOIN @PaymentKeys keyset ON keyset.PaymentId=application.PaymentId
            WHERE application.FinancialDiscountAdjustment IS NOT NULL
              AND application.FinancialDiscountAdjustment<>0;
            SELECT application.PayableId,application.FinancialDiscountAdjustment,receipt.GoodsReceiptId,
              line.ReceiptLineNumber,original.ProductId,COALESCE(link.ParentProductId,original.ProductId),
              COALESCE(link.PriceFactor,CONVERT(decimal(19,6),1)),line.EligibleLineAmount,line.EligibleQuantity,
              COALESCE(preparation.CostBasisAmount,
                CASE WHEN tenant.InventoryCostBasis=N'WeightedAverageCost' AND balance.QuantityOnHand>0
                  THEN balance.AverageUnitCost END,
                latest.LatestUnitCost,price.CostBasisAmount,
                CASE WHEN balance.QuantityOnHand>0 THEN balance.AverageUnitCost END,
                original.RecognizedInventoryCostAmount/original.Quantity/COALESCE(link.PriceFactor,1)) CurrentCost
            FROM dbo.SupplierPaymentApplications application
            JOIN @PaymentKeys keyset ON keyset.PaymentId=application.PaymentId
            JOIN dbo.SupplierPayments payment ON payment.PaymentId=application.PaymentId AND payment.BusinessId=@BusinessId
            JOIN dbo.Payables payable ON payable.PayableId=application.PayableId AND payable.BusinessId=@BusinessId
              AND payable.SourceDocumentType=N'GoodsReceipt'
            JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=payable.SourceDocumentId
              AND receipt.BusinessId=@BusinessId AND receipt.SupplierId=payment.SupplierId
            JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
            JOIN dbo.Tenants tenant ON tenant.TenantId=business.TenantId
            CROSS APPLY accounting.GoodsReceiptFinancialDiscountLines(@BusinessId,receipt.GoodsReceiptId) line
            JOIN dbo.GoodsReceiptLines original ON original.GoodsReceiptId=receipt.GoodsReceiptId
              AND original.LineNumber=line.ReceiptLineNumber
            LEFT JOIN dbo.ProductLinks link ON link.TenantId=business.TenantId AND link.ChildProductId=original.ProductId
              AND link.SharesPrice=1 AND link.IsActive=1
            JOIN dbo.ProductPrices price ON price.BusinessId=@BusinessId
              AND price.ProductId=COALESCE(link.ParentProductId,original.ProductId) AND price.IsActive=1
            LEFT JOIN dbo.ProductPricePreparations preparation ON preparation.BusinessId=@BusinessId
              AND preparation.ProductId=price.ProductId AND preparation.Status=N'Pending'
            LEFT JOIN dbo.SupplierProductLatestCosts latest ON latest.BusinessId=@BusinessId
              AND latest.SupplierId=payment.SupplierId AND latest.ProductId=price.ProductId
            LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
              AND balance.WarehouseId=receipt.WarehouseId AND balance.ProductId=price.ProductId
            WHERE application.FinancialDiscountAdjustment IS NOT NULL
              AND application.FinancialDiscountAdjustment<>0
            ORDER BY application.PayableId,original.LineNumber;
            """, connection, transaction);
        read.Parameters.AddWithValue("@BusinessId", businessId);
        read.Parameters.Add("@PaymentIds", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(paymentIds);
        read.Parameters.AddWithValue("@PaymentCount", paymentIds.Count);
        var lines = new List<SupplierDiscountPriceLine>();
        await using (var reader = await read.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
                throw new InvalidOperationException("The supplier discount decision count is missing.");
            var decisions = reader.GetInt32(0);
            if (decisions == 0) return;
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (reader.IsDBNull(9))
                    throw new InvalidOperationException("El pago no se aplicó: falta el costo base para preparar el precio futuro.");
                lines.Add(new(reader.GetGuid(0), reader.GetDecimal(1), reader.GetGuid(2),
                    reader.GetInt32(3), reader.GetGuid(4), reader.GetGuid(5), reader.GetDecimal(6),
                    reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9)));
            }
            if (lines.Select(line => line.PayableId).Distinct().Count() != decisions)
                throw new InvalidOperationException("El pago no se aplicó: faltan las líneas originales del descuento financiero.");
        }
        foreach (var payable in lines.GroupBy(line => line.PayableId))
        {
            if (payable.Select(line => line.Adjustment).Distinct().Count() != 1 ||
                payable.Sum(line => line.EligibleAmount) != Math.Abs(payable.First().Adjustment))
                throw new InvalidOperationException("El pago no se aplicó: el descuento no concilia con la compra y sus devoluciones.");
        }
        var families = lines.GroupBy(line => line.ParentProductId).Select(parent =>
        {
            var currentCost = parent.First().CurrentCost;
            if (parent.Any(line => line.CurrentCost != currentCost))
                throw new InvalidOperationException("El pago no se aplicó: el producto tiene costos base distintos sin una preparación vigente única.");
            var delta = parent.GroupBy(line => line.PayableId).Sum(receipt =>
            {
                var quantity = receipt.Sum(line => line.EligibleQuantity * line.PriceFactor);
                if (quantity <= 0)
                    throw new InvalidOperationException("El pago no se aplicó: la compra no conserva unidades elegibles para el descuento.");
                return Math.Sign(receipt.First().Adjustment) * receipt.Sum(line => line.EligibleAmount) / quantity;
            });
            var futureCost = decimal.Round(currentCost - delta, 6, MidpointRounding.AwayFromZero);
            if (futureCost <= 0)
                throw new InvalidOperationException("El pago no se aplicó: el costo futuro calculado no es positivo.");
            var origin = parent.First();
            return new { ParentProductId = parent.Key, ParentCost = futureCost,
                ChildProductId = origin.ProductId == parent.Key ? (Guid?)null : origin.ProductId,
                SourceDocumentId = origin.ReceiptId, SourceLineNumber = origin.ReceiptLineNumber };
        }).ToArray();
        await using var prepare = new SqlCommand("dbo.ProductLinkedCostsPrepare", connection, transaction)
            { CommandType = CommandType.StoredProcedure };
        prepare.Parameters.AddWithValue("@BusinessId", businessId);
        prepare.Parameters.AddWithValue("@UserId", userId);
        prepare.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        prepare.Parameters.Add("@FamiliesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(families);
        prepare.Parameters.AddWithValue("@PrepareParents", true);
        await prepare.ExecuteNonQueryAsync(token);
    }
}
