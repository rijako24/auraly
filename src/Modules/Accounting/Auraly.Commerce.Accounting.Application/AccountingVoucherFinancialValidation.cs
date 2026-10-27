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
    Guid? SupplierDiscountEarnedAccountId = null, Guid? SupplierDiscountLostAccountId = null);

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
    public static VoucherFinancialEffects Normalize(AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, VoucherFinancialContext context, IAuralyIdGenerator ids,
        VoucherFiscalPreview? fiscalProof = null)
    {
        if (request.PayloadVersion != 4 || request.CurrencyCode != "COP" || request.FinancialEffects is not null)
            throw new AccountingValidationException("La captura financiera requiere COP y efectos calculados por el servidor.");
        if (request.VoucherTypeCode == "SupportDocumentCancellation")
            return NormalizeFiscalCancellation(user, request, context, ids, fiscalProof);
        if (request.VoucherTypeCode is not ("Accounting" or "Disbursement" or "SupportDocument") ||
            request.VoucherTypeCode == "SupportDocument" && (fiscalProof is null || !fiscalProof.CanConfirm || request.FiscalCapture is null))
            throw new AccountingValidationException("El documento soporte requiere completar su información fiscal antes de enviarlo.");

        var accounts = context.Accounts.ToDictionary(item => item.AccountId);
        var positions = context.Positions.ToDictionary(item => (item.Kind, item.PositionId));
        var parties = context.Parties.ToDictionary(item => item.PartyId);
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
                if (request.VoucherTypeCode is not ("Accounting" or "SupportDocument") || line.PartyId is not { } partyId ||
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

        var advances = openings.Where(item => item.SubledgerKind is "SupplierCredit" or "CustomerCredit")
            .ToDictionary(item => item.SourceLineId);
        var financial = lines.Where(line => line.ExistingPosition is not null || advances.ContainsKey(line.LineId!.Value) ||
            IsTreasury(accounts[line.AccountId].Function)).ToArray();
        if (financial.Length == 0)
        {
            if (request.VoucherTypeCode == "Disbursement")
                throw new AccountingValidationException("No se registró el egreso: selecciona un auxiliar de efectivo, banco o tarjeta para la salida.");
            var openingLines = openings.Select(item => item.SourceLineId).ToHashSet();
            if (lines.Any(line => !openingLines.Contains(line.LineId!.Value) &&
                (line.FinancialGroupId is not null || line.PaymentEvidence is not null)))
                throw new AccountingValidationException("El grupo o evidencia de pago no corresponde a una partida financiera.");
            return new(user.UserId, [], openings, [], []);
        }

        // Debt groups identify one party. Pure treasury groups identify their
        // balanced journal counterparts instead; they do not create a debt.
        // Never manufacture groups per invoice to evade the shared tender rule.
        var explicitGroups = financial.Any(line => line.FinancialGroupId is not null);
        if (explicitGroups && financial.Any(line => line.FinancialGroupId is null))
            throw new AccountingValidationException("Asocia todas las partidas financieras a su grupo de cruce.");
        var financialParties = financial.Where(line => !IsTreasury(accounts[line.AccountId].Function))
            .Select(line => line.PartyId).Distinct().ToArray();
        if (!explicitGroups && financialParties.Length > 1)
            throw new AccountingValidationException("Asocia las partidas financieras a su grupo para identificar su tercero y contrapartidas.");
        var implicitId = financial[0].LineId!.Value;
        var groups = financial.GroupBy(line => line.FinancialGroupId ?? implicitId);
        var payments = new List<VoucherFinancialPaymentGroup>();
        var creditApplications = new List<VoucherFinancialCreditApplication>();
        var unapplications = new List<VoucherFinancialUnapplication>();
        var directMovements = new List<VoucherFinancialMoneyMovement>();
        var directGroups = new HashSet<Guid>();
        var discountCounterpartIds = new HashSet<Guid>();
        var openingIds = openings.Select(item => item.SourceLineId).ToHashSet();
        var financialIds = financial.Select(item => item.LineId!.Value).ToHashSet();
        var directCounterparts = lines.Where(line => !openingIds.Contains(line.LineId!.Value))
            .ToLookup(line => line.FinancialGroupId ?? implicitId);
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
                    if (request.VoucherTypeCode != "Accounting" || originals.Length is < 1 or > 100 ||
                        originals.Length + tenders.Length != group.Count() || openings.Any(item => item.GroupId == group.Key) ||
                        originals.Any(line => line.ExistingPosition!.SubledgerKind != returnKind || line.PartyId != origin.PartyId ||
                            positions[(returnKind, line.ExistingPosition.SubledgerId)].RoleId != origin.RoleId ||
                            positions[(returnKind, line.ExistingPosition.SubledgerId)].PartySiteId != origin.PartySiteId) ||
                        tenders.Any(line => line.PartyId is { } partyId && partyId != origin.PartyId ||
                            returnPayable && (line.Debit <= 0 || line.Credit != 0) ||
                            !returnPayable && (line.Credit <= 0 || line.Debit != 0)))
                        throw new AccountingValidationException("La devolución requiere los abonos originales del mismo tercero y sede, y el dinero realmente devuelto, sin anticipos ni obligaciones nuevas en ese grupo.");
                    try
                    {
                        var amount = originals.Sum(line => returnPayable ? line.Credit : line.Debit);
                        var returnedTenders = tenders.Select(line => NormalizeTender(line, accounts[line.AccountId], !returnPayable)).ToArray();
                        PaymentTenderBreakdown.Create(returnedTenders.Select(item => new PaymentTender(item.MethodCode, item.Amount,
                            item.TenderedAmount, item.BankAccountId, item.Reference, item.Notes, item.CardFranchiseCode, item.ApprovalNumber)),
                            amount, originals.Select(line => line.ExistingPosition!.SubledgerId).Distinct().Count(), PortfolioPaymentMethods.Supported);
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
                    throw new AccountingValidationException("El movimiento directo requiere sus contrapartidas balanceadas en el mismo grupo.");
                var net = tenders.Sum(line => line.Debit - line.Credit);
                if (request.VoucherTypeCode != (net < 0 ? "Disbursement" : "Accounting"))
                    throw new AccountingValidationException("La salida directa requiere Egreso; la entrada o traslado entre cuentas requiere Comprobante contable.");
                if (request.WorkSessionId is not null && !user.Permissions.Contains(WorkSessionPermissionCodes.ManageCash))
                    throw new AccountingForbiddenException("No tienes permiso para registrar movimientos directos en la sesión de trabajo.");
                foreach (var line in tenders)
                {
                    var tender = NormalizeTender(line, accounts[line.AccountId], line.Credit > 0);
                    try
                    {
                        PaymentTenderBreakdown.Create([new PaymentTender(tender.MethodCode, tender.Amount,
                            tender.TenderedAmount, tender.BankAccountId, tender.Reference, tender.Notes,
                            tender.CardFranchiseCode, tender.ApprovalNumber)], tender.Amount, 1, PortfolioPaymentMethods.Supported);
                    }
                    catch (ArgumentException error)
                    {
                        throw new AccountingValidationException($"Revisa la partida de tesorería y su evidencia: {error.Message}", error);
                    }
                    directMovements.Add(new(group.Key, line.LineId!.Value, line.AccountId,
                        tender.MethodCode, tender.BankAccountId, line.Debit - line.Credit, line.Reference, ids.NewId()));
                }
                directGroups.Add(group.Key);
                continue;
            }
            if (tenders.Length == 0 && groupAdvances.Length == 0)
            {
                if (request.VoucherTypeCode != "Accounting")
                    throw new AccountingValidationException("Aplicar un saldo a favor sin dinero requiere Comprobante contable.");
                var credits = applications.Where(line => line.ExistingPosition!.SubledgerKind is
                    AccountingSubledgerKinds.SupplierCredit or AccountingSubledgerKinds.CustomerCredit).ToArray();
                if (credits.Length != 1)
                    throw new AccountingValidationException("Cada grupo de cruce sin dinero requiere un saldo a favor seleccionado. Separa los demás en otro grupo.");
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
                    var total = PortfolioSettlement.Create(positionLines.Select(line => new PortfolioAllocation(
                        line.ExistingPosition!.SubledgerId, payableCredit ? line.Debit : line.Credit))).TotalAmount;
                    if (total != (payableCredit ? creditLine.Credit : creditLine.Debit))
                        throw new AccountingValidationException("El importe aplicado del saldo a favor debe coincidir con los abonos a las obligaciones.");
                    creditApplications.AddRange(positionLines.Select(line => new VoucherFinancialCreditApplication(ids.NewId(), group.Key,
                        positionKind, line.LineId!.Value, creditLine.LineId!.Value, line.ExistingPosition!.SubledgerId,
                        creditLine.ExistingPosition.SubledgerId, payableCredit ? line.Debit : line.Credit)));
                }
                catch (ArgumentException error)
                {
                    throw new AccountingValidationException($"Revisa las obligaciones del cruce: {error.Message}", error);
                }
                continue;
            }
            if (applications.Length + groupAdvances.Length == 0 || groupAdvances.Length > 1 || tenders.Length == 0)
                throw new AccountingValidationException("El grupo requiere obligaciones seleccionadas o un anticipo, y sus partidas de tesorería.");
            var kinds = applications.Select(line => line.ExistingPosition!.SubledgerKind)
                .Concat(groupAdvances.Select(item => item.SubledgerKind == "SupplierCredit" ? AccountingSubledgerKinds.Payable : AccountingSubledgerKinds.Receivable))
                .Distinct().ToArray();
            if (kinds.Length != 1 || kinds[0] is not (AccountingSubledgerKinds.Payable or AccountingSubledgerKinds.Receivable))
                throw new AccountingValidationException("Cada grupo de abono debe pagar o recaudar obligaciones de una misma clase.");
            var kind = kinds[0];
            var payable = kind == AccountingSubledgerKinds.Payable;
            DemandPaymentPermission(user, payable);
            if (payable && request.VoucherTypeCode != "Disbursement" || !payable && request.VoucherTypeCode != "Accounting")
                throw new AccountingValidationException("El pago al proveedor requiere Egreso y el recaudo requiere Comprobante contable.");
            var origins = applications.Select(line => positions[(kind, line.ExistingPosition!.SubledgerId)])
                .Select(item => (item.PartyId, item.RoleId, item.PartySiteId))
                .Concat(groupAdvances.Select(item => (item.PartyId, RoleId: item.CounterpartyRoleId, item.PartySiteId))).ToArray();
            var party = origins.Select(item => item.PartyId).Distinct().ToArray();
            var roles = origins.Select(item => item.RoleId).Distinct().ToArray();
            var sites = origins.Select(item => item.PartySiteId).Distinct().ToArray();
            if (party.Length != 1 || roles.Length != 1 || groupAdvances.Length > 0 && sites.Length != 1 ||
                payable && (roles[0] is null || sites.Length != 1 || sites[0] is null))
                throw new AccountingValidationException("El grupo debe corresponder al mismo tercero y, en pagos, a una sola sede del proveedor.");
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
            var discountCounterparts = lines.Where(line => line.FinancialGroupId == group.Key &&
                !financialIds.Contains(line.LineId!.Value) && !openingIds.Contains(line.LineId.Value)).ToArray();
            var gain = discountAdjustments.Values.Where(value => value > 0m).Sum();
            var loss = -discountAdjustments.Values.Where(value => value < 0m).Sum();
            var expectedCount = (gain > 0m ? 1 : 0) + (loss > 0m ? 1 : 0);
            if (discountCounterparts.Length != expectedCount ||
                gain > 0m && !discountCounterparts.Any(line => line.AccountId == context.SupplierDiscountEarnedAccountId &&
                    line.PartyId == party[0] && line.Credit == gain && line.Debit == 0m) ||
                loss > 0m && !discountCounterparts.Any(line => line.AccountId == context.SupplierDiscountLostAccountId &&
                    line.PartyId == party[0] && line.Debit == loss && line.Credit == 0m) ||
                discountCounterparts.Any(line => line.ExistingPosition is not null || line.PaymentEvidence is not null ||
                    line.DueDate is not null || line.FinancialDiscountEarned is not null))
                throw new AccountingValidationException("No se registró el egreso ni se pagó: la contrapartida del descuento financiero debe usar la cuenta configurada y el importe exacto del grupo.");
            foreach (var counterpart in discountCounterparts) discountCounterpartIds.Add(counterpart.LineId!.Value);
            try
            {
                var allocations = applications.Select(line => new {
                    Line = line,
                    Adjustment = discountAdjustments.GetValueOrDefault(line.LineId!.Value),
                    CashAmount = (payable ? line.Debit : line.Credit) - discountAdjustments.GetValueOrDefault(line.LineId!.Value)
                }).ToArray();
                var appliedAmount = allocations.Length == 0 ? 0m : PortfolioSettlement.Create(allocations.Select(item =>
                    new PortfolioAllocation(item.Line.ExistingPosition!.SubledgerId, item.CashAmount))).TotalAmount;
                var advance = groupAdvances.SingleOrDefault();
                var amount = appliedAmount + (advance?.Amount ?? 0m);
                var normalizedTenders = tenders.Select(line => NormalizeTender(line, accounts[line.AccountId], payable)).ToArray();
                PaymentTenderBreakdown.Create(normalizedTenders.Select(item => new PaymentTender(item.MethodCode, item.Amount,
                    item.TenderedAmount, item.BankAccountId, item.Reference, item.Notes, item.CardFranchiseCode, item.ApprovalNumber)),
                    amount, applications.Length + groupAdvances.Length, PortfolioPaymentMethods.Supported);
                if (advance is not null)
                    openings[openings.IndexOf(advance)] = advance with { GroupId = group.Key };
                payments.Add(new(group.Key, ids.NewId(), kind, party[0], roles[0], sites.Length == 1 ? sites[0] : null,
                    allocations.Select(item => new VoucherFinancialPaymentAllocation(item.Line.LineId!.Value,
                        item.Line.ExistingPosition!.SubledgerId, item.Line.AccountId, item.CashAmount,
                        item.Line.FinancialDiscountEarned, item.Adjustment)).ToArray(), normalizedTenders,
                    advance?.PositionId, advance?.Amount ?? 0m));
            }
            catch (ArgumentException error)
            {
                throw new AccountingValidationException($"Revisa el abono y sus medios de pago: {error.Message}", error);
            }
        }
        if (lines.Any(line => line.FinancialGroupId is { } groupId &&
            !financialIds.Contains(line.LineId!.Value) && !openingIds.Contains(line.LineId!.Value) &&
            !discountCounterpartIds.Contains(line.LineId!.Value) && !directGroups.Contains(groupId)))
            throw new AccountingValidationException("La partida no corresponde a un grupo financiero ni a sus contrapartidas directas.");
        return new(user.UserId, payments, openings, creditApplications, directMovements,
            unapplications.Count == 0 ? null : unapplications);
    }

    internal static void DemandPaymentPermission(AccountingUserIdentity user, bool payable)
    {
        var permission = payable ? PayablesPermissionCodes.RegisterPayment : ReceivablesPermissionCodes.RegisterPayment;
        if (!user.Permissions.Contains(permission))
            throw new AccountingForbiddenException("No tienes permiso para ejecutar el pago, recaudo o cruce del comprobante.");
    }

    private static bool IsTreasury(string function) => function is "Cash" or "Bank" or "CardClearing" or "CreditCardLiability";

    private static VoucherFinancialTender NormalizeTender(ManualVoucherLineRequest line, VoucherFinancialAccount account, bool payable)
    {
        var method = account.MethodCode ?? (account.Function == "Cash" ? "Cash" : account.Function == "Bank" ? "Transfer" : null);
        if (!PortfolioPaymentMethods.IsSupported(method))
            throw new AccountingValidationException("Configura un medio de pago soportado en el auxiliar de tesorería.");
        if (account.Function == "Bank" && account.BankAccountId is null)
            throw new AccountingValidationException("El auxiliar bancario no identifica su cuenta bancaria.");
        // The auxiliary retains the bank dimension. The native tender field selects
        // a transfer destination; passing it for a card would change its meaning.
        return new(line.LineId!.Value, line.AccountId, line.PartyId, method!,
            method == PortfolioPaymentMethods.Transfer ? account.BankAccountId : null,
            payable ? line.Credit : line.Debit, line.Reference, line.PaymentEvidence?.CardFranchiseCode,
            line.PaymentEvidence?.ApprovalNumber, line.PaymentEvidence?.TenderedAmount, line.PaymentEvidence?.Notes);
    }
}
