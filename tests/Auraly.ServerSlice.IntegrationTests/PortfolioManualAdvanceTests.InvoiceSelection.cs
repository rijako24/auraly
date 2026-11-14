using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.BuildingBlocks.Domain.Time;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrative_invoice_selection_filters_before_paging_and_keeps_one_bounded_reader(bool supplier)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            ReceivablesPermissionCodes.Read, ReceivablesPermissionCodes.RegisterPayment,
            PayablesPermissionCodes.Read, PayablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared"))) active.EnsureSuccessStatusCode();
        var party = supplier ? await CreateSelectionSupplierPartyAsync() : await CreateCustomerPartyAsync(true);
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            supplier ? "2294" : "1394",supplier ? "Liability" : "Asset",supplier ? "Payable" : "Receivable",supplier ? "Credit" : "Debit");
        var general = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            supplier ? "5194" : "4194",supplier ? "Expense" : "Revenue","General",supplier ? "Debit" : "Credit");
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var at = new DateTimeOffset(2026,8,10,12,0,0,TimeSpan.Zero);
        var targetLine = Guid.NewGuid();
        var lines = Enumerable.Range(0,41).Select(index => new VoucherDraftLine(control.AccountId,party,null,$"Obligación {index}",
            supplier ? 0 : index == 0 ? 600 : index == 1 ? 400 : 10,
            supplier ? index == 0 ? 600 : index == 1 ? 400 : 10 : 0,
            $"SEL-{index}-{Guid.NewGuid():N}",index == 0 ? targetLine : Guid.NewGuid(), DueDate:at.AddDays(index == 0 ? 30 : 1))).ToList();
        lines.Add(new(general.AccountId,party,null,"Contrapartida",supplier ? 1390 : 0,supplier ? 0 : 1390,LineId:Guid.NewGuid()));
        var opened = await Send(new(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,at,"MANUAL_VOUCHER","Selección paginada",null,
            lines,null,null,MainPartyId:party,PayloadVersion:4,VoucherTypeCode:"Accounting"));
        var target = opened.FinancialEffects!.Positions.Single(item => item.SourceLineId == targetLine);
        await Send(new(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,at,"MANUAL_VOUCHER","Abono",null,
            [new(control.AccountId,party,null,"Aplicación",supplier ? 100 : 0,supplier ? 0 : 100,LineId:Guid.NewGuid(),
                ExistingPosition:new(supplier ? "Payable" : "Receivable",target.PositionId)),
             new(cash.AccountId,party,null,"Dinero",supplier ? 0 : 100,supplier ? 100 : 0,LineId:Guid.NewGuid())],
            null,null,MainPartyId:party,PayloadVersion:4,VoucherTypeCode:supplier ? "Disbursement" : "CashReceipt"));
        using var scope = fixture.CreateScope();
        var accounting = scope.ServiceProvider.GetRequiredService<AccountingService>();
        await accounting.ConfirmAccountAdjustmentAsync(new(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend}),
            new(Guid.NewGuid(),fixture.BusinessId,supplier ? "Payable" : "Receivable",target.PositionId,
                "Decrease",50,general.AccountId,null,at,"ACCOUNT_ADJUSTMENT","Ajuste explícito"));
        var path = supplier ? "payables" : "receivables";
        var idKey = supplier ? "payableId" : "receivableId";
        var partyKey = supplier ? "supplierId" : "customerId";
        var root = $"/api/commerce/v1/{path}?dateRangeVersion=2&pageSize=20&outstandingOnly=true&{partyKey}={target.CounterpartyRoleId}";
        var firstRead=Stopwatch.StartNew();
        var full = (await client.GetFromJsonAsync<JsonObject>(root+"&page=1"))!;
        var coldMilliseconds=firstRead.Elapsed.TotalMilliseconds;
        Assert.True(coldMilliseconds<2000,$"The first page exceeded 2 s: {coldMilliseconds:F1} ms");
        Assert.Equal(41,full["totalCount"]!.GetValue<int>());
        Assert.DoesNotContain(full["items"]!.AsArray(),item => item![idKey]!.GetValue<Guid>() == target.PositionId);
        var selected = (await client.GetFromJsonAsync<JsonObject>($"{root}&page=1&{idKey}={target.PositionId}"))!;
        Assert.Equal(1,selected["totalCount"]!.GetValue<int>());
        var row = Assert.Single(selected["items"]!.AsArray())!;
        Assert.Equal(600,row["originalAmount"]!.GetValue<decimal>());
        Assert.Equal(100,row["paidAmount"]!.GetValue<decimal>());
        Assert.Equal(50,row["adjustmentAmount"]!.GetValue<decimal>());
        Assert.Equal(450,row["outstandingAmount"]!.GetValue<decimal>());
        Assert.Equal("ManualAccountingVoucher",row["sourceDocumentType"]!.GetValue<string>());
        Assert.Equal(at,row["issuedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal("2026-08-10",row["issuedOn"]!.GetValue<string>());
        Assert.Equal("2026-09-09",row["dueOn"]!.GetValue<string>());
        var totals = supplier ? Assert.Single(selected["currencyTotals"]!.AsArray())! : selected;
        Assert.Equal(600,totals[supplier ? "originalAmount" : "totalOriginal"]!.GetValue<decimal>());
        Assert.Equal(100,totals[supplier ? "paidAmount" : "totalPaid"]!.GetValue<decimal>());
        Assert.Equal(450,totals[supplier ? "outstandingAmount" : "totalOutstanding"]!.GetValue<decimal>());
        foreach (var suffix in new[] {$"&{partyKey}={Guid.NewGuid()}", $"&partySiteId={Guid.NewGuid()}"})
        {
            var scopedRoot = suffix.StartsWith($"&{partyKey}",StringComparison.Ordinal)
                ? $"/api/commerce/v1/{path}?dateRangeVersion=2&page=1&pageSize=20&{idKey}={target.PositionId}{suffix}"
                : $"{root}&page=1&{idKey}={target.PositionId}{suffix}";
            var absent = (await client.GetFromJsonAsync<JsonObject>(scopedRoot))!;
            Assert.Equal(0,absent["totalCount"]!.GetValue<int>());
        }
        using (var invalid = await client.GetAsync($"{root}&page=1&{idKey}={Guid.Empty}")) Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using (var invalidDate = await client.GetAsync($"{root}&page=1&to=9999-12-31")) Assert.Equal(HttpStatusCode.BadRequest,invalidDate.StatusCode);
        var referenceDate=DateOnly.Parse(full["referenceDate"]!.GetValue<string>(),System.Globalization.CultureInfo.InvariantCulture);
        await using (var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var zoneQuery=new SqlCommand("SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId",connection);
            zoneQuery.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);zoneQuery.Parameters.AddWithValue("@TenantId",fixture.TenantId);
            var zone=TimeZoneInfo.FindSystemTimeZoneById((string)(await zoneQuery.ExecuteScalarAsync())!);
            var beforeDay=BusinessCalendar.StartOfDay(referenceDate,zone).AddMinutes(-30);
            // Only the isolated fixture's read-projection timestamp changes; money and immutable sources are untouched.
            await using var stamp=new SqlCommand($"UPDATE dbo.{(supplier ? "Payables" : "Receivables")} SET CreatedAt=@At WHERE {(supplier ? "PayableId" : "ReceivableId")}=@Id AND BusinessId=@BusinessId",connection);
            stamp.Parameters.AddWithValue("@At",beforeDay);stamp.Parameters.AddWithValue("@Id",target.PositionId);stamp.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
            Assert.Equal(1,await stamp.ExecuteNonQueryAsync());
        }
        var fromDate=(await client.GetFromJsonAsync<JsonObject>($"{root}&page=1&{idKey}={target.PositionId}&from={referenceDate:yyyy-MM-dd}"))!;
        Assert.Equal(0,fromDate["totalCount"]!.GetValue<int>());
        var toDate=(await client.GetFromJsonAsync<JsonObject>($"{root}&page=1&{idKey}={target.PositionId}&to={referenceDate.AddDays(-1):yyyy-MM-dd}"))!;
        Assert.Equal(1,toDate["totalCount"]!.GetValue<int>());
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var samples = new List<double>();
        foreach (var page in new[] {1,2}) for (var sample=0;sample<20;sample++)
        {
            using var commands = new InventoryBalanceProcessingTests.CommandCounter(database);
            var timer = Stopwatch.StartNew();
            if (supplier)
            {
                var value = await scope.ServiceProvider.GetRequiredService<PayablesService>().ListAsync(
                    new(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{PayablesPermissionCodes.Read}),
                    new(page,20,null,target.CounterpartyRoleId,null,null,OutstandingOnly:true));
                Assert.Equal(41,value.TotalCount);Assert.Equal(20,value.Items.Count);
            }
            else
            {
                var value = await scope.ServiceProvider.GetRequiredService<ReceivablesService>().ListAsync(
                    new(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{ReceivablesPermissionCodes.Read}),
                    new(page,20,null,target.CounterpartyRoleId,null,null,OutstandingOnly:true));
                Assert.Equal(41,value.TotalCount);Assert.Equal(20,value.Items.Count);
            }
            samples.Add(timer.Elapsed.TotalMilliseconds);Assert.Equal(3,commands.Count);
        }
        var p95=samples.Order().ElementAt(37);
        var evidenceDirectory=Path.Combine(AppContext.BaseDirectory,"TestResults");Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory,$"administrative-invoice-selection-{supplier}.json"),
            System.Text.Json.JsonSerializer.Serialize(new {supplier,invoiceCount=41,pageSize=20,samples=samples.Count,sqlCommandsPerPage=3,coldMilliseconds,p95Milliseconds=p95}));
        Assert.True(p95<800,$"List p95 exceeded 800 ms: {p95:F1}");

        async Task<VoucherDraftView> Send(SaveVoucherDraftRequest request)
        {
            var url=$"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}";
            using var saved=await client.PutAsJsonAsync(url,request);Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
            var draft=(await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var sent=await client.PostAsJsonAsync(url+"/send",new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(sent.IsSuccessStatusCode,await sent.Content.ReadAsStringAsync());
            return (await sent.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }

    private async Task<Guid> CreateSelectionSupplierPartyAsync()
    {
        var party = await CreateCustomerPartyAsync(false);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Suppliers(SupplierId,TenantId,PartyId,Identification,Name,IsActive,CreatedAt)
              SELECT NEWID(),TenantId,PartyId,CONVERT(nvarchar(36),PartyId),DisplayName,1,SYSDATETIMEOFFSET()
              FROM dbo.Parties WHERE TenantId=@Tenant AND PartyId=@Party;
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
              SELECT TOP(1) NEWID(),@Party,N'PRINCIPAL',N'Principal',CountryId,AdministrativeDivisionId,CityId,AddressLine,1,1,@User,SYSDATETIMEOFFSET()
              FROM dbo.PartySites WHERE PartyId=@OriginalSupplier AND IsPrimary=1 AND IsActive=1 ORDER BY PartySiteId;
            """, connection);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Party", party);
        command.Parameters.AddWithValue("@User", fixture.UserId);
        command.Parameters.AddWithValue("@OriginalSupplier", fixture.SupplierPartyId);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
        return party;
    }
}
