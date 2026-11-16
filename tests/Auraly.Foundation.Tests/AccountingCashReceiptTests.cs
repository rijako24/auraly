using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Receivables;

namespace Auraly.Foundation.Tests;

public sealed class AccountingCashReceiptTests
{
    [Theory]
    [InlineData(1, 1, false, false)]
    [InlineData(1, 2, false, false)]
    [InlineData(2, 1, false, false)]
    [InlineData(2, 2, false, false)]
    [InlineData(2, 2, true, false)]
    [InlineData(2, 2, false, true)]
    public void New_receipts_require_unambiguous_invoice_attribution_without_changing_general_vouchers(
        int invoiceCount, int formCount, bool separateGroups, bool generalVoucher)
    {
        var (user, request, context) = Receipt(true, false);
        var first = request.Lines[0] with { Credit = invoiceCount == 1 ? 250m : 125m, FinancialGroupId = request.VoucherId };
        var cash = request.Lines[1] with { Debit = formCount == 1 ? 250m : 125m, FinancialGroupId = request.VoucherId,
            PaymentEvidence = new(PaymentMethodId: Guid.NewGuid(), PaymentMethodVersion: 1, PaymentMethodTypeCode: "Cash", PaymentMethodCode: "C1", PaymentMethodName: "Caja uno") };
        var lines = new List<ManualVoucherLineRequest> { first, cash };
        if (invoiceCount == 2)
        {
            var position = context.Positions[0] with { PositionId = Guid.NewGuid() };
            context = context with { Positions = [..context.Positions, position] };
            lines.Add(first with { LineId = Guid.NewGuid(), ExistingPosition = new("Receivable", position.PositionId),
                FinancialGroupId = separateGroups ? position.PositionId : request.VoucherId });
        }
        if (formCount == 2)
        {
            var account = Guid.NewGuid();
            context = context with { Accounts = [..context.Accounts, new(account, "Cash", "Cash", null)] };
            lines.Add(cash with { AccountId = account, LineId = Guid.NewGuid(),
                FinancialGroupId = separateGroups ? context.Positions[1].PositionId : request.VoucherId,
                PaymentEvidence = new(PaymentMethodId: Guid.NewGuid(), PaymentMethodVersion: 1, PaymentMethodTypeCode: "Cash", PaymentMethodCode: "C2", PaymentMethodName: "Caja dos") });
        }
        request = request with { Lines = lines, VoucherTypeCode = generalVoucher ? "Accounting" : "CashReceipt" };
        if (!generalVoucher && invoiceCount > 1 && formCount > 1)
        {
            var error = Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids()));
            Assert.Contains("una sola forma de pago", error.Message);
            return;
        }
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids());
        var payment = Assert.Single(effects.Payments);
        Assert.Equal(invoiceCount, payment.Allocations.Count);
        Assert.Equal(formCount, payment.Tenders.Count);
        Assert.Equal(250m, payment.Allocations.Sum(item => item.Amount));
    }

    [Fact]
    public void An_explicit_reduction_can_share_a_credit_cross_without_creating_cash_or_a_second_advance()
    {
        var (user, request, context) = Receipt(true, false);
        var group = Guid.NewGuid(); var general = Guid.NewGuid(); var concept = Guid.NewGuid(); var creditAccount = Guid.NewGuid(); var creditId = Guid.NewGuid();
        var target = request.Lines[0] with { FinancialGroupId = group };
        request = request with { Lines = [target,
            new(general, request.MainPartyId, null, "Reducción", 100, 0, LineId: Guid.NewGuid(), FinancialGroupId: group,
                FinancialConceptId: concept, FinancialConceptTargetLineId: target.LineId),
            new(creditAccount, request.MainPartyId, null, "Anticipo existente", 150, 0, LineId: Guid.NewGuid(),
                FinancialGroupId: group, ExistingPosition: new("CustomerCredit", creditId))] };
        context = context with { Accounts = [..context.Accounts, new(general, "General", null, null), new(creditAccount, "CustomerAdvance", null, null)],
            Concepts = [new(concept, "DESC", "Descuento")], Positions = [..context.Positions,
                new("CustomerCredit", creditId, creditAccount, request.MainPartyId!.Value, context.Parties[0].CustomerId,
                    context.Parties[0].DefaultPartySiteId, "COP", request.OccurredAt.AddDays(-1), null)] };
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids());
        Assert.Equal(100, Assert.Single(effects.Positions).Amount);
        Assert.Equal(150, Assert.Single(effects.CreditApplications).Amount);
        Assert.Empty(effects.Payments); Assert.Empty(effects.DirectMovements);
        Assert.Equal("Accounting", AccountingVoucherFinancialValidation.ResolveVoucherType("CashReceipt", effects));
        Assert.Throws<AccountingForbiddenException>(() => AccountingVoucherFinancialValidation.Normalize(
            user with { Permissions = new HashSet<string>() }, request, context, Ids()));
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, 250)]
    [InlineData(true, 250)]
    public void Explicit_concept_reductions_target_one_invoice_and_only_collect_the_remaining_money(bool supplier, decimal reduction)
    {
        var (user, request, context) = Receipt(true, false);
        var group = Guid.NewGuid(); var account = Guid.NewGuid(); var concept = Guid.NewGuid();
        var target = request.Lines[0] with { FinancialGroupId = group };
        var adjustment = new ManualVoucherLineRequest(account, request.MainPartyId, null, "Descuento elegido", reduction, 0,
            LineId: Guid.NewGuid(), FinancialGroupId: group, FinancialConceptId: concept, FinancialConceptTargetLineId: target.LineId);
        request = request with { Lines = reduction == 250 ? [target, adjustment] : [target, adjustment,
            request.Lines[1] with { Debit = 250 - reduction, FinancialGroupId = group }] };
        context = context with { Accounts = [..context.Accounts, new(account, "General", null, null)], Concepts = [new(concept, "DESC", "Descuento")] };
        if (supplier)
        {
            var role = Guid.NewGuid();
            user = user with { Permissions = new HashSet<string> { Auraly.Contracts.Payables.PayablesPermissionCodes.RegisterPayment } };
            request = request with { VoucherTypeCode = "Disbursement", Lines = request.Lines.Select(line => line with {
                Debit = line.Credit, Credit = line.Debit,
                ExistingPosition = line.ExistingPosition is null ? null : line.ExistingPosition with { SubledgerKind = "Payable" } }).ToArray() };
            context = context with { Accounts = context.Accounts.Select(item => item.Function == "Receivable" ? item with { Function = "Payable" } : item).ToArray(),
                Positions = context.Positions.Select(item => item with { Kind = "Payable", RoleId = role }).ToArray(),
                Parties = context.Parties.Select(item => item with { SupplierId = role }).ToArray() };
        }
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids());
        var effect = Assert.Single(effects.Positions);
        Assert.Equal("Adjustment", effect.EffectKind); Assert.Equal(reduction, effect.Amount);
        Assert.Equal(target.LineId, effect.TargetSourceLineId); Assert.Equal(adjustment.LineId, effect.SourceLineId);
        Assert.Equal(target.AccountId, effect.AccountId); Assert.Empty(effects.DirectMovements); Assert.Empty(effects.CreditApplications);
        if (reduction == 250) Assert.Empty(effects.Payments);
        else Assert.Equal(250 - reduction, Assert.Single(Assert.Single(effects.Payments).Allocations).Amount);
        Assert.Equal(reduction == 250 ? "Accounting" : request.VoucherTypeCode,
            AccountingVoucherFinancialValidation.ResolveVoucherType(request.VoucherTypeCode, effects));
        Assert.Throws<AccountingForbiddenException>(() => AccountingVoucherFinancialValidation.Normalize(
            user with { Permissions = new HashSet<string>() }, request, context, Ids()));
    }

    [Theory]
    [InlineData("unknownTarget")]
    [InlineData("self")]
    [InlineData("otherGroup")]
    [InlineData("otherParty")]
    [InlineData("overdraw")]
    [InlineData("wrongSide")]
    [InlineData("noConcept")]
    [InlineData("reversal")]
    public void A_concept_cannot_reduce_an_unrelated_or_incompatible_invoice(string fault)
    {
        var (user, request, context) = Receipt(true, false);
        var group = Guid.NewGuid(); var account = Guid.NewGuid(); var concept = Guid.NewGuid(); var lineId = Guid.NewGuid();
        var target = request.Lines[0] with { FinancialGroupId = group };
        if (fault == "reversal") target = target with { ExistingPosition = target.ExistingPosition! with { ReversesTransactionId = Guid.NewGuid() } };
        var adjustment = new ManualVoucherLineRequest(account, fault == "otherParty" ? Guid.NewGuid() : request.MainPartyId, null, "Ajuste",
            fault == "wrongSide" ? 0 : fault == "overdraw" ? 251 : 10, fault == "wrongSide" ? 10 : 0, LineId: lineId,
            FinancialGroupId: fault == "otherGroup" ? Guid.NewGuid() : group, FinancialConceptId: fault == "noConcept" ? null : concept,
            FinancialConceptTargetLineId: fault == "self" ? lineId : fault == "unknownTarget" ? Guid.NewGuid() : target.LineId);
        request = request with { Lines = [target, adjustment, request.Lines[1] with { Debit = 240, FinancialGroupId = group }] };
        context = context with { Accounts = [..context.Accounts, new(account, "General", null, null)], Concepts = [new(concept, "DESC", "Descuento")] };
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids()));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "missingConcept")]
    [InlineData(true, "missingConcept")]
    [InlineData(false, "otherParty")]
    [InlineData(true, "otherParty")]
    [InlineData(false, "unclassifiedDifference")]
    [InlineData(true, "unclassifiedDifference")]
    [InlineData(false, "debtReduction")]
    [InlineData(true, "debtReduction")]
    public void An_explicit_additional_concept_and_invoice_share_one_payment_without_an_advance(bool supplier, string? fault)
    {
        var (user, request, context) = Receipt(true, false);
        var group = Guid.NewGuid(); var account = Guid.NewGuid(); var concept = Guid.NewGuid();
        var debt = request.Lines[0] with { Credit = 216_748.03m, FinancialGroupId = group };
        var cash = request.Lines[1] with { Debit = 216_800m, FinancialGroupId = group };
        var extra = new ManualVoucherLineRequest(account, fault == "otherParty" ? Guid.NewGuid() : request.MainPartyId,
            null, "Ajuste elegido", 0, 51.97m, LineId: Guid.NewGuid(), FinancialGroupId: group,
            FinancialConceptId: fault == "missingConcept" ? null : concept);
        if (fault == "debtReduction")
        {
            extra = extra with { Debit = 51.97m, Credit = 0m };
            cash = cash with { Debit = 216_696.06m };
        }
        request = request with { Lines = fault == "unclassifiedDifference" ? [debt, cash] : [debt, cash, extra] };
        context = context with { Accounts = [.. context.Accounts, new(account, "General", null, null)],
            Concepts = [new(concept, "AJUSTE", "Ajuste explícito")] };
        if (supplier)
        {
            var role = Guid.NewGuid();
            user = user with { Permissions = new HashSet<string> { Auraly.Contracts.Payables.PayablesPermissionCodes.RegisterPayment } };
            request = request with { VoucherTypeCode = "Disbursement", Lines = request.Lines.Select(line => line with {
                Debit = line.Credit, Credit = line.Debit,
                ExistingPosition = line.ExistingPosition is null ? null : line.ExistingPosition with { SubledgerKind = "Payable" } }).ToArray() };
            context = context with {
                Accounts = context.Accounts.Select(item => item.Function == "Receivable" ? item with { Function = "Payable" } : item).ToArray(),
                Positions = context.Positions.Select(item => item with { Kind = "Payable", RoleId = role }).ToArray(),
                Parties = context.Parties.Select(item => item with { SupplierId = role }).ToArray() };
        }
        if (fault is not null)
        {
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids()));
            return;
        }
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids());
        var payment = Assert.Single(effects.Payments);
        Assert.Equal(216_748.03m, Assert.Single(payment.Allocations).Amount);
        Assert.Equal(216_800m, Assert.Single(payment.Tenders).Amount);
        Assert.Equal(51.97m, payment.AdditionalConceptAmount);
        Assert.Equal(0, payment.AdvanceAmount);
        Assert.Empty(effects.Positions);
        Assert.Empty(effects.DirectMovements);
        Assert.Equal(concept, Assert.Single(effects.Concepts!).FinancialConceptId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("empty")]
    [InlineData("payment")]
    public void A_selected_concept_records_one_identity_and_never_infers_an_advance_from_its_name(string? fault)
    {
        var (user,request,context)=Receipt(true,false);
        var revenue=Guid.NewGuid();var concept=Guid.NewGuid();var cash=request.Lines[1];
        var selected=fault=="unknown"?Guid.NewGuid():fault=="empty"?Guid.Empty:concept;
        request=request with {Lines=[
            new(revenue,request.MainPartyId,null,"Contrapartida uno",0,150m,LineId:Guid.NewGuid(),FinancialConceptId:selected),
            new(revenue,request.MainPartyId,null,"Contrapartida dos",0,100m,LineId:Guid.NewGuid(),FinancialConceptId:selected),cash]};
        if(fault=="payment")request=request with {Lines=[request.Lines[0] with {PaymentEvidence=new(TenderedAmount:150m)},..request.Lines.Skip(1)]};
        context=context with {Accounts=[..context.Accounts,new(revenue,"General",null,null)],Positions=[],
            Concepts=[new(concept,"ANTICIPO-NOMBRE","Anticipo en el nombre, cuenta general")]};
        if(fault is not null)
        {
            Assert.Throws<AccountingValidationException>(()=>AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids()));return;
        }
        var result=AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids());
        Assert.Equal(concept,Assert.Single(result.Concepts!).FinancialConceptId);
        Assert.Empty(result.Positions);Assert.Empty(result.Payments);
        Assert.Equal(250m,result.DirectMovements.Sum(item=>item.Amount));
    }

    [Fact]
    public void Missing_financial_concept_fields_preserve_the_legacy_wire_shape()
    {
        var line=new ManualVoucherLineRequest(Guid.NewGuid(),null,null,"Anterior",100,0);
        Assert.DoesNotContain("FinancialConcept",System.Text.Json.JsonSerializer.Serialize(line));
        Assert.DoesNotContain("FinancialConcept", System.Text.Json.JsonSerializer.Serialize(new VoucherDraftLine(Guid.NewGuid(), null, null, "Anterior", 100, 0)));
        Assert.DoesNotContain("TargetSourceLineId", System.Text.Json.JsonSerializer.Serialize(new VoucherFinancialPositionEffect(
            Guid.NewGuid(), Guid.NewGuid(), "Open", "Receivable", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, 100, null)));
        Assert.DoesNotContain("Concepts",System.Text.Json.JsonSerializer.Serialize(new VoucherFinancialEffects(Guid.NewGuid(),[],[],[],[])));
        Assert.DoesNotContain("AdditionalConceptAmount", System.Text.Json.JsonSerializer.Serialize(
            new VoucherFinancialPaymentGroup(Guid.NewGuid(), Guid.NewGuid(), "Receivable", Guid.NewGuid(), null, null, [], [])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("duplicate")]
    [InlineData("incomplete")]
    [InlineData("mixed")]
    [InlineData("change")]
    public void Configured_forms_share_the_native_tender_rules_for_one_invoice(string? fault)
    {
        var (user, request, context) = Receipt(true, false);
        var debt = request.Lines[0]; var money = request.Lines[1];
        var firstForm = new VoucherPaymentEvidence(PaymentMethodId:Guid.NewGuid(),PaymentMethodTypeCode:"Cash",
            PaymentMethodVersion:1,PaymentMethodCode:"CAJA-A",PaymentMethodName:"Caja A");
        var secondForm = firstForm with { PaymentMethodId=Guid.NewGuid(),PaymentMethodCode="CAJA-B",PaymentMethodName="Caja B" };
        if (fault == "duplicate") secondForm = firstForm;
        if (fault == "incomplete") secondForm = secondForm with { PaymentMethodVersion=null };
        if (fault == "change") secondForm = secondForm with { TenderedAmount=200m };
        request = request with { Lines = [debt with { Credit=250m },
            money with { Debit=125m,PaymentEvidence=firstForm },
            money with { LineId=Guid.NewGuid(),Debit=125m,PaymentEvidence=fault == "mixed" ? null : secondForm }] };
        if (fault is not null)
        {
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids()));
            return;
        }
        var group = Assert.Single(AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids()).Payments);
        Assert.Single(group.Allocations);
        Assert.Equal(2,group.Tenders.Count);
        Assert.Equal(250m,group.Tenders.Sum(item => item.Amount));
        Assert.Equal(new[] { firstForm.PaymentMethodId,secondForm.PaymentMethodId },group.Tenders.Select(item => item.PaymentMethodId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_general_receipt_accepts_two_forms_of_the_same_type_and_rejects_using_one_twice(bool duplicate)
    {
        var (user,request,context) = Receipt(true,false);
        var revenue = Guid.NewGuid(); var cash = request.Lines[1];
        var form = new VoucherPaymentEvidence(PaymentMethodId:Guid.NewGuid(),PaymentMethodTypeCode:"Cash",
            PaymentMethodVersion:1,PaymentMethodCode:"CAJA",PaymentMethodName:"Caja configurada");
        request = request with { Lines = [new(revenue,request.MainPartyId,null,"Ingreso",0,250m,LineId:Guid.NewGuid()),
            cash with { Debit=125m,PaymentEvidence=form },
            cash with { LineId=Guid.NewGuid(),Debit=125m,PaymentEvidence=duplicate ? form : form with { PaymentMethodId=Guid.NewGuid() } }] };
        context = context with { Accounts=[.. context.Accounts,new(revenue,"General",null,null)],Positions=[] };
        if (duplicate)
            Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids()));
        else
        {
            var effects = AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids());
            Assert.Empty(effects.Payments);
            Assert.Equal(250m,effects.DirectMovements.Sum(item => item.Amount));
            Assert.Equal(2,effects.DirectMovements.Count);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_receipt_uses_the_existing_payment_and_advance_effects(bool invoice, bool advance)
    {
        var (user, request, context) = Receipt(invoice, advance);
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids());
        var payment = Assert.Single(effects.Payments);
        Assert.Equal("Receivable", payment.SubledgerKind);
        Assert.Equal(request.MainPartyId, payment.PartyId);
        Assert.Equal(invoice ? 250m : 0m, payment.Allocations.Sum(line => line.Amount));
        Assert.Equal(advance ? 500m : 0m, payment.AdvanceAmount);
        Assert.Equal((invoice ? 250m : 0m) + (advance ? 500m : 0m), Assert.Single(payment.Tenders).Amount);
        Assert.Equal(advance ? 1 : 0, effects.Positions.Count);
        Assert.All(effects.Positions, position => Assert.Equal("CustomerCredit", position.SubledgerKind));
        Assert.Empty(effects.DirectMovements);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Administrative_receipts_and_disbursements_record_effective_cash_without_change(bool outgoing, bool configured)
    {
        var (user, request, context) = Receipt(false, false);
        var counterpart = Guid.NewGuid();
        var evidence = new VoucherPaymentEvidence(TenderedAmount: 110m);
        if (configured) evidence = evidence with { PaymentMethodId = Guid.NewGuid(), PaymentMethodTypeCode = "Cash",
            PaymentMethodVersion = 1, PaymentMethodCode = "CAJA", PaymentMethodName = "Caja del tenant" };
        request = request with { VoucherTypeCode = outgoing ? "Disbursement" : "CashReceipt",
            Lines = [request.Lines[0] with { Debit = outgoing ? 0 : 100, Credit = outgoing ? 100 : 0, PaymentEvidence = evidence },
                new(counterpart, request.MainPartyId, null, "Contrapartida", outgoing ? 100 : 0, outgoing ? 0 : 100, LineId: Guid.NewGuid())] };
        context = context with { Accounts = [.. context.Accounts, new(counterpart, "General", null, null)] };
        var error = Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids()));
        Assert.Contains("sin vueltos", error.Message);
    }

    [Theory]
    [InlineData("outgoing", "sin salidas ni vueltos")]
    [InlineData("change", "sin vueltos")]
    [InlineData("cardDebt", "entrada real de dinero")]
    [InlineData("noMoney", "no genera otra entrada de caja")]
    public void A_receipt_does_not_create_outflows_or_change(string fault, string message)
    {
        var (user, request, context) = Receipt(true, false);
        if (fault == "outgoing") request = request with { Lines = request.Lines.Select((line, index) => index == 1 ? line with { Debit = 0, Credit = 250 } : line).ToArray() };
        if (fault == "change") request = request with { Lines = request.Lines.Select((line, index) => index == 1 ? line with { PaymentEvidence = new(TenderedAmount: 300) } : line).ToArray() };
        if (fault == "noMoney") request = request with { Lines = request.Lines.Take(1).ToArray() };
        if (fault == "cardDebt")
        {
            context = context with { Accounts = context.Accounts.Select(account => account.AccountId == request.Lines[1].AccountId
                ? account with { Function = "CreditCardLiability", MethodCode = "CreditCard" } : account).ToArray() };
            request = request with { Lines = request.Lines.Select((line, index) => index == 1
                ? line with { PaymentEvidence = new(CardFranchiseCode: "VISA", ApprovalNumber: "APPROVAL") } : line).ToArray() };
        }
        var error = Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request, context, Ids()));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void A_receipt_applied_to_receivables_requires_collection_permission()
    {
        var (user, request, context) = Receipt(true, false);
        Assert.Throws<AccountingForbiddenException>(() => AccountingVoucherFinancialValidation.Normalize(
            user with { Permissions = new HashSet<string>() }, request, context, Ids()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Any_party_can_be_the_payer_but_a_receipt_cannot_collect_another_partys_positions(bool invoice)
    {
        var (user,request,context) = Receipt(invoice,!invoice);
        var other = Guid.NewGuid();
        context = context with { Parties = [.. context.Parties,new(other,null,null,null,null)] };
        var error = Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(
            user,request with { MainPartyId = other },context,Ids()));
        Assert.Contains("tercero principal",error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_explicit_advance_can_be_received_from_a_supplier_or_a_party_without_commercial_roles(bool supplier)
    {
        var (user,request,context) = Receipt(false,true);
        context = context with { Parties = context.Parties.Select(party => party with {
            CustomerId = null,CustomerIsActive = null,SupplierId = supplier ? Guid.NewGuid() : null }).ToArray() };
        var effects = AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids());
        var credit = Assert.Single(effects.Positions);
        Assert.Equal(request.MainPartyId,credit.PartyId);
        Assert.Null(credit.CounterpartyRoleId);
        Assert.Null(credit.PartySiteId);
        Assert.Equal(500m,credit.Amount);
        var payment = Assert.Single(effects.Payments);
        Assert.Null(payment.CounterpartyRoleId);
        Assert.Equal(request.MainPartyId,payment.PartyId);
        Assert.Equal(500m,Assert.Single(payment.Tenders).Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_general_receipt_does_not_require_a_customer_role_or_collection_permission(bool liability)
    {
        var (user,request,context) = Receipt(true,false);
        var general = Guid.NewGuid();
        request = request with { Reference = "RC-TERCERO-UNIT", Lines = request.Lines.Select((line,index) => index == 0
            ? line with { AccountId = general,ExistingPosition = null,DueDate = liability ? request.OccurredAt.AddDays(30) : null } : line).ToArray() };
        context = context with { Accounts = [.. context.Accounts,new(general,liability ? "Payable" : "General",null,null)],Positions = [],
            Parties = context.Parties.Select(party => party with { CustomerId = null,CustomerIsActive = null,
                SupplierId = liability ? Guid.NewGuid() : null }).ToArray() };
        var effects = AccountingVoucherFinancialValidation.Normalize(user with { Permissions = new HashSet<string>() },request,context,Ids());
        Assert.Equal(250m,Assert.Single(effects.DirectMovements).Amount);
        Assert.Empty(effects.Payments);
        Assert.Equal(liability ? 1 : 0,effects.Positions.Count);
        if (liability) Assert.Equal("Payable",Assert.Single(effects.Positions).SubledgerKind);
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(
            user,request with { Lines = [request.Lines[0],request.Lines[1] with { Debit = 249m }] },context,Ids()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_existing_advance_and_new_money_share_the_receipt_without_another_cash_entry(bool newMoney, bool supplier)
    {
        var (user, request, context) = Receipt(true, false);
        var original = request.Lines[0];
        var cash = request.Lines[1];
        var creditAccount = Guid.NewGuid(); var creditId = Guid.NewGuid();
        var creditGroup = Guid.NewGuid(); var moneyGroup = Guid.NewGuid();
        var amount = newMoney ? 150m : 250m;
        var lines = new List<ManualVoucherLineRequest> {
            original with { Credit = amount, FinancialGroupId = creditGroup },
            new(creditAccount,request.MainPartyId,null,"Anticipo seleccionado",amount,0,LineId:Guid.NewGuid(),
                ExistingPosition:new("CustomerCredit",creditId),FinancialGroupId:creditGroup)
        };
        if (newMoney)
        {
            lines.Add(original with { Credit = 100,LineId = Guid.NewGuid(),FinancialGroupId = moneyGroup });
            lines.Add(cash with { Debit = 100,FinancialGroupId = moneyGroup });
        }
        request = request with { Lines = lines };
        context = context with { Accounts = [.. context.Accounts,new(creditAccount,"CustomerAdvance",null,null)],
            Positions = [.. context.Positions,new("CustomerCredit",creditId,creditAccount,request.MainPartyId!.Value,
                context.Parties[0].CustomerId,context.Parties[0].DefaultPartySiteId,"COP",request.OccurredAt.AddDays(-1),null)] };
        var requestedType = supplier ? "Disbursement" : "CashReceipt";
        if (supplier)
        {
            var role = Guid.NewGuid();
            user = user with { Permissions = new HashSet<string> { Auraly.Contracts.Payables.PayablesPermissionCodes.RegisterPayment } };
            request = request with { VoucherTypeCode = requestedType, Lines = request.Lines.Select(line => line with {
                Debit = line.Credit, Credit = line.Debit,
                ExistingPosition = line.ExistingPosition is not { } linked ? null : linked with {
                    SubledgerKind = linked.SubledgerKind == "Receivable" ? "Payable" : "SupplierCredit" } }).ToArray() };
            context = context with {
                Accounts = context.Accounts.Select(account => account with { Function = account.Function switch {
                    "Receivable" => "Payable", "CustomerAdvance" => "SupplierAdvance", _ => account.Function } }).ToArray(),
                Positions = context.Positions.Select(position => position with {
                    Kind = position.Kind == "Receivable" ? "Payable" : "SupplierCredit", RoleId = role }).ToArray(),
                Parties = context.Parties.Select(party => party with { SupplierId = role }).ToArray() };
        }
        var effects = AccountingVoucherFinancialValidation.Normalize(user,request,context,Ids());
        Assert.Equal(amount,Assert.Single(effects.CreditApplications).Amount);
        Assert.Equal(newMoney ? 1 : 0,effects.Payments.Count);
        Assert.Equal(newMoney ? 100m : 0m,effects.Payments.SelectMany(payment => payment.Tenders).Sum(tender => tender.Amount));
        Assert.Empty(effects.DirectMovements);
        Assert.Empty(effects.Positions);
        Assert.Equal(newMoney ? requestedType : "Accounting",AccountingVoucherFinancialValidation.ResolveVoucherType(requestedType,effects));
        Assert.Equal("Accounting",AccountingVoucherFinancialValidation.ResolveVoucherType("Accounting",effects));
    }

    private static (AccountingUserIdentity, ConfirmManualAccountingVoucherRequest, VoucherFinancialContext) Receipt(bool invoice, bool advance)
    {
        var party = Guid.NewGuid(); var customer = Guid.NewGuid(); var site = Guid.NewGuid();
        var cash = Guid.NewGuid(); var debt = Guid.NewGuid(); var prepaid = Guid.NewGuid(); var position = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { ReceivablesPermissionCodes.RegisterPayment });
        var lines = new List<ManualVoucherLineRequest>();
        if (invoice) lines.Add(new(debt, party, null, "Abono", 0, 250, LineId: Guid.NewGuid(),
            ExistingPosition: new("Receivable", position)));
        lines.Add(new(cash, party, null, "Dinero recibido", (invoice ? 250 : 0) + (advance ? 500 : 0), 0, LineId: Guid.NewGuid()));
        if (advance) lines.Add(new(prepaid, party, null, "Anticipo elegido", 0, 500, LineId: Guid.NewGuid()));
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), user.BusinessId, at, "MANUAL_VOUCHER", "Recibo", lines,
            MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: "CashReceipt", CurrencyCode: "COP");
        var context = new VoucherFinancialContext([new(cash, "Cash", "Cash", null), new(debt, "Receivable", null, null),
            new(prepaid, "CustomerAdvance", null, null)],
            invoice ? [new("Receivable", position, debt, party, customer, site, "COP", at.AddDays(-1), null)] : [],
            [new(party, null, site, customer, true)]);
        return (user, request, context);
    }

    private static Uuid7AuralyIdGenerator Ids() => new(TimeProvider.System);
}
