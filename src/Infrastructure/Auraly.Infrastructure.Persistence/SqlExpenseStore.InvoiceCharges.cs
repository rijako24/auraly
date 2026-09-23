using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    internal static async Task AcceptInvoiceChargesAsync(SqlConnection connection, SqlTransaction transaction,
        PosSaleUploadRequest sale, WithholdingService taxation,
        Auraly.BuildingBlocks.Domain.Identifiers.IAuralyIdGenerator ids, DateTimeOffset now, CancellationToken ct)
    {
        if (sale.Charges is not { Count: > 0 } charges) return;
        InvoiceChargeApplication.ValidateSnapshot(sale.Lines.Sum(line => line.LineTotal), charges);
        var payableCharges = charges.Where(charge => charge.Amount > 0).ToArray();
        if (payableCharges.Length == 0) return;
        var issuedAt = sale.CommercialSnapshot.IssuedAt;
        // Rules are loaded once for the independently accepted supplier expenses.
        // Supplier profiles come from the verified charge version, not mutable masters.
        var plan = await taxation.PrepareCalculationPlanAsync(sale.TenantId, sale.BusinessId, [], ct);
        var numbers = await SqlOperationalDocumentAllocator.AllocateNumbersAsync(connection, transaction,
            sale.BusinessId, ExpenseDocumentTypes.Expense, payableCharges.Length, now, ct);
        var accepted = payableCharges.Select((charge, index) => {
            var supplier = charge.Supplier;
            var profile = new CounterpartyTaxProfileView(sale.BusinessId, supplier.SupplierId,
                supplier.AppliesWithholding, supplier.TaxResponsibilities ?? [], supplier.TaxJurisdictionCode, issuedAt);
            var withholding = taxation.Calculate(plan with {
                Profiles = new Dictionary<Guid, CounterpartyTaxProfileView> { [supplier.SupplierId] = profile }
            }, new WithholdingPreviewRequest(sale.BusinessId, WithholdingDirections.Purchase,
                WithholdingRecognitionMoments.Accrual, supplier.SupplierId, charge.WithholdingConceptCode,
                supplier.TaxJurisdictionCode, charge.SupplierUntaxedAmount, charge.SupplierVatAmount, issuedAt));
            var number = numbers[index];
            var payload = new ExpenseDocumentPayload(sale.TenantId, sale.BusinessId, charge.AppliedChargeId,
                supplier.SupplierId, charge.ExpenseConceptId, charge.ExpenseAccountId, charge.CostCenterId,
                sale.SoldByUserId, number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode,
                number.Consecutive, null, issuedAt, issuedAt.AddDays(supplier.DefaultPaymentDueDays),
                "COP", charge.Name, charge.SupplierUntaxedAmount, charge.SupplierVatAmount,
                charge.Amount, null, withholding, sale.DocumentId,
                supplier.PurchaseEvidencePolicy == PurchaseEvidenceTypes.BuyerElectronicSupportDocument
                    ? PurchaseEvidenceTypes.BuyerElectronicSupportDocument
                    : PurchaseEvidenceTypes.InternalReceiptVoucher);
            return new AcceptedExpense(payload, $"invoice-charge:{charge.AppliedChargeId:N}",
                SHA256.HashData(Encoding.UTF8.GetBytes(ExpenseContractSerializer.Serialize(payload))), ids.NewId());
        }).ToArray();
        var supportExpenses = accepted.Where((_, index) => payableCharges[index].Supplier.PurchaseEvidencePolicy
            == "BuyerElectronicSupportDocument").Select(item => item.Payload).ToArray();
        IReadOnlyList<SqlGoodsReceiptStore.SupportFiscalAllocation> supports = [];
        if (supportExpenses.Length > 0)
        {
            if (!await SqlDianDocumentQuota.TryReserveManyAsync(connection, transaction, sale.BusinessId,
                    supportExpenses.Select(expense => expense.ExpenseId).ToArray(), "SupportDocument", now, ct))
                throw new InvalidOperationException("No hay cupo para el documento soporte de los cargos de factura.");
            supports = await SqlGoodsReceiptStore.AllocateSupportFiscalBatchAsync(connection, transaction,
                sale.BusinessId, supportExpenses.Select(expense => expense.SupplierId).ToArray(), issuedAt, now, ct);
        }
        await PersistAcceptedAsync(connection, transaction, accepted, now, ct);
        if (supports.Count > 0)
            await InsertSupportFiscalBatchAsync(connection, transaction,
                supportExpenses.Select((expense, index) => (expense, supports[index])).ToArray(), now, ct);
    }
}
