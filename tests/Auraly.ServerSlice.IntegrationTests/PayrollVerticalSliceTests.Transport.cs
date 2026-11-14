using System.Diagnostics;
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
    [Theory]
    [InlineData(2,1)]
    [InlineData(100,2)]
    [InlineData(500,3)]
    [InlineData(501,4)]
    public async Task Calculation_batches_workers_and_respects_contract_transport_and_revision(int count,int month)
    {
        using var client=fixture.CreateAdminClient(PayrollPermissionCodes.Read,PayrollPermissionCodes.Configure);
        var options=(await client.GetFromJsonAsync<PayrollWorkspaceOptions>("/api/commerce/v1/payroll/options"))!;
        await EnsureCalculationConceptsAsync(client,options);
        Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(value=>value.Code==code).OptionId;
        var ruleId=Guid.NewGuid();
        var rules=await PutAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{ruleId}",new SavePayrollRuleSetRequest(
            ruleId,"CO",$"BATCH-{Guid.NewGuid():N}","Reglas del cálculo por lote",new(2026,1,1),null,"Prueba de cálculo por lote",
            Rules().Select(value=>new SavePayrollRuleParameterRequest(value.Key,value.Value,"Value",null)).ToArray(),null));
        await PostAsync<PayrollRuleSetView>(client,$"/api/commerce/v1/payroll/rule-sets/{ruleId}/approve",new {rowVersion=rules.RowVersion});
        var businessId=Guid.NewGuid();var runId=Guid.NewGuid();var start=new DateOnly(2026,month,1);var end=start.AddMonths(1).AddDays(-1);
        var people=Enumerable.Range(0,count).Select(index=>new {Id=Guid.NewGuid(),EmploymentId=Guid.NewGuid(),Apply=index%2==0}).ToArray();
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed=new SqlCommand("""
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                VALUES(@Business,@Tenant,N'Prueba cálculo por lote',N'',N'',N'',N'',N'');
                SELECT * INTO #Workers FROM OPENJSON(@People) WITH(Id uniqueidentifier,EmploymentId uniqueidentifier,Apply bit);
                INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,Identification,
                  NormalizedIdentification,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                SELECT worker.Id,@Tenant,N'NaturalPerson',country.CountryId,N'CC',CONVERT(nvarchar(36),worker.Id),CONVERT(nvarchar(36),worker.Id),
                  N'Trabajador de cálculo',N'Trabajador',N'Cálculo',N'Complete',1,@User,SYSUTCDATETIME()
                FROM #Workers worker CROSS JOIN dbo.Countries country WHERE country.Code=N'CO';
                INSERT dbo.Employees(EmployeeId,TenantId,PartyId,Name,IsActive,CreatedAt)
                SELECT NEWID(),@Tenant,Id,N'Trabajador de cálculo',1,SYSUTCDATETIME() FROM #Workers;
                INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,EmployeeId,ContractTypeOptionId,SalaryTypeOptionId,
                  PayFrequencyOptionId,RiskClassOptionId,WorkerTypeOptionId,PaymentMethodOptionId,ContractNumber,StartDate,MonthlySalary,
                  AppliesTransportAllowance,IsActive,CreatedBy,CreatedAt)
                SELECT worker.EmploymentId,@Tenant,worker.Id,@Business,employee.EmployeeId,@Contract,@Salary,@Frequency,@Risk,@Worker,@Cash,
                  CONVERT(nvarchar(36),worker.EmploymentId),'2026-01-01',1500000,worker.Apply,1,@User,SYSUTCDATETIME()
                FROM #Workers worker JOIN dbo.Employees employee ON employee.PartyId=worker.Id AND employee.TenantId=@Tenant;
                """,connection);
            seed.Parameters.AddWithValue("@People",JsonSerializer.Serialize(people));seed.Parameters.AddWithValue("@Business",businessId);
            seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@User",fixture.UserId);
            seed.Parameters.AddWithValue("@Contract",Option(PayrollCatalogCodes.ContractType,"Indefinite"));seed.Parameters.AddWithValue("@Salary",Option(PayrollCatalogCodes.SalaryType,"Ordinary"));
            seed.Parameters.AddWithValue("@Frequency",Option(PayrollCatalogCodes.PayFrequency,"Monthly"));seed.Parameters.AddWithValue("@Risk",Option(PayrollCatalogCodes.RiskClass,"I"));
            seed.Parameters.AddWithValue("@Worker",Option(PayrollCatalogCodes.WorkerType,"01"));seed.Parameters.AddWithValue("@Cash",Option(PayrollCatalogCodes.PaymentMethod,"Cash"));
            await seed.ExecuteNonQueryAsync();
        }
        using var scope=fixture.CreateScope();var store=scope.ServiceProvider.GetRequiredService<IPayrollStore>();
        var service=scope.ServiceProvider.GetRequiredService<PayrollService>();
        var user=new PayrollUserIdentity(fixture.UserId,fixture.TenantId,businessId,new HashSet<string>{PayrollPermissionCodes.Read,PayrollPermissionCodes.Calculate,PayrollPermissionCodes.Approve});
        await store.CreateRunAsync(user,new(runId,businessId,ruleId,Option(PayrollCatalogCodes.PayFrequency,"Monthly"),"Regular",null,start,end,end),CancellationToken.None);
        if(count==501)
        {
            await Assert.ThrowsAsync<PayrollValidationException>(()=>service.CalculateRunAsync(user,runId));
            Assert.Empty((await store.GetRunAsync(user,runId,CancellationToken.None)).Employees);
            await AssertRunSelectionAsync(service,store,user,runId,people[0].EmploymentId,people[1].EmploymentId);
            return;
        }
        var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var commandTimes=new List<double>();var commandStarts=new List<double>();var commandEnds=new List<double>();
        var elapsed=Stopwatch.StartNew();PayrollRunView result;
        using(var commands=new InventoryBalanceProcessingTests.CommandCounter(database,
            beforeExecute:_=>commandStarts.Add(elapsed.Elapsed.TotalMilliseconds),
            afterExecute:(_,time)=>{commandTimes.Add(time);commandEnds.Add(elapsed.Elapsed.TotalMilliseconds);} ))
        {
            result=await service.CalculateRunAsync(user,runId);
            Assert.Equal(10,commands.Count);
        }
        var calculationMs=elapsed.Elapsed.TotalMilliseconds;
        Assert.Equal(count,result.Employees.Count);Assert.All(result.Employees,worker=>Assert.Equal(30,worker.WorkedDays));
        var transport=people.ToDictionary(person=>person.Id,person=>person.Apply);
        foreach(var worker in result.Employees)
        {
            Assert.Equal(transport[worker.PartyId],worker.Lines.Any(line=>line.ConceptCode=="TRANSPORT"));
            Assert.Equal(60000m,worker.Lines.Single(line=>line.ConceptCode=="HEALTH_EMPLOYEE").Amount);
            Assert.Equal(60000m,worker.Lines.Single(line=>line.ConceptCode=="PENSION_EMPLOYEE").Amount);
        }
        if(count==2)
        {
            var inputs=await store.LoadCalculationDataAsync(user,runId,CancellationToken.None);
            var oldCalculations=inputs.Employees.Select(new PayrollCalculator().Calculate).ToArray();
            foreach(var worker in result.Employees)
            {
                var expected=oldCalculations.Single(value=>value.EmploymentId==worker.EmploymentId);
                foreach(var line in worker.Lines)
                {
                    var source=expected.Lines[line.LineNumber-1];
                    Assert.Equal(source.Quantity,line.Quantity);Assert.Equal(source.Rate,line.Rate);
                    Assert.Equal(source.BaseAmount,line.BaseAmount);Assert.Equal(source.Amount,line.Amount);
                    Assert.Equal(source.ConceptCode,line.ConceptCode);Assert.Equal(source.ConceptName,line.ConceptName);
                    Assert.Equal(source.DianConceptCode,line.DianConceptCode);
                }
            }
            var employment=Assert.Single((await store.PageEmploymentsAsync(user,1,1,null,null,people[0].EmploymentId,CancellationToken.None)).Items);
            Assert.True(employment.AppliesTransportAllowance);
            var update=new SavePayrollEmploymentRequest(employment.EmploymentId,employment.PartyId,businessId,employment.EmployeeId,
                employment.ContractTypeOptionId,employment.SalaryTypeOptionId,employment.PayFrequencyOptionId,employment.RiskClassOptionId,
                employment.WorkerTypeOptionId,employment.WorkerSubtypeOptionId,employment.PaymentMethodOptionId,employment.ContractNumber,
                employment.StartDate,employment.EndDate,employment.MonthlySalary,employment.IntegralSalaryPercentage,employment.BankAccountReference,
                employment.BankOptionId,employment.BankAccountTypeOptionId,employment.BankAccountNumber,true,employment.RowVersion,false);
            var saved=await store.SaveEmploymentAsync(user,update,CancellationToken.None);Assert.False(saved.AppliesTransportAllowance);
            var legacy=await store.SaveEmploymentAsync(user,update with {RowVersion=saved.RowVersion,AppliesTransportAllowance=null},CancellationToken.None);
            Assert.False(legacy.AppliesTransportAllowance);
            await Assert.ThrowsAsync<PayrollValidationException>(()=>store.SaveEmploymentAsync(user with {BusinessId=fixture.BusinessId},update with {BusinessId=fixture.BusinessId,RowVersion=legacy.RowVersion},CancellationToken.None));
            await Assert.ThrowsAsync<PayrollConflictException>(()=>store.SaveCalculationAsync(user,inputs,oldCalculations,CancellationToken.None));
            var afterRollback=await store.GetRunAsync(user,runId,CancellationToken.None);
            Assert.Equal(result.RowVersion,afterRollback.RowVersion);Assert.Equal(count,afterRollback.Employees.Count);
            await Assert.ThrowsAsync<PayrollConflictException>(()=>service.ApproveRunAsync(user,runId,"stale-transport",result.RowVersion));
            result=await service.CalculateRunAsync(user,runId);
            Assert.DoesNotContain(result.Employees.Single(worker=>worker.PartyId==people[0].Id).Lines,line=>line.ConceptCode=="TRANSPORT");
            await Assert.ThrowsAsync<PayrollNotFoundException>(()=>store.GetRunAsync(user with {BusinessId=fixture.BusinessId},runId,CancellationToken.None));
        }
        var directory=Path.Combine(AppContext.BaseDirectory,"TestResults");Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory,$"payroll-calculation-batch-{count}.json"),JsonSerializer.Serialize(new {count,calculationMs,commandTimes,commandStarts,commandEnds,commands=10}));
        Assert.True(calculationMs<(count==500?5000:2000),$"Calculation exceeded its budget: {calculationMs:F1} ms");
        if(count==500)await AssertWorkedDaysAsync(client,options,service,store,user,result,people[0].EmploymentId);
        if(count==2)await AssertContractDayBoundsAsync(service,store,user,result,people[1].EmploymentId,Option(PayrollCatalogCodes.PayFrequency,"Semimonthly"));
    }
}
