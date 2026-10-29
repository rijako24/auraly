using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Commerce.Accounting.Application;

public sealed record UnapplicationCreditCandidate(Guid PositionId, Guid? AccountId, string? AccountCode,
    string? AccountName, string? Issue);

public sealed record UnapplicationCandidate(Guid? AccountId, string? AccountCode, string? AccountName,
    Guid PartyId, string PartyName, string? PartyIdentification, Guid? PartySiteId,
    string DocumentNumber, string CurrencyCode, decimal AvailableAmount, string? Issue,
    UnapplicationCreditCandidate? RestoredCredit = null, bool CancellationPending = false, bool SourceCancelled = false,
    bool FinancialDiscountDecided = false);

public sealed record PaymentCorrectionTender(Guid? AccountId, string? AccountCode, string? AccountName,
    string MethodCode, decimal Amount);

public sealed record PaymentCorrectionCandidate(Guid PaymentId, string DocumentNumber, decimal TotalAmount,
    IReadOnlyList<(Guid PositionId, Guid TransactionId, UnapplicationCandidate Original)> Applications,
    IReadOnlyList<PaymentCorrectionTender> Tenders);

public sealed partial class AccountingService
{
    public async Task<VoucherDraftPreparation?> GetPaymentCorrectionCaptureAsync(AccountingUserIdentity user,
        string kind, Guid paymentId, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (kind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
            paymentId == Guid.Empty)
            throw new AccountingValidationException("Selecciona el pago o recaudo que vas a anular.");
        var payable = kind == AccountingSubledgerKinds.Payable;
        AccountingVoucherFinancialValidation.DemandPaymentPermission(user, payable);
        var payment = await store.GetPaymentCorrectionAsync(user, kind, paymentId, token);
        if (payment is null) return null;
        foreach (var item in payment.Applications) ValidateUnapplicationCandidate(item.Original);
        if (payment.Applications.Count > 100 || payment.Tenders.Count is < 1 or > 100 ||
            payment.Applications.Select(item => item.Original.PartyId).Distinct().Count() != 1 ||
            payment.Tenders.Sum(item => item.Amount) != payment.TotalAmount ||
            payment.Applications.Sum(item => item.Original.AvailableAmount) != payment.TotalAmount)
            throw new AccountingConflictException("Este pago ya tiene una corrección parcial o supera el límite de un comprobante. Corrige sus abonos desde cada obligación.");

        var first = payment.Applications[0].Original;
        var lines = new List<VoucherDraftLineView>();
        var tenderRemainders = payment.Tenders.Select(item => item.Amount).ToArray();
        foreach (var siteGroup in payment.Applications.GroupBy(item => item.Original.PartySiteId))
        {
            var groupId = ids.NewId();
            var groupAmount = siteGroup.Sum(item => item.Original.AvailableAmount);
            foreach (var item in siteGroup)
            {
                var original = item.Original;
                var line = new VoucherDraftLine(original.AccountId, original.PartyId, null,
                    $"Reabrir {original.DocumentNumber}", payable ? 0m : original.AvailableAmount,
                    payable ? original.AvailableAmount : 0m, original.DocumentNumber,
                    ExistingPosition: new(kind, item.PositionId, item.TransactionId),
                    FinancialGroupId: groupId, PartySiteId: siteGroup.Key);
                lines.Add(new(line, original.AccountCode, original.AccountName,
                    original.PartyName, original.PartyIdentification, null));
            }
            for (var index = 0; index < payment.Tenders.Count && groupAmount > 0; index++)
            {
                var amount = decimal.Min(groupAmount, tenderRemainders[index]);
                if (amount == 0) continue;
                var tender = payment.Tenders[index];
                var line = new VoucherDraftLine(tender.AccountId, first.PartyId, null,
                    $"Reversar {tender.MethodCode} registrado",
                    payable ? amount : 0m, payable ? 0m : amount,
                    FinancialGroupId: groupId, PartySiteId: siteGroup.Key);
                lines.Add(new(line, tender.AccountCode, tender.AccountName,
                    first.PartyName, first.PartyIdentification, null));
                tenderRemainders[index] -= amount;
                groupAmount -= amount;
            }
            if (groupAmount != 0) throw new AccountingConflictException("Los medios del pago no cubren todas las obligaciones.");
        }
        return new($"Anulación de {payment.DocumentNumber}", payment.DocumentNumber,
            first.PartyId, first.PartyName, first.PartyIdentification, lines);
    }

