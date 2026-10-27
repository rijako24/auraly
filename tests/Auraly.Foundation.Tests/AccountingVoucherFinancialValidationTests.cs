using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;

namespace Auraly.Foundation.Tests;

public sealed class AccountingVoucherFinancialValidationTests
{
    [Theory]
    [InlineData(false, true, 100, 90, 10)]
    [InlineData(true, false, 90, 100, -10)]
    [InlineData(false, false, 100, 100, 0)]
    [InlineData(true, true, 90, 90, 0)]
    public void Manual_disbursement_freezes_discount_decision_and_uses_one_payment_flow(
        bool anticipated, bool earned, decimal payableReduction, decimal cash, decimal adjustment)
    {
        var party = Guid.NewGuid(); var role = Guid.NewGuid(); var site = Guid.NewGuid();
        var debt = Guid.NewGuid(); var bank = Guid.NewGuid(); var earnedAccount = Guid.NewGuid();
        var lostAccount = Guid.NewGuid(); var position = Guid.NewGuid(); var group = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(-5));
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { PayablesPermissionCodes.RegisterPayment });
        var lines = new List<ManualVoucherLineRequest> {
            new(debt, party, null, "Obligación", payableReduction, 0m,
                LineId: Guid.NewGuid(), ExistingPosition: new("Payable", position), FinancialGroupId: group,
                FinancialDiscountEarned: earned),
            new(bank, party, null, "Banco", 0m, cash, Reference: "EGRESO-1",
                LineId: Guid.NewGuid(), FinancialGroupId: group)
        };
        if (adjustment != 0m)
            lines.Add(new(adjustment > 0m ? earnedAccount : lostAccount, party, null, "Descuento financiero",
                adjustment < 0m ? -adjustment : 0m, adjustment > 0m ? adjustment : 0m,
                LineId: Guid.NewGuid(), FinancialGroupId: group));
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), user.BusinessId, at,
            "MANUAL_VOUCHER", "Pago con descuento", lines, PayloadVersion: 4,
            VoucherTypeCode: "Disbursement", CurrencyCode: "COP");
        var context = new VoucherFinancialContext(
            [new(debt, "Payable", null, null), new(bank, "Bank", "Transfer", Guid.NewGuid()),
                new(earnedAccount, "General", null, null), new(lostAccount, "General", null, null)],
            [new("Payable", position, debt, party, role, site, "COP", at.AddDays(-1), null,
                10m, anticipated)], [], earnedAccount, lostAccount);
        var ids = new Uuid7AuralyIdGenerator(TimeProvider.System);
        var payment = Assert.Single(AccountingVoucherFinancialValidation.Normalize(user, request, context, ids).Payments);
        var allocation = Assert.Single(payment.Allocations);
        Assert.Equal(cash, allocation.Amount);
        Assert.Equal(adjustment, allocation.FinancialDiscountAdjustment);
        Assert.Equal(earned, allocation.FinancialDiscountEarned);
        Assert.Equal(cash, Assert.Single(payment.Tenders).Amount);
        if (adjustment != 0m)
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,
                request with { Lines = lines.Take(2).ToArray() }, context, ids));
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,
            request with { Lines = lines.Select(line => line.AccountId == bank ? line with { Credit = cash + 1m } : line).ToArray() },
            context, ids));
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,
            request with { Lines = [.. lines, lines[0] with { LineId = Guid.NewGuid(), FinancialGroupId = Guid.NewGuid() }] },
            context, ids));
    }

    [Theory]
    [InlineData(true, "Cash")]
    [InlineData(false, "Cash")]
    [InlineData(true, "Transfer")]
    [InlineData(false, "Transfer")]
    [InlineData(true, "DebitCard")]
    [InlineData(false, "DebitCard")]
    [InlineData(true, "CreditCard")]
    [InlineData(false, "CreditCard")]
    public void Returning_real_money_reopens_the_original_position_without_manufacturing_an_advance(bool payable, string method)
    {
        var party = Guid.NewGuid(); var role = Guid.NewGuid(); var site = Guid.NewGuid();
        var debtAccount = Guid.NewGuid(); var moneyAccount = Guid.NewGuid(); var position = Guid.NewGuid();
        var original = Guid.NewGuid(); var group = Guid.NewGuid();
        var kind = payable ? "Payable" : "Receivable";
        var at = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(-5));
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment,
                Auraly.Contracts.WorkSessions.WorkSessionPermissionCodes.ManageCash });
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), user.BusinessId, at, "MANUAL_VOUCHER", "Dinero devuelto", [
            new(debtAccount, party, null, "Reabrir obligación", payable ? 0m : 75m, payable ? 75m : 0m,
                LineId: Guid.NewGuid(), ExistingPosition: new(kind, position, original), FinancialGroupId: group, PartySiteId: site),
            new(moneyAccount, party, null, "Devolución real del dinero", payable ? 75m : 0m, payable ? 0m : 75m,
                LineId: Guid.NewGuid(), FinancialGroupId: group, Reference: method == "Transfer" ? "DEVUELTO-001" : null,
                PaymentEvidence: method is "DebitCard" or "CreditCard" ? new("Visa", "DEVOLUCION-001") : null)],
            MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: "Accounting", WorkSessionId: Guid.NewGuid(), CurrencyCode: "COP");
        var context = new VoucherFinancialContext(
            [new(debtAccount, kind, null, null), new(moneyAccount, method == "Cash" ? "Cash" : method == "Transfer" ? "Bank" : "CardClearing", method,
                method != "Cash" ? Guid.NewGuid() : null)],
            [new(kind, position, debtAccount, party, role, site, "COP", at.AddDays(-1), null)], []);
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, new Uuid7AuralyIdGenerator(TimeProvider.System));
        Assert.Empty(effects.Positions);
        Assert.Empty(effects.Payments);
        Assert.Empty(effects.CreditApplications);
        var reversal = Assert.Single(effects.Unapplications!);
        Assert.Equal("PaymentReturn", reversal.ReversedTransactionType);
        Assert.Equal(original, reversal.ReversesTransactionId);
        Assert.Null(reversal.AdvancePositionId);
        Assert.Null(reversal.AdvanceSourceLineId);
        var movement = Assert.Single(effects.DirectMovements);
        Assert.Equal(payable ? 75m : -75m, movement.Amount);
        Assert.Equal(group, movement.GroupId);
        Assert.Equal(moneyAccount, movement.AccountId);
        Assert.Throws<AccountingForbiddenException>(() => AccountingVoucherFinancialValidation.Normalize(
            user with { Permissions = new HashSet<string>() }, request, context, new Uuid7AuralyIdGenerator(TimeProvider.System)));
        var backofficeUser = user with { Permissions = new HashSet<string> {
            PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment } };
        Assert.Throws<AccountingForbiddenException>(() => AccountingVoucherFinancialValidation.Normalize(
            backofficeUser, request, context, new Uuid7AuralyIdGenerator(TimeProvider.System)));
        Assert.Single(AccountingVoucherFinancialValidation.Normalize(backofficeUser, request with { WorkSessionId = null },
            context, new Uuid7AuralyIdGenerator(TimeProvider.System)).DirectMovements);
        foreach (var invalid in new[] {
            request with { VoucherTypeCode = "Disbursement" },
            request with { Lines = [request.Lines[0], request.Lines[1] with { PartyId = Guid.NewGuid() }] },
            request with { Lines = [request.Lines[0], request.Lines[1] with { Debit = payable ? 74m : 0m, Credit = payable ? 0m : 74m }] },
            request with { Lines = [request.Lines[0], request.Lines[1] with { Debit = payable ? 0m : 75m, Credit = payable ? 75m : 0m }] } })
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(
                user, invalid, context, new Uuid7AuralyIdGenerator(TimeProvider.System)));
        if (method == "Transfer")
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,
                request with { Lines = [request.Lines[0], request.Lines[1] with { Reference = null }] }, context,
                new Uuid7AuralyIdGenerator(TimeProvider.System)));
        if (method is "DebitCard" or "CreditCard")
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,
                request with { Lines = [request.Lines[0], request.Lines[1] with { PaymentEvidence = null }] }, context,
                new Uuid7AuralyIdGenerator(TimeProvider.System)));
    }

    [Fact]
    public void Disbursement_without_treasury_is_rejected_while_the_same_pure_accounting_entry_remains_valid()
    {
        var expense = Guid.NewGuid(); var counterpart = Guid.NewGuid();
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), user.BusinessId,
            new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Reclasificación sin dinero", [
                new(expense, null, null, "Gasto", 100m, 0, LineId: Guid.NewGuid()),
                new(counterpart, null, null, "Contrapartida general", 0, 100m, LineId: Guid.NewGuid())],
            PayloadVersion: 4, VoucherTypeCode: "Accounting", CurrencyCode: "COP");
        var context = new VoucherFinancialContext([new(expense, "General", null, null), new(counterpart, "General", null, null)], [], []);
        var ids = new Uuid7AuralyIdGenerator(TimeProvider.System);
        var accounting = AccountingVoucherFinancialValidation.Normalize(user, request, context, ids);
        Assert.Empty(accounting.Payments);
        Assert.Empty(accounting.Positions);
        Assert.Empty(accounting.DirectMovements);
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,
            request with { VoucherTypeCode = "Disbursement" }, context, ids));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Credit_restoration_rejects_an_opening_in_its_group_before_acceptance_but_allows_an_independent_one(bool payable)
    {
        var debtAccount = Guid.NewGuid(); var creditAccount = Guid.NewGuid(); var generalAccount = Guid.NewGuid();
        var party = Guid.NewGuid(); var role = Guid.NewGuid(); var site = Guid.NewGuid();
        var debt = Guid.NewGuid(); var credit = Guid.NewGuid(); var original = Guid.NewGuid(); var group = Guid.NewGuid();
        var kind = payable ? "Payable" : "Receivable";
        var creditKind = payable ? "SupplierCredit" : "CustomerCredit";
        var at = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment });
        var lines = new List<ManualVoucherLineRequest> {
            new(debtAccount, party, null, "Reabrir obligación", payable ? 0 : 150m, payable ? 150m : 0,
                LineId: Guid.NewGuid(), ExistingPosition: new(kind, debt, original), FinancialGroupId: group),
            new(creditAccount, party, null, "Restituir crédito original", payable ? 150m : 0, payable ? 0 : 150m,
                LineId: Guid.NewGuid(), ExistingPosition: new(creditKind, credit), FinancialGroupId: group),
            new(debtAccount, party, null, "Nueva obligación independiente", payable ? 0 : 1m, payable ? 1m : 0,
                Reference: "NUEVA", LineId: Guid.NewGuid(), FinancialGroupId: group, DueDate: at.AddDays(1)),
            new(generalAccount, party, null, "Contrapartida de la nueva obligación", payable ? 1m : 0, payable ? 0 : 1m, LineId: Guid.NewGuid()) };
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), user.BusinessId, at, "MANUAL_VOUCHER", "Operaciones independientes", lines,
            PayloadVersion: 4, VoucherTypeCode: "Accounting", CurrencyCode: "COP");
        var context = new VoucherFinancialContext(
            [new(debtAccount, kind, null, null), new(creditAccount, payable ? "SupplierAdvance" : "CustomerAdvance", null, null), new(generalAccount, "General", null, null)],
            [new(kind, debt, debtAccount, party, role, site, "COP", at, null), new(creditKind, credit, creditAccount, party, role, site, "COP", at, null)],
            [new(party, payable ? role : null, site, payable ? null : role, true)]);
        var ids = new Uuid7AuralyIdGenerator(TimeProvider.System);
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, ids));
        lines[2] = lines[2] with { FinancialGroupId = Guid.NewGuid() };
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, ids);
        Assert.Equal(kind, Assert.Single(effects.Positions).SubledgerKind);
        var reversal = Assert.Single(effects.Unapplications!);
        Assert.Equal("CreditApplication", reversal.ReversedTransactionType);
        Assert.Equal(credit, reversal.AdvancePositionId);
        Assert.Empty(effects.Payments);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Obligation_date_is_compared_in_the_voucher_offset_without_allowing_a_future_local_day(
        bool payable, bool futureLocalDay)
    {
        var accountId = Guid.NewGuid();
        var cashId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var kind = payable ? "Payable" : "Receivable";
        var date = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(-5));
        var originalDate = new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero)
            .AddDays(futureLocalDay ? 1 : 0);
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment });
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), user.BusinessId, date,
            "MANUAL_VOUCHER", "Cruce de la misma fecha local", [
                new(accountId, partyId, null, "Obligación", payable ? 100m : 0m, payable ? 0m : 100m,
                    LineId: Guid.NewGuid(), ExistingPosition: new(kind, positionId), PartySiteId: siteId),
                new(cashId, partyId, null, "Efectivo", payable ? 0m : 100m, payable ? 100m : 0m, LineId: Guid.NewGuid())],
            PayloadVersion: 4, VoucherTypeCode: payable ? "Disbursement" : "Accounting", CurrencyCode: "COP");
        var context = new VoucherFinancialContext(
            [new(accountId, kind, null, null), new(cashId, "Cash", null, null)],
            [new(kind, positionId, accountId, partyId, roleId, siteId, "COP", originalDate, null)], []);
        var ids = new Uuid7AuralyIdGenerator(TimeProvider.System);
        if (futureLocalDay)
        {
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, ids));
            return;
        }
        var effect = Assert.Single(AccountingVoucherFinancialValidation.Normalize(user, request, context, ids).Payments);
        Assert.Equal(positionId, Assert.Single(effect.Allocations).SubledgerId);
        Assert.Equal(100m, Assert.Single(effect.Tenders).Amount);
    }
}
