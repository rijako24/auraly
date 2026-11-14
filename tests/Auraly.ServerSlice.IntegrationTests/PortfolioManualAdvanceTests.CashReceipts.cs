using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public async Task An_incoming_receipt_can_record_general_income_or_a_liability_without_customer_collection_permission(bool liability, bool configured)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))
            Assert.True(active.IsSuccessStatusCode,await active.Content.ReadAsStringAsync());
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var form = configured ? await CreateVoucherCashFormAsync(client,cash,"Caja de ingreso general") : null;
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            liability ? "2393" : "4193",liability ? "Liability" : "Revenue",liability ? "Payable" : "General","Credit");
        var party = liability ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(false);
        using (var payerReads = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("FROM dbo.Parties party",StringComparison.Ordinal) &&
                command.CommandText.Contains("partyRole.",StringComparison.Ordinal)))
        {
            var payerOptions = (await client.GetFromJsonAsync<PartyRoleOptionPage>(
                $"/api/commerce/v1/parties/role-options?role=Any&partyId={party}&page=1&pageSize=10"))!;
            Assert.Equal(party,Assert.Single(payerOptions.Items).PartyId);
            Assert.Equal(party,Assert.Single(payerOptions.Items).RoleId);
            Assert.Equal(1,payerReads.Count);
        }
        if (!liability)
        {
            using var unauthorized = fixture.CreateAdminClient(CatalogPermissionCodes.Read);
            using var forbidden = await unauthorized.GetAsync($"/api/commerce/v1/parties/role-options?role=Any&partyId={party}");
            Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
            using var specificRole = await client.GetAsync($"/api/commerce/v1/parties/role-options?role=Customer&partyId={party}");
            Assert.Equal(HttpStatusCode.Forbidden,specificRole.StatusCode);
        }
        var at = new DateTimeOffset(2026,8,10,12,0,0,TimeSpan.Zero);
        var accepted = await SendReceiptScenarioAsync(client,new(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,
            at,"MANUAL_VOUCHER","Dinero recibido del tercero","RC-TERCERO-02",
            [new(cash.AccountId,liability ? party : null,null,"Entrada real",500,0,LineId:Guid.NewGuid(),
                PaymentEvidence:form is null ? null : VoucherFormEvidence(form)),
             new(counterpart.AccountId,liability ? party : null,null,"Contrapartida",0,500,LineId:Guid.NewGuid(),DueDate:liability ? at.AddDays(30) : null)],
            null,null,MainPartyId:party,PayloadVersion:4,VoucherTypeCode:"CashReceipt"));
        Assert.Equal("CashReceipt",accepted.VoucherTypeCode);
        Assert.Equal("Posted",accepted.Status);
        Assert.Equal(party,accepted.MainPartyId);
        if (!liability) Assert.All(accepted.Lines,line => Assert.Null(line.Value.PartyId));
        Assert.Equal(500m,Assert.Single(accepted.FinancialEffects!.DirectMovements).Amount);
        Assert.Empty(accepted.FinancialEffects.Payments);
        Assert.Equal(configured ? 1 : 0,accepted.AppliedPaymentMethods?.Count ?? 0);
        if (configured) Assert.Equal(cash.AccountId,Assert.Single(accepted.AppliedPaymentMethods!).AccountId);
        Assert.Equal(liability ? 1 : 0,accepted.FinancialEffects.Positions.Count);
        if (liability) Assert.Equal("Payable",Assert.Single(accepted.FinancialEffects.Positions).SubledgerKind);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.Customers WHERE PartyId=@Id",party));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id",accepted.DocumentId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",accepted.DocumentId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cash_receipt_accepts_a_supplier_or_a_party_without_commercial_roles_and_rejects_outflows(bool supplier)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            ReceivablesPermissionCodes.Read, ReceivablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))
            Assert.True(active.IsSuccessStatusCode,await active.Content.ReadAsStringAsync());
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2395","Liability","CustomerAdvance","Credit");
        var party = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(false);
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,
            new DateTimeOffset(2026,8,10,12,0,0,TimeSpan.Zero),"MANUAL_VOUCHER","Anticipo recibido","RC-TERCERO-01",
            [new(cash.AccountId,party,null,"Salida no permitida",0,500,LineId:Guid.NewGuid()),
             new(advance.AccountId,party,null,"Contrapartida",500,0,LineId:Guid.NewGuid())],null,null,
            MainPartyId:party,PayloadVersion:4,VoucherTypeCode:"CashReceipt");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        using var saved = await client.PutAsJsonAsync(path,capture);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var draft = (await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using (var rejected = await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
            Assert.Contains("sin salidas ni vueltos",await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceDocumentId=@Id",capture.DocumentId));
        var corrected = capture with { RowVersion = draft.RowVersion,
            Lines = capture.Lines.Select(line => line with { Debit = line.Credit,Credit = line.Debit }).ToArray() };
        using var correctedSave = await client.PutAsJsonAsync(path,corrected);
        Assert.True(correctedSave.IsSuccessStatusCode,await correctedSave.Content.ReadAsStringAsync());
        draft = (await correctedSave.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var send = await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion));
        Assert.True(send.IsSuccessStatusCode,await send.Content.ReadAsStringAsync());
        var accepted = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal("CashReceipt",accepted.VoucherTypeCode);
        Assert.Equal("Posted",accepted.Status);
        Assert.Equal("Receivable",Assert.Single(accepted.FinancialEffects!.Payments).SubledgerKind);
        Assert.Null(Assert.Single(accepted.FinancialEffects.Positions).CounterpartyRoleId);
        if (!supplier)
        {
            await using var legalConnection = new SqlConnection(fixture.ConnectionString);
            await legalConnection.OpenAsync();
            await using var legalName = new SqlCommand("UPDATE dbo.Parties SET DisplayName=NULL,LegalName=N'Jurídico de anticipos' WHERE PartyId=@Party AND TenantId=@Tenant;",legalConnection);
            legalName.Parameters.AddWithValue("@Party",party);
            legalName.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            Assert.Equal(1,await legalName.ExecuteNonQueryAsync());
            var reopened = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
            Assert.Equal("Jurídico de anticipos",reopened.MainPartyName);
            Assert.All(reopened.Lines,line => Assert.Equal("Jurídico de anticipos",line.PartyName));
            var byName = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind=All&partyId={party}&voucherTypeCode=CashReceipt&search=juridico"))!;
            Assert.Equal("Jurídico de anticipos",Assert.Single(byName.Items).PartyName);
        }
        var available = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=All&partyId={party}&voucherTypeCode=CashReceipt&page=1&pageSize=50"))!;
        Assert.All(available.Items,item => Assert.True(item.SubledgerKind is "Receivable" or "CustomerCredit"));
        Assert.Equal(500m,Assert.Single(available.Items.Where(item => item.SourceDocumentId==capture.DocumentId)).AvailableAmount);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.Customers WHERE PartyId=@Id",party));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",capture.DocumentId));
        using var replay = await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion));
        Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",capture.DocumentId));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public async Task A_receipt_combines_an_existing_advance_and_money_and_credit_only_capture_has_no_cash_receipt_effect(bool supplier, bool configured)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,
            ReceivablesPermissionCodes.Read,ReceivablesPermissionCodes.RegisterPayment,
            PayablesPermissionCodes.Read,PayablesPermissionCodes.RegisterPayment,CatalogPermissionCodes.Read,CatalogPermissionCodes.Update);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))
            Assert.True(active.IsSuccessStatusCode,await active.Content.ReadAsStringAsync());
        var party = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(true);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            supplier ? "1394" : "2394",supplier ? "Asset" : "Liability",supplier ? "SupplierAdvance" : "CustomerAdvance",supplier ? "Debit" : "Credit");
        var debt = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            supplier ? "2294" : "1394",supplier ? "Liability" : "Asset",supplier ? "Payable" : "Receivable",supplier ? "Credit" : "Debit");
        var revenue = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            supplier ? "5194" : "4194",supplier ? "Expense" : "Revenue","General",supplier ? "Debit" : "Credit");
        var paidType = supplier ? "Disbursement" : "CashReceipt";
        var debtKind = supplier ? "Payable" : "Receivable";
        var creditKind = supplier ? "SupplierCredit" : "CustomerCredit";
        var paymentTable = supplier ? "SupplierPayments" : "CustomerPayments";
        var debtTable = supplier ? "Payables" : "Receivables";
        var creditTable = supplier ? "SupplierCredits" : "CustomerCredits";
        var reference = $"MIX-{Guid.NewGuid():N}";
        var at = new DateTimeOffset(2026,8,10,12,0,0,TimeSpan.Zero);
        IReadOnlyList<VoucherDraftLine> ForDirection(IReadOnlyList<VoucherDraftLine> lines) =>
            supplier ? lines.Select(line => line with { Debit = line.Credit,Credit = line.Debit,
                ExistingPosition = line.ExistingPosition is not { } linked ? null : linked with {
                    SubledgerKind = linked.SubledgerKind == "Receivable" ? debtKind : creditKind } }).ToArray() : lines;
        SaveVoucherDraftRequest Capture(string type,IReadOnlyList<VoucherDraftLine> lines) => new(Guid.NewGuid(),
            AccountingManualDocumentTypes.ManualVoucher,at,"MANUAL_VOUCHER","Recibo y cruce de cliente",reference,
            ForDirection(lines),
            null,null,MainPartyId:party,PayloadVersion:4,VoucherTypeCode:type == "CashReceipt" ? paidType : type);
        var invoice = await SendReceiptScenarioAsync(client,Capture("Accounting",[
            new(debt.AccountId,party,null,"Cuenta por cobrar",1_000,0,LineId:Guid.NewGuid(),DueDate:at.AddDays(30)),
            new(revenue.AccountId,party,null,"Contrapartida",0,1_000,LineId:Guid.NewGuid())]));
        var receivable = Assert.Single(invoice.FinancialEffects!.Positions).PositionId;
        var prepaid = await SendReceiptScenarioAsync(client,Capture("CashReceipt",[
            new(cash.AccountId,party,null,"Dinero del anticipo",500,0,LineId:Guid.NewGuid()),
            new(advance.AccountId,party,null,"Anticipo elegido",0,500,LineId:Guid.NewGuid())]));
        var credit = Assert.Single(prepaid.FinancialEffects!.Positions).PositionId;
        using (var collectionsReader = fixture.CreateAdminClient(supplier ? PayablesPermissionCodes.Read : ReceivablesPermissionCodes.Read))
        {
            var pendingPath = $"/api/commerce/v1/accounting/open-positions?subledgerKind=All&partyId={party}&page=1&pageSize=50";
            using (var unrestricted = await collectionsReader.GetAsync(pendingPath))
                Assert.Equal(HttpStatusCode.Forbidden,unrestricted.StatusCode);
            foreach (var expected in new[] { (Kind:debtKind,Id:receivable,Amount:1_000m), (Kind:creditKind,Id:credit,Amount:500m) })
            {
                var selectedPath = $"/api/commerce/v1/accounting/open-positions?subledgerKind={expected.Kind}&subledgerId={expected.Id}&partyId={party}&page=1&pageSize=1&voucherTypeCode={paidType}";
                var pending = (await collectionsReader.GetFromJsonAsync<AccountingOpenPositionPage>(selectedPath))!;
                Assert.Equal(1,pending.TotalCount);
                var item = Assert.Single(pending.Items);
                Assert.Equal(party,item.PartyId);
                Assert.Equal(expected.Kind,item.SubledgerKind);
                Assert.Equal(expected.Id,item.SubledgerId);
                Assert.Equal(expected.Amount,item.AvailableAmount);
                var oppositeType = supplier ? "CashReceipt" : "Disbursement";
                using var excluded = await client.GetAsync(
                    selectedPath.Replace($"voucherTypeCode={paidType}",$"voucherTypeCode={oppositeType}",StringComparison.Ordinal));
                Assert.Equal(HttpStatusCode.BadRequest,excluded.StatusCode);
            }
        }
        var creditGroup = Guid.NewGuid(); var moneyGroup = Guid.NewGuid();
        var firstForm = configured ? await CreateVoucherCashFormAsync(client,cash,"Caja A") : null;
        var secondForm = configured ? await CreateVoucherCashFormAsync(client,cash,"Caja B") : null;
        VoucherDraftLine[] MixedLines(decimal appliedCredit,decimal money) => [
            new(debt.AccountId,party,null,"Cruzar anticipo",0,appliedCredit,LineId:Guid.NewGuid(),
                ExistingPosition:new("Receivable",receivable),FinancialGroupId:creditGroup),
            new(advance.AccountId,party,null,"Anticipo de origen",appliedCredit,0,LineId:Guid.NewGuid(),
                ExistingPosition:new("CustomerCredit",credit),FinancialGroupId:creditGroup),
            new(debt.AccountId,party,null,"Abono con dinero",0,money,LineId:Guid.NewGuid(),
                ExistingPosition:new("Receivable",receivable),FinancialGroupId:moneyGroup),
            new(cash.AccountId,party,null,"Dinero nuevo",configured ? money/2 : money,0,LineId:Guid.NewGuid(),FinancialGroupId:moneyGroup,
                PaymentEvidence:firstForm is null ? null : VoucherFormEvidence(firstForm)),
            .. secondForm is null ? Array.Empty<VoucherDraftLine>() : [new(cash.AccountId,party,null,"Otra forma",money/2,0,
                LineId:Guid.NewGuid(),FinancialGroupId:moneyGroup,PaymentEvidence:VoucherFormEvidence(secondForm))]
        ];
        // Both parts fit separately, but together exceed the debt. The existing
        // acceptance participant must roll back the complete mixed source.
        var oversized = Capture("CashReceipt",MixedLines(400,700));
        var path = $"/api/commerce/v1/accounting/manual/drafts/{oversized.DocumentId}";
        using var saved = await client.PutAsJsonAsync(path,oversized);
        Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var draft = (await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using (var rejected = await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion)))
        {
            Assert.True(rejected.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,await rejected.Content.ReadAsStringAsync());
            Assert.Contains("saldo disponible",await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",oversized.DocumentId));
        Assert.Equal(0,await CountAsync($"SELECT COUNT(*) FROM dbo.{paymentTable} WHERE ParentSourceDocumentId=@Id",oversized.DocumentId));
        Assert.Equal(1_000,await CountAsync($"SELECT CONVERT(int,OutstandingAmount) FROM dbo.{debtTable} WHERE {debtKind}Id=@Id",receivable));
        Assert.Equal(500,await CountAsync($"SELECT CONVERT(int,AvailableAmount) FROM dbo.{creditTable} WHERE {creditKind}Id=@Id",credit));
        var mixed = await SendReceiptScenarioAsync(client,oversized with { RowVersion = draft.RowVersion,Lines = ForDirection(MixedLines(150,100)) });
        Assert.Equal(paidType,mixed.VoucherTypeCode);
        Assert.Equal(150m,Assert.Single(mixed.FinancialEffects!.CreditApplications).Amount);
        Assert.Equal(100m,Assert.Single(mixed.FinancialEffects.Payments).Tenders.Sum(item => item.Amount));
        Assert.Equal(configured ? 2 : 1,Assert.Single(mixed.FinancialEffects.Payments).Tenders.Count);
        Assert.Equal(configured ? 2 : 0,mixed.AppliedPaymentMethods?.Count ?? 0);
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",mixed.DocumentId));
        Assert.Equal(1,await CountAsync($"SELECT COUNT(*) FROM dbo.{paymentTable} WHERE ParentSourceDocumentId=@Id",mixed.DocumentId));
        Assert.Equal(750,await CountAsync($"SELECT CONVERT(int,OutstandingAmount) FROM dbo.{debtTable} WHERE {debtKind}Id=@Id",receivable));
        Assert.Equal(350,await CountAsync($"SELECT CONVERT(int,AvailableAmount) FROM dbo.{creditTable} WHERE {creditKind}Id=@Id",credit));

        var noMoney = Capture("CashReceipt",[
            new(debt.AccountId,party,null,"Cruzar sin dinero",0,100,LineId:Guid.NewGuid(),ExistingPosition:new("Receivable",receivable)),
            new(advance.AccountId,party,null,"Consumir anticipo",100,0,LineId:Guid.NewGuid(),ExistingPosition:new("CustomerCredit",credit))]);
        var purePath = $"/api/commerce/v1/accounting/manual/drafts/{noMoney.DocumentId}";
        using var pureSave = await client.PutAsJsonAsync(purePath,noMoney);
        Assert.True(pureSave.IsSuccessStatusCode,await pureSave.Content.ReadAsStringAsync());
        var pureDraft = (await pureSave.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        var sends = await Task.WhenAll(client.PostAsJsonAsync(purePath+"/send",new SendVoucherDraftRequest(pureDraft.RowVersion)),
            client.PostAsJsonAsync(purePath+"/send",new SendVoucherDraftRequest(pureDraft.RowVersion)));
        foreach (var response in sends)
        {
            using (response)
            {
                Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
                var result = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                Assert.Equal("Accounting",result.VoucherTypeCode);
                Assert.Empty(result.FinancialEffects!.Payments);
                Assert.Empty(result.FinancialEffects.DirectMovements);
                Assert.Single(result.FinancialEffects.CreditApplications);
            }
        }
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",noMoney.DocumentId));
        Assert.Equal(0,await CountAsync($"SELECT COUNT(*) FROM dbo.{paymentTable} WHERE ParentSourceDocumentId=@Id",noMoney.DocumentId));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id",noMoney.DocumentId));
        Assert.Equal(650,await CountAsync($"SELECT CONVERT(int,OutstandingAmount) FROM dbo.{debtTable} WHERE {debtKind}Id=@Id",receivable));
        Assert.Equal(250,await CountAsync($"SELECT CONVERT(int,AvailableAmount) FROM dbo.{creditTable} WHERE {creditKind}Id=@Id",credit));
        var listPath = $"/api/commerce/v1/accounting/documents?from=2026-08-09&to=2026-08-11&partyId={party}&search={Uri.EscapeDataString(reference)}&voucherTypeCode=";
        var receipts = (await client.GetFromJsonAsync<AccountingDocumentPage>(listPath+paidType))!;
        Assert.Equal(2,receipts.TotalCount);
        Assert.DoesNotContain(receipts.Items,item => item.SourceDocumentId==noMoney.DocumentId);
        var accounting = (await client.GetFromJsonAsync<AccountingDocumentPage>(listPath+"Accounting"))!;
        Assert.Equal(2,accounting.TotalCount);
        Assert.Equal("Accounting",Assert.Single(accounting.Items.Where(item => item.SourceDocumentId==noMoney.DocumentId)).VoucherTypeCode);

        // A due instant on September 9 UTC falls on September 10 in this
        // business. Aging must use the real business zone, not the server zone.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var setZone = new SqlCommand("""
            UPDATE dbo.Businesses SET TimeZone=N'Pacific/Kiritimati'
            OUTPUT deleted.TimeZone WHERE BusinessId=@Business AND TenantId=@Tenant;
            """,connection);
        setZone.Parameters.AddWithValue("@Business",fixture.BusinessId);
        setZone.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        var originalZone = (string)(await setZone.ExecuteScalarAsync())!;
        try
        {
            using var reads = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("#OpenPositionCandidates",StringComparison.Ordinal));
            using var scope = fixture.CreateScope();
            var store = ActivatorUtilities.CreateInstance<SqlAccountingStore>(scope.ServiceProvider,
                new ReceiptReferenceClock(new DateTimeOffset(2027,9,11,0,0,0,TimeSpan.Zero)));
            var positions = await store.OpenPositionsAsync(new AccountingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                new HashSet<string> { AccountingPermissionCodes.Read }),
                new("All",1,50,PartyId:party,VoucherTypeCode:paidType,EffectiveOn:new(2026,9,11)),default);
            Assert.Equal(1,reads.Count);
            Assert.Equal(new DateOnly(2027,9,11),positions.ReferenceDate);
            var invoiceRow = Assert.Single(positions.Items.Where(item => item.SubledgerId==receivable));
            Assert.Equal(1_000m,invoiceRow.OriginalAmount);
            Assert.Equal(100m,invoiceRow.PaidAmount);
            Assert.Equal(250m,invoiceRow.OtherMovementsAmount);
            Assert.Equal(650m,invoiceRow.OutstandingAmount);
            Assert.Equal(650m,invoiceRow.AvailableAmount);
            Assert.Equal(366,invoiceRow.OverdueDays);
            var advanceRow = Assert.Single(positions.Items.Where(item => item.SubledgerId==credit));
            Assert.Null(advanceRow.PaidAmount);
            Assert.Null(advanceRow.OverdueDays);
            Assert.Equal(250m,advanceRow.OtherMovementsAmount);
        }
        finally
        {
            await using var restore = new SqlCommand("UPDATE dbo.Businesses SET TimeZone=@Zone WHERE BusinessId=@Business AND TenantId=@Tenant;",connection);
            restore.Parameters.AddWithValue("@Zone",originalZone);
            restore.Parameters.AddWithValue("@Business",fixture.BusinessId);
            restore.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            Assert.Equal(1,await restore.ExecuteNonQueryAsync());
        }
    }

    private sealed class ReceiptReferenceClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<VoucherDraftView> SendReceiptScenarioAsync(HttpClient client,SaveVoucherDraftRequest capture)
    {
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        using var save = await client.PutAsJsonAsync(path,capture);
        Assert.True(save.IsSuccessStatusCode,await save.Content.ReadAsStringAsync());
        var draft = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var send = await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion));
        Assert.True(send.IsSuccessStatusCode,await send.Content.ReadAsStringAsync());
        return (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
    }
}
