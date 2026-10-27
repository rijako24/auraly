using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Commerce.Accounting.Application;

public sealed record UnapplicationCreditCandidate(Guid PositionId, Guid? AccountId, string? AccountCode,
    string? AccountName, string? Issue);

public sealed record UnapplicationCandidate(Guid? AccountId, string? AccountCode, string? AccountName,
    Guid PartyId, string PartyName, string? PartyIdentification, Guid? PartySiteId,
    string DocumentNumber, string CurrencyCode, decimal AvailableAmount, string? Issue,
    UnapplicationCreditCandidate? RestoredCredit = null, bool CancellationPending = false, bool SourceCancelled = false,
    bool FinancialDiscountDecided = false);

public sealed partial class AccountingService
{
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
        var amount = original.AvailableAmount;
        var target = new VoucherDraftLine(accountId, original.PartyId, null, "Reabrir obligación",
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
}
