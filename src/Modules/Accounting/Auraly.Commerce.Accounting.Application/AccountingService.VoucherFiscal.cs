using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Commerce.Accounting.Application;

public sealed partial class AccountingService
{
    public Task<VoucherDraftPreparation> GetSupportCancellationCaptureAsync(AccountingUserIdentity user,
        Guid payableId, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.Read);
        Demand(user, AccountingPermissionCodes.ManualCreate);
        if (payableId == Guid.Empty)
            throw new AccountingValidationException("Selecciona la obligación del soporte original.");
        return store.GetSupportCancellationCaptureAsync(user, payableId, token);
    }

    public Task<VoucherFiscalPreview> PreviewFiscalVoucherAsync(AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, CancellationToken token = default)
    {
        Demand(user, AccountingPermissionCodes.ManualCreate);
        ValidateManualVoucher(user, request, requireFiscalReview: false);
        if (request.PayloadVersion != 4 || request.VoucherTypeCode is not ("SupportDocument" or "SupportDocumentCancellation") ||
            request.FiscalCapture is null || request.FinancialEffects is not null)
            throw new AccountingValidationException("El preview fiscal requiere la captura de un documento soporte manual.");
        return store.PreviewFiscalVoucherAsync(user, request, token);
    }

    private static void ValidateFiscalCapture(AccountingUserIdentity user, int? payloadVersion,
        string? voucherType, IEnumerable<Guid?> sourceLineIds, VoucherFiscalCapture? capture,
        bool requireReview)
    {
        if (capture is null)
        {
            if (requireReview && voucherType is "SupportDocument" or "SupportDocumentCancellation")
                throw new AccountingValidationException("Completa y revisa los datos fiscales del documento soporte antes de contabilizar.");
            return;
        }
        if (payloadVersion != 4 || voucherType is not ("SupportDocument" or "SupportDocumentCancellation"))
            throw new AccountingValidationException("Los datos fiscales corresponden al documento soporte de la captura financiera.");
        if (voucherType == "SupportDocumentCancellation")
        {
            Demand(user, AccountingPermissionCodes.Read);
            if (capture.Acquisitions is not { Count: 0 } || capture.WithholdingBindings is { Count: > 0 } ||
                capture.WithholdingAdjustments is { Count: > 0 })
                throw new AccountingValidationException("La anulación conserva los datos fiscales originales; no admite bases ni retenciones nuevas.");
            ValidateFiscalReviewHash(capture.CalculationHash, requireReview);
            return;
        }
        if (capture.Acquisitions is not { Count: >= 1 and <= 500 } ||
            capture.WithholdingBindings?.Count > 500 || capture.WithholdingAdjustments?.Count > 20 ||
            capture.WithholdingAdjustments?.Any(item => item is null) == true)
            throw new AccountingValidationException("El documento soporte admite hasta 500 bases y 20 ajustes de retenciones.");
        if (capture.WithholdingAdjustments is { Count: > 0 } &&
            !user.Permissions.Contains(TaxationPermissionCodes.ManageWithholdingRules))
            throw new AccountingForbiddenException("No tienes permiso para ajustar retenciones manualmente.");
        var lines = sourceLineIds.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        var acquisitions = new HashSet<Guid>();
        foreach (var item in capture.Acquisitions)
            if (item is null || item.SourceLineId == Guid.Empty || !lines.Contains(item.SourceLineId) ||
                !acquisitions.Add(item.SourceLineId) || item.TaxExclusiveAmount <= 0 || !ValidDraftAmount(item.TaxExclusiveAmount) ||
                item.TaxProfileId == Guid.Empty || item.TaxTreatment is not ("DeductibleInputVat" or "CapitalizedCost") ||
                item.InputVatSourceLineId is { } vatLine && (vatLine == item.SourceLineId || !lines.Contains(vatLine)) ||
                item.WithholdingConceptCode?.Length > 32)
                throw new AccountingValidationException("Revisa las bases fiscales, su tratamiento de IVA y las partidas contables vinculadas.");
        var bindings = new HashSet<(Guid Id, bool Manual)>();
        foreach (var item in capture.WithholdingBindings ?? [])
            if (item is null || !lines.Contains(item.SourceLineId) ||
                item.RuleId.HasValue == item.ManualLineId.HasValue || item.RuleId == Guid.Empty || item.ManualLineId == Guid.Empty ||
                !bindings.Add((item.RuleId ?? item.ManualLineId!.Value, item.ManualLineId.HasValue)))
                throw new AccountingValidationException("Vincula cada retención una sola vez con su partida contable y su regla o identidad manual.");
        ValidateFiscalReviewHash(capture.CalculationHash, requireReview);
    }

    private static void ValidateFiscalReviewHash(string? hash, bool requireReview)
    {
        if (hash is not null && (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character))) ||
            requireReview && hash is null)
            throw new AccountingValidationException("Recalcula y revisa los datos fiscales antes de contabilizar el documento soporte.");
    }
}
