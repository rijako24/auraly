using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.WorkSessions;

namespace Auraly.Commerce.Accounting.Application;

public sealed record VoucherFinancialAccount(Guid AccountId, string Function, string? MethodCode, Guid? BankAccountId,
    string? AccountType = null, string? AccountCode = null);

public sealed record VoucherFinancialPosition(string Kind, Guid PositionId, Guid? AccountId, Guid PartyId,
    Guid? RoleId, Guid? PartySiteId, string? CurrencyCode, DateTimeOffset OccurredAt, string? OpeningIssue,
    decimal? FinancialDiscountEligibleAmount = null, bool? FinancialDiscountAnticipated = null,
    bool FinancialDiscountDecided = false, bool FinancialDiscountPendingReturn = false);

public sealed record VoucherFinancialContext(
    IReadOnlyList<VoucherFinancialAccount> Accounts, IReadOnlyList<VoucherFinancialPosition> Positions,
    IReadOnlyList<VoucherFinancialParty> Parties,
    Guid? SupplierDiscountEarnedAccountId = null, Guid? SupplierDiscountLostAccountId = null,
    IReadOnlyList<VoucherFinancialConceptDefinition>? Concepts=null);

public sealed record VoucherFinancialConceptDefinition(Guid FinancialConceptId,string Code,string Name);

public sealed record VoucherFinancialParty(Guid PartyId, Guid? SupplierId, Guid? DefaultPartySiteId,
    Guid? CustomerId, bool? CustomerIsActive)
{
    public Guid? ResolveSite(Guid? selectedSite) => selectedSite ?? DefaultPartySiteId;
}

// Deterministic normalization of an already validated capture. It neither loads
// data nor accepts/applies payments. Financial shape rules remain in the existing
// settlement and tender domain owners used by assisted capture.
public static partial class AccountingVoucherFinancialValidation
{
    // The wire field is retained on immutable sources; new captures bind it to
    // an existing document/line identity, never to another generated entity.
    public static SaveVoucherDraftRequest BindSourceReferences(SaveVoucherDraftRequest request)
    {
        if (request.PayloadVersion != 4) return request;
        var references = SourceReferences(request.DocumentId,
            request.Lines.Select(line => (line.LineId, line.FinancialGroupId)));
        return request with { Lines = request.Lines.Select(line => line with {
            FinancialGroupId = line.FinancialGroupId is { } id ? references[id] : null }).ToArray() };
    }

    public static ConfirmManualAccountingVoucherRequest BindSourceReferences(ConfirmManualAccountingVoucherRequest request)
    {
        if (request.PayloadVersion != 4) return request;
        var references = SourceReferences(request.VoucherId,
            request.Lines.Select(line => (line.LineId, line.FinancialGroupId)));
        return request with { Lines = request.Lines.Select(line => line with {
            FinancialGroupId = line.FinancialGroupId is { } id ? references[id] : null }).ToArray() };
    }

    private static Dictionary<Guid, Guid> SourceReferences(Guid documentId,
        IEnumerable<(Guid? LineId, Guid? Reference)> source)
    {
        var lines = source.ToArray();
        var identities = lines.Where(line => line.LineId is not null).ToDictionary(line => line.LineId!.Value);
        var references = lines.Where(line => line.Reference is not null).GroupBy(line => line.Reference!.Value).ToArray();
        var result = new Dictionary<Guid, Guid>();
        foreach (var reference in references)
        {
            if (reference.Key == Guid.Empty)
                throw new AccountingValidationException("El vínculo financiero no puede tener una identidad vacía.");
            if (reference.Key == documentId) result.Add(reference.Key, documentId);
            else if (identities.TryGetValue(reference.Key, out var target))
            {
                if (target.Reference != reference.Key)
                    throw new AccountingValidationException("El vínculo financiero debe corresponder a su partida de origen.");
                result.Add(reference.Key, reference.Key);
            }
            else
            {
                var anchor = reference.FirstOrDefault(line => line.LineId is not null).LineId
                    ?? throw new AccountingValidationException("El vínculo financiero requiere una partida identificada.");
                result.Add(reference.Key, references.Length == 1 ? documentId : anchor);
            }
        }
        return result;
    }

