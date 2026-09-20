using Auraly.BuildingBlocks.Domain.Money;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Domain.Sales;

namespace Auraly.Application.Sales;

public static class InvoiceChargeClosureProjection
{
    public static IReadOnlyList<WorkSessionInvoiceCharge> MapPayments(IEnumerable<WorkSessionInvoiceCharge> charges,
        Func<string, string> closureMethod) => charges.Select(charge => charge with {
            Payments = charge.Payments.Select(payment => payment with {
                PaymentMethodCode = closureMethod(payment.PaymentMethodCode) }).ToArray()
        }).ToArray();

    public static IReadOnlyList<WorkSessionInvoiceCharge> FromSale(PosSaleUploadRequest sale)
    {
        if (sale.Charges is not { Count: > 0 } charges) return [];
        var payments = sale.Payments.Where(payment => payment.Amount > 0)
            .Select(payment => new InvoiceChargePaymentShare(payment.PaymentNumber, payment.Amount)).ToList();
        if (sale.Credit is { Amount: > 0 } credit) payments.Add(new(0, credit.Amount));
        var totalApplied = payments.Sum(payment => payment.AppliedAmount);
        var gross = sale.CommercialSnapshot.PayableAmount;
        return charges.Select(charge => {
            var collectedOrCredit = gross == 0 ? 0 : MonetaryRounding.RoundLineAmount(
                charge.InvoicedAmount * totalApplied / gross);
            var allocations = totalApplied == 0 ? [] : InvoiceChargeCalculation.Allocate(collectedOrCredit, payments);
            return new WorkSessionInvoiceCharge(sale.DocumentId, sale.DocumentNumber.FullNumber,
                charge.AppliedChargeId, charge.ChargeId, charge.Code, charge.Name, charge.Supplier.Name,
                charge.Amount, charge.InvoicedAmount, charge.ExpenseAmount,
                charge.InvoicedAmount - collectedOrCredit,
                allocations.Where(allocation => allocation.Amount > 0).Select(allocation =>
                    new WorkSessionInvoiceChargePayment(allocation.PaymentNumber,
                        allocation.PaymentNumber == 0 ? "Credit" : sale.Payments.Single(
                            payment => payment.PaymentNumber == allocation.PaymentNumber).MethodCode,
                        allocation.Amount)).ToArray());
        }).ToArray();
    }
}
