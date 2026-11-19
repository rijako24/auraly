using System.Net.Http.Json;
using Auraly.Application.Receivables;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Portfolio_report_preserves_available_advances_once_and_keeps_the_net_after_an_explicit_cross(bool withDebt)
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,ReceivablesPermissionCodes.Read,
            ReceivablesPermissionCodes.RegisterPayment);
        var party=await CreateCustomerPartyAsync(withDebt);
        var name="REPORT-"+Guid.NewGuid().ToString("N");
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using(var identify=new SqlCommand("UPDATE dbo.Parties SET DisplayName=@Name WHERE PartyId=@Party AND TenantId=@Tenant",connection))
        {
            identify.Parameters.AddWithValue("@Name",name);identify.Parameters.AddWithValue("@Party",party);
            identify.Parameters.AddWithValue("@Tenant",fixture.TenantId);Assert.Equal(1,await identify.ExecuteNonQueryAsync());
        }
        var cash=await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var advance=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"2398","Liability","CustomerAdvance","Credit");
        var receivable=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"1398","Asset","Receivable","Debit");
        var revenue=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"4198","Revenue","General","Credit");
        var at=new DateTimeOffset(2026,8,10,12,0,0,TimeSpan.Zero);
        var debtIds=new List<Guid>();
        if(withDebt)
            for(var i=0;i<2;i++)
            {
                var opened=await SendAsync("Accounting",[
                    new(receivable.AccountId,party,null,"Factura",500_000,0,$"REPORT-{i}",Guid.NewGuid(),DueDate:at.AddDays(30)),
                    new(revenue.AccountId,party,null,"Ingreso",0,500_000,LineId:Guid.NewGuid())]);
                debtIds.Add(Assert.Single(opened.FinancialEffects!.Positions).PositionId);
            }
        var receipt=await SendAsync("CashReceipt",[
            new(cash.AccountId,party,null,"Recaudo",2_000_000,0,LineId:Guid.NewGuid()),
            new(advance.AccountId,party,null,"Anticipo elegido",0,2_000_000,LineId:Guid.NewGuid())]);
        var creditId=Assert.Single(receipt.FinancialEffects!.Positions).PositionId;
        using var scope=fixture.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<ReceivablesService>();
        var actor=new ReceivablesUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{ReceivablesPermissionCodes.Read});
        var query=new ReceivablesReportQuery(1,1,false,new(2026,12,31),Search:name,TemplateVersion:4,DateRangeVersion:2);
        ReceivablesReportPage report;
        using(var reads=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            report=await service.ReportAsync(actor,query);
            Assert.Equal(2,reads.Count);
        }
        Assert.Equal(withDebt?3:1,report.TotalCount);
        Assert.Equal(withDebt?2:0,report.TotalInvoiceCount);
        Assert.Equal(2_000_000,report.TotalAvailableAdvances);
        Assert.Equal(0,report.TotalAvailableCredits);
        Assert.Equal(withDebt?-1_000_000:-2_000_000,report.TotalNetAmount);
        Assert.Equal(withDebt?1_000_000:0,report.TotalOverdue);
        Assert.Equal(2_000_000,Assert.Single(report.Items).GroupTotals!.AvailableAdvances);
        // Exercise the HTTP adapter used by the screen as well as the service.
        // Ignoring templateVersion here used to hide credits in the screen
        // while the PDF/export correctly included them.
        var reportPath=$"/api/commerce/v1/receivables/report?page=1&pageSize=1&consolidated=false&cutoff=2026-12-31&search={name}&dateRangeVersion=2";
        var screen=(await client.GetFromJsonAsync<ReceivablesReportPage>(reportPath+"&templateVersion=4"))!;
        Assert.Equal(report.TotalCount,screen.TotalCount);
        Assert.Equal(report.TotalAvailableAdvances,screen.TotalAvailableAdvances);
        Assert.Equal(report.TotalAvailableCredits,screen.TotalAvailableCredits);
        Assert.Equal(report.TotalNetAmount,screen.TotalNetAmount);
        var export=(await client.GetFromJsonAsync<ReceivablesReportPage>(reportPath.Replace("/report?","/report/export?",StringComparison.Ordinal)+"&templateVersion=4"))!;
        Assert.Equal(screen.TotalNetAmount,export.TotalNetAmount);
        Assert.Equal(screen.TotalAvailableAdvances,export.TotalAvailableAdvances);
        var legacy=(await client.GetFromJsonAsync<ReceivablesReportPage>(reportPath))!;
        Assert.Equal(withDebt?2:0,legacy.TotalCount);
        Assert.Equal(0,legacy.TotalAvailableAdvances);
        using(var invalid=await client.GetAsync(reportPath+"&templateVersion=999"))
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,invalid.StatusCode);
        var complete=await service.PrintReportAsync(actor,query);
        foreach(var invoice in complete.Items.Where(item=>item.ReceivableId.HasValue))
        {
            Assert.StartsWith("ASI",invoice.DocumentNumber);
            Assert.StartsWith("REPORT-",invoice.InternalDocumentNumber);
            var byReference=await service.ReportAsync(actor,query with {DocumentNumber=invoice.InternalDocumentNumber,PageSize=50});
            Assert.Equal(invoice.ReceivableId,Assert.Single(byReference.Items.Where(item=>item.ReceivableId.HasValue)).ReceivableId);
        }
        Assert.Equal(1,complete.Items.Count(item=>item.IsCreditBalance));
        Assert.Equal(2_000_000,complete.Items.Sum(item=>item.AvailableAdvances));
        Assert.All(complete.Items.Where(item=>item.ReceivableId.HasValue),item=>Assert.Equal(0,item.AvailableAdvances));
        Assert.Equal(report.TotalNetAmount,(await service.ReportAsync(actor,query with {Consolidated=true})).TotalNetAmount);
        var old=await service.ReportAsync(actor,query with {TemplateVersion=3,PageSize=50});
        Assert.Equal(withDebt?2:0,old.TotalCount);Assert.Equal(0,old.TotalAvailableAdvances);
        Assert.Equal(0,(await service.ReportAsync(actor,query with {Cutoff=new(2026,8,31)})).TotalCount);
        await Assert.ThrowsAsync<ReceivablesForbiddenException>(()=>service.ReportAsync(actor with {TenantId=Guid.NewGuid()},query));
        using(var printed=await client.GetAsync($"/api/commerce/v1/receivables/report/print?cutoff=2026-12-31&consolidated=false&templateVersion=4&search={name}"))
        {
            Assert.True(printed.IsSuccessStatusCode,await printed.Content.ReadAsStringAsync());
            var html=await printed.Content.ReadAsStringAsync();Assert.Contains("portfolio-v4",html);
            Assert.Contains("Anticipos disponibles",html);Assert.Contains("Neto comercial",html);
            Assert.Contains("2.000.000,00 COP",html);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html,"<h2>"));
            Assert.Contains("Edad de cartera",html);
            Assert.Contains("age-old",html);
            Assert.DoesNotContain("<strong>Anticipos y otros saldos a favor</strong>",html);
            if(!withDebt)Assert.Contains("Sin sede identificada",html);
        }
        if(!withDebt)
        {
            var customerId=Guid.NewGuid();
            await using(var role=new SqlCommand("""
                INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
                VALUES(@Customer,@Party,@Tenant,0,1,@User,SYSDATETIMEOFFSET());
                INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                SELECT TOP(1) NEWID(),@Party,N'PRINCIPAL',N'Principal',CountryId,AdministrativeDivisionId,CityId,AddressLine,1,1,@User,SYSDATETIMEOFFSET()
                FROM dbo.PartySites WHERE PartyId=@SupplierParty AND IsPrimary=1 AND IsActive=1 ORDER BY PartySiteId;
                """,connection))
            {
                role.Parameters.AddWithValue("@Customer",customerId);role.Parameters.AddWithValue("@Party",party);
                role.Parameters.AddWithValue("@Tenant",fixture.TenantId);role.Parameters.AddWithValue("@User",fixture.UserId);
                role.Parameters.AddWithValue("@SupplierParty",fixture.SupplierPartyId);Assert.Equal(2,await role.ExecuteNonQueryAsync());
            }
            await SendAsync("CashReceipt",[
                new(cash.AccountId,party,null,"Otro recaudo",100_000,0,LineId:Guid.NewGuid()),
                new(advance.AccountId,party,null,"Anticipo posterior al rol",0,100_000,LineId:Guid.NewGuid())]);
            using(var reads=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                commandFilter: command => command.CommandText.Contains("SELECT TimeZone FROM dbo.Businesses", StringComparison.Ordinal)
                    || command.CommandText.Contains("CREATE TABLE #Scoped(ReceivableId", StringComparison.Ordinal)))
            {
                var selected=await service.ReportAsync(actor,query with {CustomerId=customerId,PageSize=50});
                Assert.Equal(2,reads.Count);Assert.Equal(2,selected.TotalCount);
                Assert.Equal(2_100_000,selected.TotalAvailableAdvances);
                Assert.All(selected.Items,item=>Assert.Equal(customerId,item.CustomerId));
                Assert.Single(selected.Items,item=>item.PartySiteId is null);
            }
            var consolidated=await service.ReportAsync(actor,query with {CustomerId=customerId,Consolidated=true,PageSize=50});
            Assert.Equal(2,consolidated.Items.Count);Assert.Equal(-2_100_000,consolidated.TotalNetAmount);
            await using var unchanged=new SqlCommand("SELECT CustomerId,PartySiteId FROM dbo.CustomerCredits WHERE CustomerCreditId=@Id",connection);
            unchanged.Parameters.AddWithValue("@Id",creditId);
            await using var oldCredit=await unchanged.ExecuteReaderAsync();Assert.True(await oldCredit.ReadAsync());
            Assert.True(oldCredit.IsDBNull(0));Assert.True(oldCredit.IsDBNull(1));
            return;
        }
        var crossLines=debtIds.Select(id=>new VoucherDraftLine(receivable.AccountId,party,null,"Cruce",0,500_000,
            LineId:Guid.NewGuid(),ExistingPosition:new("Receivable",id))).ToList();
        crossLines.Add(new(advance.AccountId,party,null,"Aplicación del anticipo",1_000_000,0,
            LineId:Guid.NewGuid(),ExistingPosition:new("CustomerCredit",creditId)));
        var gate="credit-report-"+Guid.NewGuid().ToString("N");
        var debtRead=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using(var hold=new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Gate,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
            IF @Result<0 THROW 51500,'The test report gate was not acquired.',1;
            """,connection))
        {hold.Parameters.AddWithValue("@Gate",gate);await hold.ExecuteNonQueryAsync();}
        using var interleave=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter:command=>command.CommandText.Contains("CREATE TABLE #CreditBalances",StringComparison.Ordinal),
            beforeExecute:command=>
            {
                command.Connection!.InfoMessage+=(_,message)=>
                {if(message.Message.Contains("credit-report-debt-read",StringComparison.Ordinal))debtRead.TrySetResult(true);};
                command.CommandText=command.CommandText.Replace("CREATE TABLE #CreditBalances","""
                    RAISERROR(N'credit-report-debt-read',0,1) WITH NOWAIT;
                    DECLARE @TestGateResult int;
                    EXEC @TestGateResult=sys.sp_getapplock @Resource=@TestReportGate,@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=20000;
                    IF @TestGateResult<0 THROW 51500,'The test report gate expired.',1;
                    CREATE TABLE #CreditBalances
                    """,StringComparison.Ordinal);
                command.Parameters.AddWithValue("@TestReportGate",gate);
            });
        var concurrent=service.ReportAsync(actor,query with {PageSize=50});
        try
        {
            await debtRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await SendAsync("Accounting",crossLines);
        }
        finally
        {
            await using var release=new SqlCommand("EXEC sys.sp_releaseapplock @Resource=@Gate,@LockOwner=N'Session';",connection);
            release.Parameters.AddWithValue("@Gate",gate);await release.ExecuteNonQueryAsync();
        }
        var duringCross=await concurrent;
        Assert.Equal(1_000_000,duringCross.TotalOutstanding);
        Assert.Equal(2_000_000,duringCross.TotalAvailableAdvances);
        interleave.Dispose();
        var crossed=await service.ReportAsync(actor,query with {PageSize=50,OutstandingOnly=true});
        Assert.Equal(1,crossed.TotalCount);Assert.Equal(0,crossed.TotalOutstanding);
        Assert.Equal(1_000_000,crossed.TotalAvailableAdvances);Assert.Equal(report.TotalNetAmount,crossed.TotalNetAmount);
        Assert.Equal(0,crossed.TotalOverdue);

        // Customer credit eligibility is by party/currency, unlike supplier
        // credit. Keep the captured site in presentation without hiding a
        // compatible advance when searching an invoice from another site.
        var otherSite=Guid.NewGuid();
        await using(var site=new SqlCommand("""
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
            SELECT @Site,PartyId,N'REPORT-OTHER',N'Otra sede',CountryId,AdministrativeDivisionId,CityId,AddressLine,0,1,@User,SYSDATETIMEOFFSET()
            FROM dbo.PartySites WHERE PartyId=@Party AND IsPrimary=1 AND IsActive=1;
            """,connection))
        {
            site.Parameters.AddWithValue("@Site",otherSite);site.Parameters.AddWithValue("@Party",party);
            site.Parameters.AddWithValue("@User",fixture.UserId);Assert.Equal(1,await site.ExecuteNonQueryAsync());
        }
        await SendAsync("CashReceipt",[
            new(cash.AccountId,party,null,"Recaudo en otra sede del tercero",100_000,0,LineId:Guid.NewGuid(),PartySiteId:otherSite),
            new(advance.AccountId,party,null,"Anticipo en otra sede del tercero",0,100_000,LineId:Guid.NewGuid(),PartySiteId:otherSite)]);
        var invoiceNumber=complete.Items.Single(item=>item.ReceivableId==debtIds[0]).DocumentNumber;
        var invoiceReport=await service.ReportAsync(actor,query with {DocumentNumber=invoiceNumber,OutstandingOnly=false,PageSize=50});
        Assert.Equal(1,invoiceReport.TotalInvoiceCount);Assert.Equal(1_100_000,invoiceReport.TotalAvailableAdvances);
        Assert.Contains(invoiceReport.Items,item=>item.IsCreditBalance && item.PartySiteId==otherSite);

        async Task<VoucherDraftView> SendAsync(string kind,IReadOnlyList<VoucherDraftLine> lines)
        {
            var input=new SaveVoucherDraftRequest(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,at,
                "MANUAL_VOUCHER","Cartera y anticipos del informe",name,lines,null,null,
                MainPartyId:party,PayloadVersion:4,VoucherTypeCode:kind);
            var path=$"/api/commerce/v1/accounting/manual/drafts/{input.DocumentId}";
            using var saved=await client.PutAsJsonAsync(path,input);
            Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
            var draft=(await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var sent=await client.PostAsJsonAsync(path+"/send",new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(sent.IsSuccessStatusCode,await sent.Content.ReadAsStringAsync());
            return (await sent.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }
}