    public async Task<VoucherDraftPreparation?> GetUnapplicationCaptureAsync(AccountingUserIdentity user,
        string kind, Guid positionId, Guid transactionId, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (kind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
            positionId == Guid.Empty || transactionId == Guid.Empty)
            throw new AccountingValidationException("Selecciona la obligación y la aplicación original.");
        var payable = kind == AccountingSubledgerKinds.Payable;
        AccountingVoucherFinancialValidation.DemandPaymentPermission(user, payable);
        var original = await store.GetUnapplicationAsync(user, kind, positionId, transactionId, token);
        if (original is null) return null;
        ValidateUnapplicationCandidate(original);
        var amount = original.AvailableAmount;
        var target = new VoucherDraftLine(original.AccountId, original.PartyId, null, "Reabrir obligación",
            payable ? 0m : amount, payable ? amount : 0m, original.DocumentNumber,
            ExistingPosition: new(kind, positionId, transactionId), PartySiteId: original.PartySiteId);
        if (original.RestoredCredit is { } credit)
        {
            if (credit.Issue is not null || credit.AccountId is not { } creditAccountId ||
                creditAccountId == Guid.Empty || string.IsNullOrWhiteSpace(credit.AccountCode) || string.IsNullOrWhiteSpace(credit.AccountName))
                throw new AccountingValidationException(credit.Issue ?? "Concilia la cuenta del saldo a favor original antes de restituirlo.");
            var restored = new VoucherDraftLine(creditAccountId, original.PartyId, null, "Restituir saldo a favor original",
                payable ? amount : 0m, payable ? 0m : amount,
                ExistingPosition: new(payable ? AccountingSubledgerKinds.SupplierCredit : AccountingSubledgerKinds.CustomerCredit, credit.PositionId));
            return new($"Desaplicar cruce de {original.DocumentNumber}", original.DocumentNumber,
                original.PartyId, original.PartyName, original.PartyIdentification,
                [new(target, original.AccountCode, original.AccountName, original.PartyName, original.PartyIdentification, null),
                 new(restored, credit.AccountCode, credit.AccountName, original.PartyName, original.PartyIdentification, null)]);
        }
        var advance = new VoucherDraftLine(null, original.PartyId, null, "Destino del dinero desaplicado",
            payable ? amount : 0m, payable ? 0m : amount, PartySiteId: original.PartySiteId);
        return new($"Desaplicar abono de {original.DocumentNumber}", original.DocumentNumber,
            original.PartyId, original.PartyName, original.PartyIdentification,
            [new(target, original.AccountCode, original.AccountName, original.PartyName, original.PartyIdentification, null),
             new(advance, null, null, original.PartyName, original.PartyIdentification, null)]);
    }

    private static void ValidateUnapplicationCandidate(UnapplicationCandidate original)
    {
        if (original.FinancialDiscountDecided)
            throw new AccountingConflictException("No se desaplicó el pago: tiene una decisión de descuento financiero. El pago y la obligación se conservan.");
        if (original.SourceCancelled)
            throw new AccountingConflictException("No se preparó la desaplicación: el documento de origen fue anulado. El abono y sus saldos se conservan.");
        if (original.CancellationPending)
            throw new AccountingConflictException("No se preparó la desaplicación: la obligación tiene una anulación aceptada pendiente. El abono y sus saldos se conservan.");
        if (original.Issue is not null || original.CurrencyCode != "COP")
            throw new AccountingValidationException(original.Issue ?? "La moneda del movimiento requiere conciliación antes de desaplicarlo.");
        if (original.AccountId is not { } accountId || accountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(original.AccountCode) || string.IsNullOrWhiteSpace(original.AccountName))
            throw new AccountingValidationException("La cuenta original de la obligación requiere conciliación antes de preparar la desaplicación.");
        if (original.AvailableAmount <= 0)
            throw new AccountingConflictException("No se preparó la desaplicación: la aplicación original no tiene importe disponible, incluidas sus desaplicaciones pendientes.");
    }
}
