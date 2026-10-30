using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Commerce.Accounting.Application;

public sealed partial class AccountingService
{
    public async Task<VoucherDraftView> CloneVoucherDraftAsync(AccountingUserIdentity user, Guid sourceDocumentId,
        CloneVoucherDraftRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (sourceDocumentId == Guid.Empty || request.DocumentId == Guid.Empty || sourceDocumentId == request.DocumentId || request.OccurredAt == default)
            throw new AccountingValidationException("La copia requiere un identificador nuevo y fecha contable.");
        var existing = await store.GetVoucherDraftAsync(user, request.DocumentId, cancellationToken);
        if (existing is not null)
            return existing.ClonedFromDocumentId == sourceDocumentId ? existing
                : throw new AccountingConflictException("El identificador ya pertenece a otro comprobante.");
        var source = await store.GetVoucherDraftAsync(user, sourceDocumentId, cancellationToken);
        var entry = source is null || source.Adjustment is not null
            ? await store.GetEntryAsync(user, sourceDocumentId, cancellationToken) : null;
        if (source is null && entry is null || source?.Adjustment is not null && entry is null)
            throw new AccountingConflictException("No hay partidas de un comprobante disponible para copiar en esta sede.");
        var lines = entry is not null
            ? entry.Lines.Select(line => new VoucherDraftLine(line.AccountId, line.PartyId, line.CostCenterId,
                line.Description, line.Debit, line.Credit, LineId: ids.NewId(), BaseAmount: line.BaseAmount)).ToArray()
            : source!.Lines.Select(line => line.Value with { Reference = null, LineId = ids.NewId(),
                ExistingPosition = null, FinancialGroupId = null, DueDate = null, PaymentEvidence = null,
                FinancialDiscountEarned = null }).ToArray();
        var copy = new SaveVoucherDraftRequest(request.DocumentId, AccountingManualDocumentTypes.ManualVoucher,
            request.OccurredAt, source?.Adjustment is null && source is not null ? source.ConceptCode : "MANUAL_VOUCHER",
            source?.Description ?? entry!.Description, null, lines, null, null, source?.MainPartyId ?? entry?.MainPartyId, sourceDocumentId,
            PayloadVersion: source?.PayloadVersion == 4 ? 4 : 2,
            VoucherTypeCode: source?.PayloadVersion == 4
                ? string.Equals(source.VoucherTypeCode,"Disbursement",StringComparison.OrdinalIgnoreCase) ? "Disbursement" : "Accounting" : null);
        try { return await SaveVoucherDraftAsync(user, copy, cancellationToken); }
        catch (AccountingConflictException)
        {
            // Two identical clone commands can race before canonical save locks the new ID.
            var raced = await store.GetVoucherDraftAsync(user, request.DocumentId, cancellationToken);
            if (raced?.ClonedFromDocumentId == sourceDocumentId) return raced;
            throw;
        }
    }

