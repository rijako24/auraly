using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayrollVerticalSliceTests
{
    [Fact]
    public async Task Payroll_histories_page_before_enrichment_and_preserve_frozen_electronic_identity()
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        var options=await GetAsync<PayrollWorkspaceOptions>(client,"/api/commerce/v1/payroll/options");
        Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(row=>row.Code==code).OptionId;
        var concepts=await EnsureCalculationConceptsAsync(client,options);
        var employment=new{PartyId=await CreateEmployeePartyAsync("HISTORY-"+Guid.NewGuid().ToString("N")),EmploymentId=Guid.NewGuid()};
        var business=Guid.NewGuid();var period=Guid.NewGuid();
        // Synthetic read-model volume in an isolated site. No posting, money,
        // fiscal submission or production writes are invoked by this fixture.
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed=new SqlCommand("""
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                  VALUES(@Business,@Tenant,N'Prueba de históricos paginados',N'',N'',N'',N'',N'');
                INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,ContractTypeOptionId,SalaryTypeOptionId,
                  PayFrequencyOptionId,RiskClassOptionId,WorkerTypeOptionId,WorkerSubtypeOptionId,PaymentMethodOptionId,ContractNumber,
                  StartDate,MonthlySalary,IsActive,CreatedBy,CreatedAt)
                  VALUES(@Employment,@Tenant,@Party,@Business,@Contract,@Salary,@Frequency,@Risk,@Worker,@Subtype,@Cash,N'Histórico sintético','20260101',1000000,1,@User,SYSUTCDATETIME());
                SELECT TOP(500) ROW_NUMBER() OVER(ORDER BY a.n,b.n,c.n) n,NEWID() RunId,NEWID() BatchId,NEWID() WorkerId,NEWID() PeriodId,NEWID() PartyId
                  INTO #Rows FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) a(n)
                  CROSS JOIN(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) b(n)
                  CROSS JOIN(VALUES(0),(1),(2),(3),(4)) c(n);
                UPDATE #Rows SET PeriodId=@Period WHERE n=500;
                INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,PayFrequencyOptionId,RunKind,PeriodStart,PeriodEnd,PaymentDate,
                  Status,CalculationVersion,TotalEarnings,TotalDeductions,TotalEmployerContributions,TotalProvisions,NetPayable,CreatedBy,CreatedAt)
                  SELECT RunId,@Tenant,@Business,@Frequency,N'Regular',DATEADD(month,n,'20800101'),EOMONTH(DATEADD(month,n,'20800101')),
                    EOMONTH(DATEADD(month,n,'20800101')),N'Approved',1,1,0,0,0,1,@User,SYSUTCDATETIME() FROM #Rows;
                INSERT payroll.RunEmployees(PayrollRunEmployeeId,TenantId,PayrollRunId,EmploymentId,PartyId,EmployeeSnapshotJson,RuleSnapshotJson,
                  WorkedDays,Earnings,Deductions,EmployerContributions,Provisions,NetPayable,CalculationHash)
                  SELECT WorkerId,@Tenant,RunId,@Employment,@Party,N'{}',N'{}',30,1,0,0,0,1,HASHBYTES('SHA2_256',N'fixture') FROM #Rows;
                INSERT payroll.PaymentBatches(PaymentBatchId,TenantId,BusinessId,PaymentDate,PaymentMethodOptionId,ReferenceNumber,Status,TotalAmount,CreatedBy,CreatedAt)
                  SELECT BatchId,@Tenant,@Business,EOMONTH(DATEADD(month,n,'20800101')),@Cash,CONCAT(N'QA-',n),N'Confirmed',1,@User,SYSUTCDATETIME() FROM #Rows;
                INSERT payroll.PaymentLines(PaymentLineId,PaymentBatchId,PayrollRunEmployeeId,Amount)
                  SELECT NEWID(),BatchId,WorkerId,1 FROM #Rows;
                INSERT payroll.DeductionAgreements(DeductionAgreementId,TenantId,EmploymentId,ConceptId,AuthorityOptionId,ReferenceNumber,EvidenceUrl,
                  EffectiveFrom,AuthorizedTotal,InstallmentAmount,DeductedToDate,Priority,MustProtectMinimumNetPay,IsActive,CreatedBy,CreatedAt)
                  SELECT NEWID(),@Tenant,@Employment,@Concept,@Authority,CONCAT(N'QA-',n),N'https://qa.invalid/soporte','20260101',1000,1,0,n,1,1,@User,SYSUTCDATETIME() FROM #Rows;
                INSERT payroll.ElectronicPeriods(ElectronicPeriodId,TenantId,BusinessId,Year,Month,Status,CreatedBy,CreatedAt)
                  SELECT PeriodId,@Tenant,@Business,YEAR(DATEADD(month,n,'20800101')),MONTH(DATEADD(month,n,'20800101')),N'Generated',@User,SYSUTCDATETIME() FROM #Rows;
                INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,Identification,NormalizedIdentification,
                  DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                  SELECT PartyId,@Tenant,N'NaturalPerson',country.CountryId,N'CC',CONVERT(nvarchar(36),PartyId),CONVERT(nvarchar(36),PartyId),
                    N'Nombre vigente posterior',N'Nombre',N'Posterior',N'Complete',1,@User,SYSUTCDATETIME() FROM #Rows CROSS JOIN dbo.Countries country WHERE country.Code=N'CO';
                INSERT payroll.ElectronicDocuments(ElectronicPayrollDocumentId,ElectronicPeriodId,TenantId,BusinessId,PartyId,DocumentKind,
                  SourceSnapshotJson,SourceHash,Status,CreatedAt)
                  SELECT NEWID(),@Period,@Tenant,@Business,PartyId,N'Individual',N'{"employeeName":"Nombre Congelado","firstName":"Otro nombre"}',
                    HASHBYTES('SHA2_256',CONVERT(nvarchar(36),PartyId)),N'Queued',SYSUTCDATETIME() FROM #Rows;
                """,connection);
            seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Business",business);
            seed.Parameters.AddWithValue("@Employment",employment.EmploymentId);seed.Parameters.AddWithValue("@Party",employment.PartyId);
            seed.Parameters.AddWithValue("@User",fixture.UserId);seed.Parameters.AddWithValue("@Period",period);
            seed.Parameters.AddWithValue("@Frequency",Option(PayrollCatalogCodes.PayFrequency,"Monthly"));
            seed.Parameters.AddWithValue("@Cash",Option(PayrollCatalogCodes.PaymentMethod,"Cash"));
            seed.Parameters.AddWithValue("@Contract",Option(PayrollCatalogCodes.ContractType,"Indefinite"));
            seed.Parameters.AddWithValue("@Salary",Option(PayrollCatalogCodes.SalaryType,"Ordinary"));
            seed.Parameters.AddWithValue("@Risk",Option(PayrollCatalogCodes.RiskClass,"I"));
            seed.Parameters.AddWithValue("@Worker",Option(PayrollCatalogCodes.WorkerType,"01"));
            seed.Parameters.AddWithValue("@Subtype",Option(PayrollCatalogCodes.WorkerSubtype,"00"));
            seed.Parameters.AddWithValue("@Authority",Option(PayrollCatalogCodes.DeductionAuthority,"WrittenAuthorization"));
            seed.Parameters.AddWithValue("@Concept",concepts[PayrollSystemConceptRoles.BasicSalary]);
            await seed.ExecuteNonQueryAsync();
        }
        using var scope=fixture.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<PayrollService>();
        var user=new PayrollUserIdentity(fixture.UserId,fixture.TenantId,business,new HashSet<string>{PayrollPermissionCodes.Read});
        var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var samples=new Dictionary<string,List<double>>();
        async Task Measure<T>(string name,Func<int,Task<(IReadOnlyList<T> Rows,int Count)>> read,Func<T,Guid> id)
        {
            var timings=new List<double>();Guid[]? first=null;
            for(var sample=0;sample<20;sample++)
            {
                using var counter=new InventoryBalanceProcessingTests.CommandCounter(database);
                var clock=Stopwatch.StartNew();var result=await read(sample%2+1);timings.Add(clock.Elapsed.TotalMilliseconds);
                Assert.Equal(1,counter.Count);Assert.Equal(500,result.Count);Assert.Equal(20,result.Rows.Count);
                var ids=result.Rows.Select(id).ToArray();
                if(sample==0)first=ids;else if(sample==1)Assert.Empty(first!.Intersect(ids));
            }
            Assert.True(timings.Order().ElementAt(18)<1000,$"La página de {name} excedió 1 s.");samples[name]=timings;
        }
        await Measure("liquidaciones",async page=>{var value=await service.PageRunsAsync(user,page,20,"Approved","Regular",null);return(value.Items,value.TotalCount);},row=>row.PayrollRunId);
        await Measure("autorizaciones",async page=>{var value=await service.PageAgreementsAsync(user,page,20,employment.EmploymentId,null,true,null);return(value.Items,value.TotalCount);},row=>row.DeductionAgreementId);
        await Measure("pagos",async page=>{var value=await service.PagePaymentsAsync(user,page,20);return(value.Items,value.TotalCount);},row=>row.PaymentBatchId);
        await Measure("períodos",async page=>{var value=await service.PageElectronicPeriodsAsync(user,page,20);Assert.Empty(value.Items[0].Documents);return(value.Items,value.TotalCount);},row=>row.ElectronicPeriodId);
        await Measure("documentos",async page=>{var value=await service.PageElectronicDocumentsAsync(user,period,page,20);Assert.All(value.Items,row=>Assert.Equal("Nombre Congelado",row.EmployeeName));return(value.Items,value.TotalCount);},row=>row.ElectronicPayrollDocumentId);
        Assert.Equal(500,(await service.PageElectronicPeriodsAsync(user,1,20)).Items[0].DocumentCount);
        Assert.Equal(0,(await service.PageRunsAsync(user with{TenantId=Guid.NewGuid()},1,20,null,null,null)).TotalCount);
        Assert.Equal(0,(await service.PagePaymentsAsync(user with{BusinessId=Guid.NewGuid()},1,20)).TotalCount);
        Assert.Equal(0,(await service.PageAgreementsAsync(user,1,20,Guid.NewGuid(),null,null,null)).TotalCount);
        Assert.Equal(0,(await service.PageElectronicDocumentsAsync(user with{BusinessId=Guid.NewGuid()},period,1,20)).TotalCount);
        foreach(var route in new[]{"runs","deduction-agreements","payments","electronic-periods",$"electronic-periods/{period}/documents"})
        {
            using var invalid=await client.GetAsync($"/api/commerce/v1/payroll/{route}?page=1&pageSize=51");Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
            using var forbidden=fixture.CreateAdminClient();using var denied=await forbidden.GetAsync($"/api/commerce/v1/payroll/{route}?page=1&pageSize=20");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"payroll-history-pages-performance.json"),JsonSerializer.Serialize(new{rows=500,pageSize=20,commandsPerPage=1,samples}));
    }
}
