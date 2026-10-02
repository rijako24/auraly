using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Commerce.Accounting.Application;

public sealed partial class AccountingService
{
    public Task<VoucherDraftView?> GetVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.GetVoucherDraftAsync(user, documentId, cancellationToken);
    }

    public Task<VoucherDraftView> SaveVoucherDraftAsync(AccountingUserIdentity user,
        SaveVoucherDraftRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (request.DocumentId == Guid.Empty || request.OccurredAt == default ||
            request.DocumentType is not (AccountingManualDocumentTypes.ManualVoucher or AccountingManualDocumentTypes.AccountAdjustment) ||
            request.Lines is null || request.Lines.Count > 500)
            throw new AccountingValidationException("El comprobante requiere identidad, fecha y hasta 500 partidas.");
        ValidateText(request.Description, 500, "Descripción");
        ValidateText(request.ConceptCode, 40, "Concepto");
        ValidateDraftReference(request.Reference);
        ValidateDraftVersion(request.RowVersion, required: false);
        foreach (var line in request.Lines)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 500 ||
                !ValidDraftAmount(line.Debit) || !ValidDraftAmount(line.Credit) ||
                line.AccountId == Guid.Empty || line.PartyId == Guid.Empty || line.CostCenterId == Guid.Empty)
                throw new AccountingValidationException("Revisa los valores y las dimensiones de las partidas.");
            ValidateDraftReference(line.Reference);
        }
        if (request.DocumentType == AccountingManualDocumentTypes.AccountAdjustment)
        {
            var adjustment = request.Adjustment;
            if (request.Lines.Count != 0 || adjustment is null || adjustment.SubledgerId == Guid.Empty ||
                adjustment.SubledgerKind is not (AccountingSubledgerKinds.Receivable or AccountingSubledgerKinds.Payable) ||
                adjustment.Direction is not (AccountingAdjustmentDirections.Increase or AccountingAdjustmentDirections.Decrease) ||
                !ValidDraftAmount(adjustment.Amount) || adjustment.CounterpartAccountId == Guid.Empty || adjustment.CostCenterId == Guid.Empty)
                throw new AccountingValidationException("El ajuste requiere una obligación y una dirección válidas.");
        }
        else if (request.Adjustment is not null)
            throw new AccountingValidationException("Un comprobante libre no modifica obligaciones de cartera.");

        return store.SaveVoucherDraftAsync(user, request with {
            Description = request.Description.Trim(), ConceptCode = request.ConceptCode.Trim(),
            Reference = Normalize(request.Reference),
            Lines = request.Lines.Select(line => line with {
                Description = line.Description.Trim(), Reference = Normalize(line.Reference) }).ToArray()
        }, cancellationToken);
    }

    public async Task<VoucherDraftView> SendVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, SendVoucherDraftRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualSend);
        ValidateDraftVersion(request.RowVersion, required: true);
        var draft = await store.GetVoucherDraftAsync(user, documentId, cancellationToken)
            ?? throw new AccountingConflictException("El comprobante no existe en esta sede.");
        // A completed command is a read-only replay; it cannot republish effects.
        if (draft.SentAt is not null) return draft;
        if (draft.RowVersion != request.RowVersion)
            throw new AccountingConflictException("El comprobante cambió. Ábrelo nuevamente antes de contabilizar.");
        AccountingManualDocumentAcceptance acceptance;
        if (draft.Adjustment is { } adjustment)
        {
            var command = new ConfirmAccountAdjustmentRequest(documentId, user.BusinessId,
                adjustment.SubledgerKind, adjustment.SubledgerId, adjustment.Direction, adjustment.Amount,
                adjustment.CounterpartAccountId ?? Guid.Empty, adjustment.CostCenterId,
                draft.OccurredAt, draft.ConceptCode, draft.Description);
            ValidateAccountAdjustment(user, command);
            acceptance = await store.ConfirmAccountAdjustmentAsync(user, command, cancellationToken, request.RowVersion);
        }
        else
        {
            var command = new ConfirmManualAccountingVoucherRequest(documentId, user.BusinessId,
                draft.OccurredAt, draft.ConceptCode, draft.Description, draft.Lines.Select(line =>
                    new ManualVoucherLineRequest(line.Value.AccountId ?? Guid.Empty, line.Value.PartyId,
                        line.Value.CostCenterId, line.Value.Description, line.Value.Debit, line.Value.Credit)).ToArray());
            ValidateManualVoucher(user, command);
            acceptance = await store.ConfirmManualVoucherAsync(user, command, cancellationToken, request.RowVersion);
        }
        if (!acceptance.IsDuplicate)
            await processing.RequestPostingAsync(user.BusinessId, acceptance.DocumentId, acceptance.DocumentType, cancellationToken);
        return (await store.GetVoucherDraftAsync(user, documentId, cancellationToken))!;
    }

    private static bool ValidDraftAmount(decimal value) => value >= 0 && value <= 999_999_999_999m && decimal.Round(value, 4) == value;

    private static void ValidateDraftReference(string? value)
    {
        if (value?.Length > 100) throw new AccountingValidationException("La referencia admite hasta 100 caracteres.");
    }

    private static void ValidateDraftVersion(string? version, bool required)
    {
        if (version is null && !required) return;
        Span<byte> bytes = stackalloc byte[8];
        if (version is null || !Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
            throw new AccountingValidationException("La versión del comprobante no es válida.");
    }
}