    public static VoucherFinancialEffects Normalize(AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, VoucherFinancialContext context, IAuralyIdGenerator ids,
        VoucherFiscalPreview? fiscalProof = null)
    {
        if (request.PayloadVersion != 4 || request.CurrencyCode != "COP" || request.FinancialEffects is not null)
            throw new AccountingValidationException("La captura financiera requiere COP y efectos calculados por el servidor.");
        if(request.VoucherTypeCode is not("Accounting" or "CashReceipt" or "Disbursement")&&request.Lines.Any(line=>line.FinancialConceptId is not null))
            throw new AccountingValidationException("Los conceptos financieros se seleccionan en recibos, egresos y comprobantes contables.");
        if (request.VoucherTypeCode == "SupportDocumentCancellation")
            return NormalizeFiscalCancellation(user, request, context, ids, fiscalProof);
        if (request.VoucherTypeCode is not ("Accounting" or "Disbursement" or "CashReceipt" or "SupportDocument") ||
            request.VoucherTypeCode == "SupportDocument" && (fiscalProof is null || !fiscalProof.CanConfirm || request.FiscalCapture is null))
            throw new AccountingValidationException("El documento soporte requiere completar su información fiscal antes de enviarlo.");

        var accounts = context.Accounts.ToDictionary(item => item.AccountId);
        var positions = context.Positions.ToDictionary(item => (item.Kind, item.PositionId));
        var parties = context.Parties.ToDictionary(item => item.PartyId);
        var conceptDefinitions=(context.Concepts??[]).ToDictionary(item=>item.FinancialConceptId);
        var selectedConcepts=new List<VoucherFinancialConcept>();
        foreach(var line in request.Lines.Where(item=>item.FinancialConceptId is not null))
        {
            if(line.FinancialConceptId==Guid.Empty||!conceptDefinitions.TryGetValue(line.FinancialConceptId!.Value,out var concept)||
                line.ExistingPosition is not null||line.PaymentEvidence is not null)
                throw new AccountingValidationException("El concepto financiero debe corresponder a una contrapartida o anticipo disponible.");
            selectedConcepts.Add(new(concept.FinancialConceptId,concept.Code,concept.Name));
        }
        IReadOnlyList<VoucherFinancialConcept>? conceptEvidence=selectedConcepts.Count==0?null:selectedConcepts.DistinctBy(item=>item.FinancialConceptId).ToArray();
        if (request.VoucherTypeCode == "CashReceipt")
        {
            ValidateCashReceipt(request, accounts, parties);
        }
        var openings = new List<VoucherFinancialPositionEffect>();
        var lines = request.Lines;
        if (lines.Where(line => line.FinancialDiscountEarned is not null)
            .GroupBy(line => line.ExistingPosition?.SubledgerId)
            .Any(group => group.Key is not null && group.Count() > 1))
            throw new AccountingValidationException("No se registró el egreso ni se pagó: decide el descuento financiero una sola vez por obligación.");
        var reversalGroups = lines.Where(line => line.ExistingPosition?.ReversesTransactionId is not null)
            .Select(line => line.FinancialGroupId).ToHashSet();
        foreach (var line in lines)
        {
            ValidatePaymentFormEvidence(line.PaymentEvidence, line.AccountId);
            if (!accounts.TryGetValue(line.AccountId, out var account))
                throw new AccountingValidationException("Una partida no tiene un auxiliar disponible para esta sede.");
            if (account.Function == "Unclassified" && line.ExistingPosition is null)
                throw new AccountingValidationException("Configura la función financiera del auxiliar antes de enviar el comprobante. No se infiere por su código o nombre.");
            if (line.PaymentEvidence is not null && !IsTreasury(account.Function))
                throw new AccountingValidationException("La evidencia de pago corresponde a las partidas de tesorería.");
            if (line.FinancialDiscountEarned is not null &&
                (request.VoucherTypeCode != "Disbursement" || line.ExistingPosition is not
                    { SubledgerKind: AccountingSubledgerKinds.Payable, ReversesTransactionId: null } ||
                 line.FinancialGroupId is null))
                throw new AccountingValidationException("No se registró el egreso ni se pagó: la decisión del descuento financiero requiere una obligación vinculada.");
            if (line.ExistingPosition is { } target)
            {
                if (!positions.TryGetValue((target.SubledgerKind, target.SubledgerId), out var position))
                    throw new AccountingValidationException("La obligación seleccionada no pertenece a esta sede.");
                if (position.OpeningIssue is not null)
                    throw new AccountingValidationException($"No se aplicó el abono: {position.OpeningIssue}");
                if (position.AccountId != line.AccountId || position.PartyId != line.PartyId ||
                    position.CurrencyCode != request.CurrencyCode ||
                    position.OccurredAt.ToOffset(request.OccurredAt.Offset).Date > request.OccurredAt.Date ||
                    line.PartySiteId is { } site && position.PartySiteId != site)
                    throw new AccountingValidationException("La partida no corresponde a la cuenta, tercero, sede, moneda o fecha de la obligación.");
                if (target.ReversesTransactionId is not null)
                {
                    if (target.ReversesTransactionId == Guid.Empty ||
                        target.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
                        target.SubledgerKind == AccountingSubledgerKinds.Payable && (line.Credit <= 0 || line.Debit != 0) ||
                        target.SubledgerKind == AccountingSubledgerKinds.Receivable && (line.Debit <= 0 || line.Credit != 0))
                        throw new AccountingValidationException("La desaplicación debe reabrir la obligación e identificar el abono original.");
                }
                else if (target.SubledgerKind == AccountingSubledgerKinds.Payable && (line.Debit <= 0 || line.Credit != 0) ||
                    target.SubledgerKind == AccountingSubledgerKinds.Receivable && (line.Credit <= 0 || line.Debit != 0) ||
                    target.SubledgerKind == AccountingSubledgerKinds.SupplierCredit && (line.Credit <= 0 || line.Debit != 0) &&
                        !(reversalGroups.Contains(line.FinancialGroupId) && line.Debit > 0 && line.Credit == 0) ||
                    target.SubledgerKind == AccountingSubledgerKinds.CustomerCredit && (line.Debit <= 0 || line.Credit != 0) &&
                        !(reversalGroups.Contains(line.FinancialGroupId) && line.Credit > 0 && line.Debit == 0))
                    throw new AccountingValidationException("El abono debe reducir la cuenta de la obligación seleccionada.");
            }
            else if (account.Function == "Payable" && line.Credit > 0 && line.Debit == 0 ||
                account.Function == "Receivable" && line.Debit > 0 && line.Credit == 0)
            {
                var payable = account.Function == "Payable";
                if (request.VoucherTypeCode is not ("Accounting" or "CashReceipt" or "SupportDocument") || line.PartyId is not { } partyId ||
                    !parties.TryGetValue(partyId, out var party))
                    throw new AccountingValidationException("Para registrar la obligación selecciona un tercero activo en un comprobante contable.");
                var roleId = payable ? party.SupplierId : party.CustomerId;
                var siteId = roleId is not null ? party.ResolveSite(line.PartySiteId) : null;
                if (payable && roleId is null || roleId is not null && siteId is null ||
                    !payable && party.CustomerIsActive == false || roleId is null && line.PartySiteId is not null)
                    throw new AccountingValidationException("La obligación requiere su rol activo y sede; un tercero sin rol de cliente se registra sin sede comercial.");
                if (line.DueDate is not { } dueDate || dueDate == default)
                    throw new AccountingValidationException("La nueva obligación requiere su fecha real de vencimiento.");
                var reference = line.Reference ?? request.Reference;
                var referenceLimit = payable ? 80 : 64;
                if (string.IsNullOrWhiteSpace(reference) || reference.Length > referenceLimit)
                    throw new AccountingValidationException($"La nueva obligación requiere una referencia documental de hasta {referenceLimit} caracteres.");
                openings.Add(new(ids.NewId(), line.FinancialGroupId ?? line.LineId!.Value, "Open",
                    payable ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable,
                    line.LineId!.Value, ids.NewId(), line.AccountId, partyId, roleId, siteId,
                    payable ? line.Credit : line.Debit, dueDate, reference));
            }
            else if (account.Function == "SupplierAdvance" && line.Debit > 0 && line.Credit == 0 ||
                account.Function == "CustomerAdvance" && line.Credit > 0 && line.Debit == 0)
            {
                var supplier = account.Function == "SupplierAdvance";
                if (line.PartyId is not { } partyId || !parties.TryGetValue(partyId, out var party))
                    throw new AccountingValidationException("Selecciona el tercero del anticipo.");
                var roleId = supplier ? party.SupplierId : party.CustomerId;
                var siteId = roleId is not null ? party.ResolveSite(line.PartySiteId) : null;
                if (supplier && roleId is null || roleId is not null && siteId is null || !supplier && party.CustomerIsActive == false)
                    throw new AccountingValidationException("El tercero del anticipo requiere su rol activo y sede principal.");
                if (line.DueDate is not null)
                    throw new AccountingValidationException("Un anticipo no abre una factura por pagar o cobrar ni requiere vencimiento.");
                openings.Add(new(ids.NewId(), line.FinancialGroupId ?? line.LineId!.Value, "Open",
                    supplier ? "SupplierCredit" : "CustomerCredit", line.LineId!.Value, ids.NewId(),
                    line.AccountId, partyId, roleId, siteId, supplier ? line.Debit : line.Credit, null));
            }
            else if (account.Function is "Payable" or "Receivable" or "SupplierAdvance" or "CustomerAdvance")
                throw new AccountingValidationException("La partida de cartera o anticipo requiere su destino financiero antes de enviar.");
        }

        openings.AddRange(NormalizeConceptAdjustments(request, accounts, positions, ids));
        var adjustmentAmounts = openings.Where(item => item.EffectKind == "Adjustment")
            .GroupBy(item => item.TargetSourceLineId!.Value).ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var advances = openings.Where(item => item.SubledgerKind is "SupplierCredit" or "CustomerCredit")
            .ToDictionary(item => item.SourceLineId);
        var financial = lines.Where(line => line.ExistingPosition is not null || advances.ContainsKey(line.LineId!.Value) ||
            IsTreasury(accounts[line.AccountId].Function)).ToArray();
        if (lines.Any(line => line.PaymentEvidence?.PaymentMethodId is not null))
        {
            var money = financial.Where(line => IsTreasury(accounts[line.AccountId].Function))
                .Select(line => NormalizeTender(line, accounts[line.AccountId], line.Credit > 0)).ToArray();
            try
            {
                PaymentTenderBreakdown.Create(money.Select(AsPaymentTender), money.Sum(item => item.Amount), 1, PortfolioPaymentMethods.Supported, requireEffectiveCash: true);
            }
            catch (ArgumentException error)
            {
                throw new AccountingValidationException($"Revisa las formas de pago del comprobante: {error.Message}", error);
            }
        }
        if (financial.Length == 0)
        {
            if (request.VoucherTypeCode == "Disbursement")
                throw new AccountingValidationException("No se registró el egreso: selecciona un auxiliar de efectivo, banco o tarjeta para la salida.");
            var openingLines = openings.Select(item => item.SourceLineId).ToHashSet();
            if (lines.Any(line => !openingLines.Contains(line.LineId!.Value) &&
                (line.FinancialGroupId is not null || line.PaymentEvidence is not null)))
                throw new AccountingValidationException("El vínculo o la evidencia de pago no corresponde a una partida financiera.");
            return new(user.UserId, [], openings, [], [],Concepts:conceptEvidence);
        }

        // Debt groups identify one party. Pure treasury groups identify their
        // balanced journal counterparts instead; they do not create a debt.
        // Never manufacture groups per invoice to evade the shared tender rule.
        var explicitGroups = financial.Any(line => line.FinancialGroupId is not null);
        if (explicitGroups && financial.Any(line => line.FinancialGroupId is null))
            throw new AccountingValidationException("Vincula las partidas de dinero y cartera al comprobante o a su partida de origen.");
        var financialParties = financial.Where(line => !IsTreasury(accounts[line.AccountId].Function))
            .Select(line => line.PartyId).Distinct().ToArray();
        if (!explicitGroups && financialParties.Length > 1)
            throw new AccountingValidationException("Identifica la partida de origen de cada pago cuando intervienen varios terceros.");
        var implicitId = request.VoucherId;
        var groups = financial.GroupBy(line => line.FinancialGroupId ?? implicitId);
        var payments = new List<VoucherFinancialPaymentGroup>();
        var creditApplications = new List<VoucherFinancialCreditApplication>();
        var unapplications = new List<VoucherFinancialUnapplication>();
        var directMovements = new List<VoucherFinancialMoneyMovement>();
        var directGroups = new HashSet<Guid>();
        var paymentCounterpartIds = new HashSet<Guid>();
        var openingIds = openings.Select(item => item.SourceLineId).ToHashSet();
        var financialIds = financial.Select(item => item.LineId!.Value).ToHashSet();
        var directCounterparts = lines.ToLookup(line => line.FinancialGroupId ?? implicitId);
        foreach (var group in groups)
        {
            var applications = group.Where(line => line.ExistingPosition is not null).ToArray();
            var groupAdvances = group.Where(line => advances.ContainsKey(line.LineId!.Value))
                .Select(line => advances[line.LineId!.Value]).ToArray();
            var tenders = group.Where(line => IsTreasury(accounts[line.AccountId].Function)).ToArray();
            if (applications.Any(line => line.ExistingPosition!.ReversesTransactionId is not null))
            {
                var originals = applications.Where(line => line.ExistingPosition!.ReversesTransactionId is not null).ToArray();
                var restoredCredits = applications.Where(line => line.ExistingPosition is { ReversesTransactionId: null,
                    SubledgerKind: AccountingSubledgerKinds.SupplierCredit or AccountingSubledgerKinds.CustomerCredit }).ToArray();
                if (tenders.Length > 0)
                {
                    var returnKind = originals[0].ExistingPosition!.SubledgerKind;
                    var returnPayable = returnKind == AccountingSubledgerKinds.Payable;
                    var origin = positions[(returnKind, originals[0].ExistingPosition!.SubledgerId)];
                    DemandPaymentPermission(user, returnPayable);
                    if (request.WorkSessionId is not null && !user.Permissions.Contains(WorkSessionPermissionCodes.ManageCash))
                        throw new AccountingForbiddenException("No tienes permiso para registrar movimientos directos en la sesión de trabajo.");
                    if (request.VoucherTypeCode is not ("Accounting" or "CashReceipt") || originals.Length is < 1 or > 100 ||
                        originals.Length + tenders.Length != group.Count() || openings.Any(item => item.GroupId == group.Key) ||
                        originals.Any(line => line.ExistingPosition!.SubledgerKind != returnKind || line.PartyId != origin.PartyId ||
                            positions[(returnKind, line.ExistingPosition.SubledgerId)].RoleId != origin.RoleId ||
                            positions[(returnKind, line.ExistingPosition.SubledgerId)].PartySiteId != origin.PartySiteId) ||
                        tenders.Any(line => line.PartyId is { } partyId && partyId != origin.PartyId ||
                            returnPayable && (line.Debit <= 0 || line.Credit != 0) ||
                            !returnPayable && (line.Credit <= 0 || line.Debit != 0)))
                        throw new AccountingValidationException("La devolución requiere los abonos originales del mismo tercero y sede, y el dinero realmente devuelto, sin anticipos ni obligaciones nuevas en esa operación.");
                    try
                    {
                        var amount = originals.Sum(line => returnPayable ? line.Credit : line.Debit);
                        var returnedTenders = tenders.Select(line => NormalizeTender(line, accounts[line.AccountId], !returnPayable)).ToArray();
                        PaymentTenderBreakdown.Create(returnedTenders.Select(AsPaymentTender),
                            amount, originals.Select(line => line.ExistingPosition!.SubledgerId).Distinct().Count(), PortfolioPaymentMethods.Supported, requireEffectiveCash: true);
                        unapplications.AddRange(originals.Select(line => new VoucherFinancialUnapplication(ids.NewId(), group.Key,
                            returnKind, line.ExistingPosition!.SubledgerId, line.ExistingPosition.ReversesTransactionId!.Value,
                            line.LineId!.Value, null, null, returnPayable ? line.Credit : line.Debit, "PaymentReturn")));
                        directMovements.AddRange(returnedTenders.Select(item => new VoucherFinancialMoneyMovement(group.Key,
                            item.SourceLineId, item.AccountId, item.MethodCode, item.BankAccountId,
                            returnPayable ? item.Amount : -item.Amount, item.Reference, ids.NewId())));
                    }
                    catch (ArgumentException error)
                    {
                        throw new AccountingValidationException($"Revisa el dinero devuelto y sus medios de pago: {error.Message}", error);
                    }
                    continue;
                }
                if (restoredCredits.Length == 1 && groupAdvances.Length == 0)
                {
                    var creditLine = restoredCredits[0];
                    var supplier = creditLine.ExistingPosition!.SubledgerKind == AccountingSubledgerKinds.SupplierCredit;
                    var debtKind = supplier ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable;
                    var credit = positions[(creditLine.ExistingPosition.SubledgerKind, creditLine.ExistingPosition.SubledgerId)];
                    DemandPaymentPermission(user, supplier);
                    if (request.VoucherTypeCode != "Accounting" || tenders.Length != 0 || originals.Length is < 1 or > 100 ||
                        originals.Length + 1 != group.Count() || openings.Any(opening => opening.GroupId == group.Key) ||
                        originals.Any(line => line.ExistingPosition!.SubledgerKind != debtKind || line.PartyId != credit.PartyId ||
                            supplier && positions[(debtKind, line.ExistingPosition.SubledgerId)].PartySiteId != credit.PartySiteId) ||
                        originals.Sum(line => supplier ? line.Credit : line.Debit) != (supplier ? creditLine.Debit : creditLine.Credit) ||
                        (supplier ? creditLine.Credit : creditLine.Debit) != 0)
                        throw new AccountingValidationException("Desaplicar el cruce requiere sus movimientos originales y restituir el mismo saldo a favor, sin dinero ni anticipos nuevos.");
                    unapplications.AddRange(originals.Select(line => new VoucherFinancialUnapplication(ids.NewId(), group.Key,
                        debtKind, line.ExistingPosition!.SubledgerId, line.ExistingPosition.ReversesTransactionId!.Value,
                        line.LineId!.Value, creditLine.LineId!.Value, credit.PositionId, supplier ? line.Credit : line.Debit,
                        "CreditApplication")));
                    continue;
                }
                if (request.VoucherTypeCode != "Accounting" || tenders.Length != 0 || groupAdvances.Length != 1 ||
                    applications.Length is < 1 or > 100 || applications.Length + 1 != group.Count() ||
                    applications.Any(line => line.ExistingPosition!.ReversesTransactionId is null))
                    throw new AccountingValidationException("Desaplicar requiere un comprobante contable con los abonos originales y su anticipo, sin otra salida de dinero.");
                var advance = groupAdvances[0];
                var unapplyPayable = advance.SubledgerKind == AccountingSubledgerKinds.SupplierCredit;
                var unapplyKind = unapplyPayable ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable;
                DemandPaymentPermission(user, unapplyPayable);
                if (applications.Any(line => line.ExistingPosition!.SubledgerKind != unapplyKind || line.PartyId != advance.PartyId ||
                    positions[(unapplyKind, line.ExistingPosition.SubledgerId)].RoleId != advance.CounterpartyRoleId ||
                    positions[(unapplyKind, line.ExistingPosition.SubledgerId)].PartySiteId != advance.PartySiteId) ||
                    applications.Sum(line => unapplyPayable ? line.Credit : line.Debit) != advance.Amount)
                    throw new AccountingValidationException("La obligación y el anticipo deben coincidir en tercero, sede, dirección e importe.");
                openings[openings.IndexOf(advance)] = advance with { GroupId = group.Key };
                unapplications.AddRange(applications.Select(line => new VoucherFinancialUnapplication(ids.NewId(), group.Key,
                    unapplyKind, line.ExistingPosition!.SubledgerId, line.ExistingPosition.ReversesTransactionId!.Value,
                    line.LineId!.Value, advance.SourceLineId, advance.PositionId, unapplyPayable ? line.Credit : line.Debit)));
                continue;
            }
            if (applications.Length == 0 && groupAdvances.Length == 0)
            {
                var counterparts = directCounterparts[group.Key];
                if (counterparts.Sum(line => line.Debit) != counterparts.Sum(line => line.Credit))
                    throw new AccountingValidationException("El movimiento directo requiere sus contrapartidas balanceadas dentro del comprobante.");
                var net = tenders.Sum(line => line.Debit - line.Credit);
                if (request.VoucherTypeCode != (net < 0 ? "Disbursement" : "Accounting") &&
                    !(request.VoucherTypeCode == "CashReceipt" && net > 0))
                    throw new AccountingValidationException("La salida directa requiere Egreso; la entrada o traslado entre cuentas requiere Comprobante contable.");
                if (request.WorkSessionId is not null && !user.Permissions.Contains(WorkSessionPermissionCodes.ManageCash))
                    throw new AccountingForbiddenException("No tienes permiso para registrar movimientos directos en la sesión de trabajo.");
                var directTenders = tenders.Select(line => NormalizeTender(line, accounts[line.AccountId], line.Credit > 0)).ToArray();
                foreach (var (line, tender) in tenders.Zip(directTenders))
                {
                    try
                    {
                        PaymentTenderBreakdown.Create([AsPaymentTender(tender)], tender.Amount, 1, PortfolioPaymentMethods.Supported, requireEffectiveCash: true);
                    }
                    catch (ArgumentException error)
                    {
                        throw new AccountingValidationException($"Revisa la partida de tesorería y su evidencia: {error.Message}", error);
                    }
                    directMovements.Add(new(group.Key, tender.SourceLineId, tender.AccountId,
                        tender.MethodCode, tender.BankAccountId, line.Debit - line.Credit, tender.Reference, ids.NewId()));
                }
                directGroups.Add(group.Key);
                continue;
            }
            if (tenders.Length == 0 && groupAdvances.Length == 0)
            {
                if (applications.Any(line => line.FinancialDiscountEarned is not null))
                    throw new AccountingValidationException("La decisión del descuento financiero de compra requiere su pago; un ajuste manual usa su concepto y documento explícitos.");
                if (applications.Length > 0 && applications.All(line =>
                    line.ExistingPosition!.SubledgerKind is (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) &&
                    adjustmentAmounts.GetValueOrDefault(line.LineId!.Value) == line.Debit + line.Credit))
                {
                    var targets = applications.Select(line => positions[(line.ExistingPosition!.SubledgerKind, line.ExistingPosition.SubledgerId)]).ToArray();
                    DemandPaymentPermission(user, targets[0].Kind == AccountingSubledgerKinds.Payable);
                    if (targets.Select(item => item.Kind).Distinct().Count() != 1 || targets.Select(item => item.PartyId).Distinct().Count() != 1 ||
                        targets[0].Kind == AccountingSubledgerKinds.Payable && targets.Select(item => item.PartySiteId).Distinct().Count() != 1)
                        throw new AccountingValidationException("Los ajustes requieren obligaciones del mismo tercero y dirección, y la misma sede de proveedor.");
                    continue;
                }
                if (request.VoucherTypeCode is not ("Accounting" or "CashReceipt" or "Disbursement"))
                    throw new AccountingValidationException("Aplicar un saldo a favor sin dinero requiere Comprobante contable.");
                var credits = applications.Where(line => line.ExistingPosition!.SubledgerKind is
                    AccountingSubledgerKinds.SupplierCredit or AccountingSubledgerKinds.CustomerCredit).ToArray();
                if (credits.Length != 1)
                    throw new AccountingValidationException("Cada cruce sin dinero debe identificar el saldo a favor y las obligaciones a las que se aplica.");
                var creditLine = credits[0];
                var payableCredit = creditLine.ExistingPosition!.SubledgerKind == AccountingSubledgerKinds.SupplierCredit;
                var positionKind = payableCredit ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable;
                var positionLines = applications.Where(line => line.ExistingPosition!.SubledgerKind == positionKind).ToArray();
                if (positionLines.Length + 1 != group.Count())
                    throw new AccountingValidationException("El cruce requiere obligaciones y saldo a favor del mismo tercero y dirección.");
                DemandPaymentPermission(user, payableCredit);
                var creditOrigin = positions[(creditLine.ExistingPosition.SubledgerKind, creditLine.ExistingPosition.SubledgerId)];
                if (positionLines.Any(line => line.PartyId != creditOrigin.PartyId || payableCredit &&
                    positions[(positionKind, line.ExistingPosition!.SubledgerId)].PartySiteId != creditOrigin.PartySiteId))
                    throw new AccountingValidationException("El saldo a favor debe corresponder al tercero y sede de las obligaciones seleccionadas.");
                try
                {
                    var remaining = positionLines.Select(line => new { Line = line,
                        Amount = (payableCredit ? line.Debit : line.Credit) - adjustmentAmounts.GetValueOrDefault(line.LineId!.Value) })
                        .Where(item => item.Amount > 0m).ToArray();
                    var total = PortfolioSettlement.Create(remaining.Select(item => new PortfolioAllocation(
                        item.Line.ExistingPosition!.SubledgerId, item.Amount))).TotalAmount;
                    if (total != (payableCredit ? creditLine.Credit : creditLine.Debit))
                        throw new AccountingValidationException("El importe aplicado del saldo a favor debe coincidir con los abonos a las obligaciones.");
                    creditApplications.AddRange(remaining.Select(item => new VoucherFinancialCreditApplication(ids.NewId(), group.Key,
                        positionKind, item.Line.LineId!.Value, creditLine.LineId!.Value, item.Line.ExistingPosition!.SubledgerId,
                        creditLine.ExistingPosition.SubledgerId, item.Amount)));
                }
                catch (ArgumentException error)
                {
                    throw new AccountingValidationException($"Revisa las obligaciones del cruce: {error.Message}", error);
                }
                continue;
            }
            if (applications.Length + groupAdvances.Length == 0 || groupAdvances.Length > 1 || tenders.Length == 0)
                throw new AccountingValidationException("El pago requiere obligaciones seleccionadas o un anticipo, y sus partidas de dinero.");
            var kinds = applications.Select(line => line.ExistingPosition!.SubledgerKind)
                .Concat(groupAdvances.Select(item => item.SubledgerKind == "SupplierCredit" ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable))
                .Distinct().ToArray();
            if (kinds.Length != 1 || kinds[0] is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable))
                throw new AccountingValidationException("El abono debe pagar o recaudar obligaciones de una misma clase.");
            var kind = kinds[0];
            var payable = kind == AccountingSubledgerKinds.Payable;
            DemandPaymentPermission(user, payable);
            if (payable && request.VoucherTypeCode != "Disbursement" ||
                !payable && request.VoucherTypeCode is not ("Accounting" or "CashReceipt"))
                throw new AccountingValidationException("El pago al proveedor requiere Egreso y el recaudo requiere Recibo de caja o su comprobante contable anterior.");
            var origins = applications.Select(line => positions[(kind, line.ExistingPosition!.SubledgerId)])
                .Select(item => (item.PartyId, item.RoleId, item.PartySiteId))
                .Concat(groupAdvances.Select(item => (item.PartyId, RoleId: item.CounterpartyRoleId, item.PartySiteId))).ToArray();
            var party = origins.Select(item => item.PartyId).Distinct().ToArray();
            var roles = origins.Select(item => item.RoleId).Distinct().ToArray();
            var sites = origins.Select(item => item.PartySiteId).Distinct().ToArray();
            if (party.Length != 1 || roles.Length != 1 || groupAdvances.Length > 0 && sites.Length != 1 ||
                payable && (roles[0] is null || sites.Length != 1 || sites[0] is null))
                throw new AccountingValidationException("El pago debe corresponder al mismo tercero y, en pagos, a una sola sede del proveedor.");
            if (tenders.Any(line => line.PartyId is { } id && id != party[0] ||
                payable && (line.Credit <= 0 || line.Debit != 0) || !payable && (line.Debit <= 0 || line.Credit != 0)))
                throw new AccountingValidationException("Las partidas de tesorería no corresponden al tercero o al sentido del abono.");
            var discountAdjustments = new Dictionary<Guid, decimal>();
            foreach (var line in applications.Where(item => item.FinancialDiscountEarned is not null))
            {
                var position = positions[(kind, line.ExistingPosition!.SubledgerId)];
                if (!payable || position.FinancialDiscountEligibleAmount is not > 0m ||
                    position.FinancialDiscountAnticipated is null || position.FinancialDiscountDecided ||
                    position.FinancialDiscountPendingReturn)
                    throw new AccountingValidationException("No se registró el egreso ni se pagó: el descuento financiero de esta compra no está disponible o ya fue decidido.");
                var eligible = position.FinancialDiscountEligibleAmount.Value;
                discountAdjustments.Add(line.LineId!.Value,
                    (line.FinancialDiscountEarned == true ? eligible : 0m) -
                    (position.FinancialDiscountAnticipated == true ? eligible : 0m));
            }
            var paymentCounterparts = directCounterparts[group.Key].Where(line => line.FinancialGroupId == group.Key &&
                !financialIds.Contains(line.LineId!.Value) && !openingIds.Contains(line.LineId.Value)).ToArray();
            var gain = discountAdjustments.Values.Where(value => value > 0m).Sum();
            var loss = -discountAdjustments.Values.Where(value => value < 0m).Sum();
            // A discount is identified by the explicit decision and its configured
            // account; other counterparts are explicit account lines, with or without a catalogue concept.
            var discountCounterparts = paymentCounterparts.Where(line => line.FinancialConceptId is null &&
                (gain > 0m && line.AccountId == context.SupplierDiscountEarnedAccountId ||
                 loss > 0m && line.AccountId == context.SupplierDiscountLostAccountId)).ToArray();
            var discountLineIds = discountCounterparts.Select(line => line.LineId!.Value).ToHashSet();
            var additionalConcepts = paymentCounterparts.Where(line => !discountLineIds.Contains(line.LineId!.Value)).ToArray();
            if (additionalConcepts.Any(line => line.PartyId != party[0] || line.ExistingPosition is not null ||
                accounts[line.AccountId].Function != "General" || line.PaymentEvidence is not null ||
                line.DueDate is not null || line.FinancialDiscountEarned is not null ||
                (payable ? line.Debit <= 0m || line.Credit != 0m : line.Credit <= 0m || line.Debit != 0m)))
                throw new AccountingValidationException("La contrapartida adicional debe ser una cuenta general del mismo tercero y sentido del pago. No reduce la obligación ni crea un anticipo.");
            var additionalAmount = additionalConcepts.Sum(line => payable ? line.Debit : line.Credit);
            if (discountCounterparts.Where(line => line.AccountId == context.SupplierDiscountEarnedAccountId).Sum(line => line.Credit) != gain ||
                discountCounterparts.Where(line => line.AccountId == context.SupplierDiscountLostAccountId).Sum(line => line.Debit) != loss ||
                discountCounterparts.Any(line => line.PartyId != party[0] || line.ExistingPosition is not null || line.PaymentEvidence is not null ||
                    line.DueDate is not null || line.FinancialDiscountEarned is not null ||
                    line.AccountId == context.SupplierDiscountEarnedAccountId && (line.Credit <= 0m || line.Debit != 0m) ||
                    line.AccountId == context.SupplierDiscountLostAccountId && (line.Debit <= 0m || line.Credit != 0m)))
                throw new AccountingValidationException("No se registró el egreso ni se pagó: las contrapartidas del descuento financiero deben usar las cuentas configuradas y sumar su importe exacto.");
            foreach (var counterpart in paymentCounterparts) paymentCounterpartIds.Add(counterpart.LineId!.Value);
            try
            {
                var allocations = applications.Select(line => new {
                    Line = line,
                    Adjustment = discountAdjustments.GetValueOrDefault(line.LineId!.Value),
                    CashAmount = (payable ? line.Debit : line.Credit) - discountAdjustments.GetValueOrDefault(line.LineId!.Value) -
                        adjustmentAmounts.GetValueOrDefault(line.LineId!.Value)
                }).ToArray();
                if (allocations.Any(item => item.CashAmount < 0m || item.CashAmount == 0m && item.Line.FinancialDiscountEarned is not null))
                    throw new AccountingValidationException("Los ajustes y el descuento no pueden superar la partida seleccionada; la decisión financiera de compra requiere un abono monetario.");
                var moneyAllocations = allocations.Where(item => item.CashAmount > 0m).ToArray();
                var appliedAmount = moneyAllocations.Length == 0 ? 0m : PortfolioSettlement.Create(moneyAllocations.Select(item =>
                    new PortfolioAllocation(item.Line.ExistingPosition!.SubledgerId, item.CashAmount))).TotalAmount;
                var advance = groupAdvances.SingleOrDefault();
                var amount = appliedAmount + (advance?.Amount ?? 0m) + additionalAmount;
                var normalizedTenders = tenders.Select(line => NormalizeTender(line, accounts[line.AccountId], payable)).ToArray();
                PaymentTenderBreakdown.Create(normalizedTenders.Select(AsPaymentTender),
                    amount, applications.Length + groupAdvances.Length, PortfolioPaymentMethods.Supported, requireEffectiveCash: true);
                if (advance is not null)
                    openings[openings.IndexOf(advance)] = advance with { GroupId = group.Key };
                if (moneyAllocations.Length == 0 && advance is null)
                {
                    if (request.WorkSessionId is not null && !user.Permissions.Contains(WorkSessionPermissionCodes.ManageCash))
                        throw new AccountingForbiddenException("No tienes permiso para registrar movimientos directos en la sesión de trabajo.");
                    directMovements.AddRange(normalizedTenders.Select(tender => new VoucherFinancialMoneyMovement(group.Key,
                        tender.SourceLineId, tender.AccountId, tender.MethodCode, tender.BankAccountId,
                        payable ? -tender.Amount : tender.Amount, tender.Reference, ids.NewId())));
                    directGroups.Add(group.Key);
                    continue;
                }
                payments.Add(new(group.Key, ids.NewId(), kind, party[0], roles[0], sites.Length == 1 ? sites[0] : null,
                    moneyAllocations.Select(item => new VoucherFinancialPaymentAllocation(item.Line.LineId!.Value,
                        item.Line.ExistingPosition!.SubledgerId, item.Line.AccountId, item.CashAmount,
                        item.Line.FinancialDiscountEarned, item.Adjustment)).ToArray(), normalizedTenders,
                    advance?.PositionId, advance?.Amount ?? 0m, additionalAmount == 0m ? null : additionalAmount,
                    additionalConcepts.Length == 0 ? null : additionalConcepts.Select(line => line.LineId!.Value).ToArray()));
            }
            catch (ArgumentException error)
            {
                throw new AccountingValidationException($"Revisa el abono y sus medios de pago: {error.Message}", error);
            }
        }
        if (lines.Any(line => line.FinancialGroupId is { } groupId &&
            !financialIds.Contains(line.LineId!.Value) && !openingIds.Contains(line.LineId!.Value) &&
            !paymentCounterpartIds.Contains(line.LineId!.Value) && !directGroups.Contains(groupId)))
            throw new AccountingValidationException("La partida no corresponde a una operación financiera ni a sus contrapartidas directas.");
        return new(user.UserId, payments, openings, creditApplications, directMovements,
            unapplications.Count == 0 ? null : unapplications,conceptEvidence);
    }

    private static void ValidateCashReceipt(ConfirmManualAccountingVoucherRequest request,
        IReadOnlyDictionary<Guid, VoucherFinancialAccount> accounts,
        IReadOnlyDictionary<Guid, VoucherFinancialParty> parties)
    {
        if (request.MainPartyId is not { } receiptParty || !parties.ContainsKey(receiptParty))
            throw new AccountingValidationException("El recibo de caja requiere un tercero activo como principal.");
        if (request.Lines.Any(line => line.PartyId is { } partyId && partyId != receiptParty))
            throw new AccountingValidationException("Las partidas del recibo deben corresponder al tercero principal.");
        var treasury = request.Lines.Where(line => accounts.TryGetValue(line.AccountId, out var account) && IsTreasury(account.Function)).ToArray();
        var invoices = request.Lines.Where(line => line.ExistingPosition is
                { SubledgerKind: AccountingSubledgerKinds.Receivable, ReversesTransactionId: null } && line.Credit > 0)
            .Select(line => line.ExistingPosition!.SubledgerId).Distinct().Count();
        try { PaymentTenderBreakdown.EnsureUnambiguousInvoiceAttribution(invoices, treasury.Length); }
        catch (ArgumentException error) { throw new AccountingValidationException(error.Message, error); }
        if (treasury.Any(line => accounts[line.AccountId].Function == "CreditCardLiability"))
            throw new AccountingValidationException("El recibo requiere una entrada real de dinero. Reducir la deuda de una tarjeta no es un recaudo.");
        if (treasury.Length == 0 && !(request.Lines.All(line => line.ExistingPosition is not null || line.FinancialConceptTargetLineId is not null) &&
            request.Lines.Any(line => line.ExistingPosition?.SubledgerKind == AccountingSubledgerKinds.CustomerCredit || line.FinancialConceptTargetLineId is not null)))
            throw new AccountingValidationException("Una aplicación sin dinero usa el comprobante contable y conserva el recibo de origen; no genera otra entrada de caja.");
        if (treasury.Any(line => line.Debit <= 0 || line.Credit != 0))
            throw new AccountingValidationException("El recibo de caja registra el dinero efectivamente recibido, sin salidas ni vueltos.");
    }

    public static string? ResolveVoucherType(string? requestedType, VoucherFinancialEffects effects) =>
        requestedType is ("CashReceipt" or "Disbursement") && effects.Payments.Count == 0 && effects.DirectMovements.Count == 0 &&
        effects.Positions.All(item => item.EffectKind == "Adjustment") &&
        (effects.CreditApplications.Count > 0 || effects.Positions.Count > 0) && effects.Unapplications is not { Count: > 0 }
            ? "Accounting" : requestedType;

    private static IReadOnlyList<VoucherFinancialPositionEffect> NormalizeConceptAdjustments(
        ConfirmManualAccountingVoucherRequest request, IReadOnlyDictionary<Guid, VoucherFinancialAccount> accounts,
        IReadOnlyDictionary<(string, Guid), VoucherFinancialPosition> positions, IAuralyIdGenerator ids)
    {
        var lines = request.Lines.ToDictionary(line => line.LineId!.Value);
        var effects = new List<VoucherFinancialPositionEffect>();
        foreach (var line in request.Lines.Where(item => item.FinancialConceptTargetLineId is not null))
        {
            if (line.FinancialConceptId is null || line.FinancialConceptTargetLineId == Guid.Empty ||
                !lines.TryGetValue(line.FinancialConceptTargetLineId!.Value, out var target) ||
                target.ExistingPosition is not { ReversesTransactionId: null } binding ||
                binding.SubledgerKind is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable) ||
                line.ExistingPosition is not null || line.PaymentEvidence is not null || line.DueDate is not null ||
                line.FinancialDiscountEarned is not null || accounts[line.AccountId].Function != "General" ||
                line.PartyId != target.PartyId || line.FinancialGroupId is null || line.FinancialGroupId != target.FinancialGroupId)
                throw new AccountingValidationException("El ajuste requiere un concepto general y una partida de obligación del mismo tercero y grupo.");
            var payable = binding.SubledgerKind == AccountingSubledgerKinds.Payable;
            var origin = positions[(binding.SubledgerKind, binding.SubledgerId)];
            var amount = payable ? line.Credit : line.Debit;
            if (amount <= 0m || (payable ? line.Debit : line.Credit) != 0m ||
                line.PartySiteId is { } site && site != origin.PartySiteId)
                throw new AccountingValidationException("La naturaleza y sede del concepto deben corresponder a la reducción de la obligación seleccionada.");
            effects.Add(new(ids.NewId(), line.FinancialGroupId.Value, "Adjustment", binding.SubledgerKind,
                line.LineId!.Value, binding.SubledgerId, origin.AccountId!.Value, origin.PartyId, origin.RoleId,
                origin.PartySiteId, amount, null, TargetSourceLineId: target.LineId));
        }
        if (effects.GroupBy(item => item.TargetSourceLineId!.Value).Any(group =>
            group.Sum(item => item.Amount) > lines[group.Key].Debit + lines[group.Key].Credit))
            throw new AccountingValidationException("Los conceptos de ajuste superan el importe de la partida seleccionada.");
        return effects;
    }

    internal static void DemandPaymentPermission(AccountingUserIdentity user, bool payable)
    {
        var permission = payable ? PayablesPermissionCodes.RegisterPayment : ReceivablesPermissionCodes.RegisterPayment;
        if (!user.Permissions.Contains(permission))
            throw new AccountingForbiddenException("No tienes permiso para ejecutar el pago, recaudo o cruce del comprobante.");
    }

    private static bool IsTreasury(string function) => function is "Cash" or "Bank" or "CardClearing" or "CreditCardLiability";

    internal static void ValidatePaymentFormEvidence(VoucherPaymentEvidence? evidence, Guid? accountId)
    {
        if (evidence is null || evidence.PaymentMethodId is null && evidence.PaymentMethodTypeCode is null &&
            evidence.PaymentMethodVersion is null && evidence.PaymentMethodCode is null && evidence.PaymentMethodName is null) return;
        if (!PortfolioPaymentMethods.IsSupported(evidence.PaymentMethodTypeCode) ||
            !PaymentMethodEvidence.IsValidSet([new(evidence.PaymentMethodId, evidence.PaymentMethodVersion,
                evidence.PaymentMethodCode, evidence.PaymentMethodName, accountId)]))
            throw new AccountingValidationException("La partida requiere la identidad completa de una forma de pago monetaria y su auxiliar visible.");
    }

    private static VoucherFinancialTender NormalizeTender(ManualVoucherLineRequest line, VoucherFinancialAccount account, bool payable)
    {
        var method = line.PaymentEvidence?.PaymentMethodTypeCode ?? account.MethodCode ?? (account.Function == "Cash" ? "Cash" : account.Function == "Bank" ? "Transfer" : null);
        if (!PortfolioPaymentMethods.IsSupported(method))
            throw new AccountingValidationException("Configura un medio de pago soportado en el auxiliar de tesorería.");
        if (account.Function == "Bank" && account.BankAccountId is null)
            throw new AccountingValidationException("El auxiliar bancario no identifica su cuenta bancaria.");
        // The auxiliary retains the bank dimension. The native tender field selects
        // a transfer destination; passing it for a card would change its meaning.
        return new(line.LineId!.Value, line.AccountId, line.PartyId, method!,
            method == PortfolioPaymentMethods.Transfer ? account.BankAccountId : null,
            payable ? line.Credit : line.Debit, line.Reference, line.PaymentEvidence?.CardFranchiseCode,
            line.PaymentEvidence?.ApprovalNumber, line.PaymentEvidence?.TenderedAmount, line.PaymentEvidence?.Notes,
            line.PaymentEvidence?.PaymentMethodId, line.PaymentEvidence?.PaymentMethodVersion,
            line.PaymentEvidence?.PaymentMethodCode, line.PaymentEvidence?.PaymentMethodName);
    }

    private static PaymentTender AsPaymentTender(VoucherFinancialTender item) => new(item.MethodCode, item.Amount,
        item.TenderedAmount, item.BankAccountId, item.Reference, item.Notes, item.CardFranchiseCode, item.ApprovalNumber,
        item.PaymentMethodId, item.PaymentMethodVersion, item.PaymentMethodCode, item.PaymentMethodName,
        item.PaymentMethodId is null ? null : item.AccountId);
}