    public Task<VoucherDraftView?> GetVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        return store.GetVoucherDraftAsync(user, documentId, cancellationToken);
    }

    public Task<bool> DeleteVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, string rowVersion, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (documentId == Guid.Empty)
            throw new AccountingValidationException("Selecciona el borrador que vas a descartar.");
        ValidateDraftVersion(rowVersion, required: true);
        return store.DeleteVoucherDraftAsync(user, documentId, rowVersion, cancellationToken);
    }

    public Task<VoucherDraftView> SaveVoucherDraftAsync(AccountingUserIdentity user,
        SaveVoucherDraftRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (request.PayloadVersion != 4 && (request.VoucherTypeCode is not null || request.WorkSessionId is not null || request.FiscalCapture is not null ||
            request.Lines?.Any(line => line is not null && (line.ExistingPosition is not null || line.FinancialGroupId is not null ||
                line.DueDate is not null || line.PartySiteId is not null || line.PaymentEvidence is not null ||
                line.FinancialDiscountEarned is not null)) == true))
            throw new AccountingValidationException("Los vínculos y datos financieros requieren la versión financiera del comprobante.");
        if (request.ClonedFromDocumentId is not null) Demand(user, AccountingPermissionCodes.Read);
        if (request.DocumentId == Guid.Empty || request.OccurredAt == default || request.MainPartyId == Guid.Empty ||
            request.ClonedFromDocumentId == Guid.Empty || request.ClonedFromDocumentId == request.DocumentId ||
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
                line.BaseAmount is { } recordedBase && !ValidDraftAmount(decimal.Abs(recordedBase)) ||
                line.AccountId == Guid.Empty || line.PartyId == Guid.Empty || line.CostCenterId == Guid.Empty)
                throw new AccountingValidationException("Revisa los valores y las dimensiones de las partidas.");
            ValidateDraftReference(line.Reference);
            if (request.PayloadVersion == 4)
            {
                if (line.FinancialDiscountEarned is not null &&
                    (line.ExistingPosition is not { SubledgerKind: AccountingSubledgerKinds.Payable, ReversesTransactionId: null } ||
                     line.FinancialGroupId is null))
                    throw new AccountingValidationException("La decisión de descuento financiero requiere una obligación por pagar vinculada a un grupo de egreso.");
                if (line.FinancialGroupId == Guid.Empty || line.PartySiteId == Guid.Empty ||
                    line.ExistingPosition is { } position && (position.SubledgerId == Guid.Empty || position.ReversesTransactionId == Guid.Empty ||
                        position.ReversesTransactionId is not null && position.SubledgerKind is not ("Payable" or "Receivable") ||
                        position.SubledgerKind is not ("Payable" or "Receivable" or "SupplierCredit" or "CustomerCredit")))
                    throw new AccountingValidationException("Revisa la identidad del grupo y la posición vinculada de la partida.");
                if (line.PaymentEvidence is { } evidence && (evidence.CardFranchiseCode?.Length > 64 ||
                    evidence.ApprovalNumber?.Length > 100 || evidence.Notes?.Length > 500 ||
                    evidence.TenderedAmount is { } tendered && (tendered <= 0 || !ValidDraftAmount(tendered))))
                    throw new AccountingValidationException("Revisa la evidencia del medio y el valor entregado de la partida.");
            }
        }
        if (request.PayloadVersion == 4)
        {
            if (string.IsNullOrWhiteSpace(request.VoucherTypeCode) || request.VoucherTypeCode.Length > 64 || request.WorkSessionId == Guid.Empty)
                throw new AccountingValidationException("Selecciona el tipo de comprobante y una sesión válida cuando corresponda.");
            try { Auraly.Commerce.Accounting.Domain.AccountingJournal.ValidateIdentifiedSourceLines(request.Lines.Select(line => line.LineId)); }
            catch (Auraly.Commerce.Accounting.Domain.AccountingRuleException error)
            { throw new AccountingValidationException("Cada partida financiera requiere una identidad única y no vacía.", error); }
        }
        else ValidateManualLineIdentities(request.PayloadVersion, request.Lines.Select(line => line.LineId));
        ValidateFiscalCapture(user, request.PayloadVersion, request.VoucherTypeCode,
            request.Lines.Select(line => line.LineId), request.FiscalCapture, requireReview: false);
        if (request.DocumentType != AccountingManualDocumentTypes.ManualVoucher && request.PayloadVersion is not null)
            throw new AccountingValidationException("La versión de partidas identificadas corresponde al comprobante contable.");
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
            Reference = Normalize(request.Reference), VoucherTypeCode = Normalize(request.VoucherTypeCode),
            Lines = request.Lines.Select(line => line with {
                Description = line.Description.Trim(), Reference = Normalize(line.Reference),
                PaymentEvidence = NormalizeVoucherPaymentEvidence(line.PaymentEvidence) }).ToArray()
        }, cancellationToken);
    }

    private static VoucherPaymentEvidence? NormalizeVoucherPaymentEvidence(VoucherPaymentEvidence? value)
    {
        if (value is null) return null;
        var result=value with { CardFranchiseCode=Normalize(value.CardFranchiseCode),ApprovalNumber=Normalize(value.ApprovalNumber),Notes=Normalize(value.Notes) };
        return result.CardFranchiseCode is null && result.ApprovalNumber is null && result.Notes is null && result.TenderedAmount is null ? null : result;
    }

    public async Task<VoucherDraftView> SendVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, SendVoucherDraftRequest request, CancellationToken cancellationToken = default)
    {
        Demand(user, AccountingPermissionCodes.ManualSend);
        ValidateDraftVersion(request.RowVersion, required: true);
        var draft = await store.GetVoucherDraftAsync(user, documentId, cancellationToken)
            ?? throw new AccountingConflictException("El comprobante no existe en esta sede.");
        // Accepted replays pass through the same source/job integrity owner.
        // They never revalidate mutable dimensions or republish financial work.
        if (draft.SentAt is null && draft.RowVersion != request.RowVersion)
            throw new AccountingConflictException("El comprobante cambió. Ábrelo nuevamente antes de contabilizar.");
        var reference = draft.SentAt is null ? draft.Reference : draft.AcceptedReference;
        AccountingManualDocumentAcceptance acceptance;
        if (draft.Adjustment is { } adjustment)
        {
            var command = new ConfirmAccountAdjustmentRequest(documentId, user.BusinessId,
                adjustment.SubledgerKind, adjustment.SubledgerId, adjustment.Direction, adjustment.Amount,
                adjustment.CounterpartAccountId ?? Guid.Empty, adjustment.CostCenterId,
                draft.OccurredAt, draft.ConceptCode, draft.Description, reference);
            ValidateAccountAdjustment(user, command);
            acceptance = await store.ConfirmAccountAdjustmentAsync(user, command, cancellationToken, request.RowVersion);
        }
        else
        {
            var command = new ConfirmManualAccountingVoucherRequest(documentId, user.BusinessId,
                draft.OccurredAt, draft.ConceptCode, draft.Description, draft.Lines.Select(line =>
                    new ManualVoucherLineRequest(line.Value.AccountId ?? Guid.Empty, line.Value.PartyId,
                        line.Value.CostCenterId, line.Value.Description, line.Value.Debit, line.Value.Credit,
                        line.Value.LineId, line.Value.BaseAmount,
                        draft.PayloadVersion == 4 ? line.Value.ExistingPosition : null,
                        draft.PayloadVersion == 4 ? line.Value.FinancialGroupId : null,
                        draft.PayloadVersion == 4 ? line.Value.DueDate : null,
                        draft.PayloadVersion == 4 ? line.Value.PartySiteId : null,
                        draft.PayloadVersion == 4 ? line.Value.PaymentEvidence : null,
                        draft.PayloadVersion == 4 ? line.Value.Reference : null,
                        draft.PayloadVersion == 4 ? line.Value.FinancialDiscountEarned : null)).ToArray(),
                draft.MainPartyId, draft.PayloadVersion,
                reference, draft.PayloadVersion == 4 ? draft.VoucherTypeCode : null,
                draft.PayloadVersion == 4 ? draft.WorkSessionId : null, draft.PayloadVersion == 4 ? draft.CurrencyCode : null,
                FiscalCapture: draft.FiscalCapture);
            ValidateManualVoucher(user, command);
            acceptance = await store.ConfirmManualVoucherAsync(user, command, cancellationToken, request.RowVersion);
        }
        if (!acceptance.IsDuplicate)
        {
            await processing.RequestPostingAsync(user.BusinessId, acceptance.DocumentId,
                acceptance.DocumentType, cancellationToken);
        }
        if (acceptance.IsDuplicate && acceptance.Status == draft.Status) return draft;
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
