using System.Diagnostics;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayrollVerticalSliceTests
{
    private async Task AssertZeroNoveltiesConsumedAsync(PayrollRunView run)
    {
        var ids=run.Employees.SelectMany(employee=>employee.Lines).Where(line=>line.NoveltyId.HasValue && line.Amount==0)
            .Select(line=>new{NoveltyId=line.NoveltyId!.Value}).ToArray();
        Assert.Equal(2,ids.Length);
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var command=new SqlCommand("""
            SELECT COUNT(*) FROM OPENJSON(@Ids) WITH(NoveltyId uniqueidentifier) source
            JOIN payroll.Novelties n ON n.NoveltyId=source.NoveltyId
            WHERE n.TenantId=@TenantId AND n.BusinessId=@BusinessId AND n.Status=N'Consumed';
            """,connection);
        command.Parameters.AddWithValue("@Ids",JsonSerializer.Serialize(ids));
        command.Parameters.AddWithValue("@TenantId",fixture.TenantId);command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
        Assert.Equal(2,(int)(await command.ExecuteScalarAsync())!);
    }

    private async Task<PayrollRunView> AddZeroDayWorkerAsync(HttpClient client,PayrollWorkspaceOptions options,
        PayrollRunView run,SavePayrollEmploymentRequest template)
    {
        Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(value=>value.Code==code).OptionId;
        var partyId=await CreateEmployeePartyAsync("1032456798");var employmentId=Guid.NewGuid();var conceptId=Guid.NewGuid();
        await PutAsync<PayrollEmploymentView>(client,$"/api/commerce/v1/payroll/employments/{employmentId}",
            template with {EmploymentId=employmentId,PartyId=partyId,ContractNumber="ZERO-DAYS",RowVersion=null});
        await PutAsync<PayrollConceptView>(client,$"/api/commerce/v1/payroll/concepts/{conceptId}",new SavePayrollConceptRequest(
            conceptId,Guid.NewGuid().ToString("N"),"Ausencia sin importe",Option(PayrollCatalogCodes.ConceptNature,"Deduction"),
            Option(PayrollCatalogCodes.CalculationMethod,"FixedAmount"),Option(PayrollCatalogCodes.ConceptTreatment,"StatutoryDeduction"),null,
            Option(PayrollCatalogCodes.AccountingCategory,"EmployeeLoansReceivable"),null,false,false,false,false,false,
            run.PeriodStart,null,true,null));
        var absenceRequest=new SavePayrollNoveltyRequest(
            Guid.NewGuid(),employmentId,conceptId,Option(PayrollCatalogCodes.NoveltyType,"Days"),null,null,
            run.PeriodStart,run.PeriodStart.AddDays(1),2,null,0,"Ausencia conserva su origen incluso sin importe",null);
        var absence=await PostAsync<PayrollNoveltyView>(client,"/api/commerce/v1/payroll/novelties",absenceRequest);
        await PostAsync<SavePayrollNoveltyRequest>(client,"/api/commerce/v1/payroll/novelties",new SavePayrollNoveltyRequest(
            Guid.NewGuid(),template.EmploymentId,conceptId,Option(PayrollCatalogCodes.NoveltyType,"Amount"),null,null,
            run.PeriodStart,run.PeriodEnd,1,null,0,"Entrada sin importe conserva su origen operativo",null));
        run=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{run.PayrollRunId}/calculate",
            new CalculatePayrollRunRequest(run.RowVersion,SelectionChanges:[new(employmentId,true)]));
        Assert.Equal(28,Assert.Single(run.Employees,employee=>employee.EmploymentId==employmentId).WorkedDays);
        await PutAsync<PayrollNoveltyView>(client,$"/api/commerce/v1/payroll/novelties/{absence.NoveltyId}",
            absenceRequest with {Quantity=3,EndDate=absenceRequest.StartDate.AddDays(2),RowVersion=absence.RowVersion});
        run=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{run.PayrollRunId}/calculate",
            new CalculatePayrollRunRequest(run.RowVersion,RecalculateEmploymentIds:[employmentId]));
        Assert.True(run.IsPartialCalculation);Assert.Equal(2,run.EmployeeCount);
        Assert.Equal(30,Assert.Single(run.Employees).BaseWorkedDays);Assert.Equal(27,Assert.Single(run.Employees).WorkedDays);
        var edited=await PostAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{run.PayrollRunId}/calculate",
            new CalculatePayrollRunRequest(run.RowVersion,WorkedDaysChanges:[new(employmentId,0)]));
        var zero=Assert.Single(edited.Employees);Assert.Equal(0,zero.WorkedDays);Assert.Equal(0,zero.Earnings);
        Assert.Equal(3,zero.BaseWorkedDays);Assert.Equal(0,Assert.Single(zero.Lines).Amount);
        Assert.Equal(absence.NoveltyId,Assert.Single(zero.Novelties).NoveltyId);
        var future=await PostAsync<PayrollNoveltyView>(client,"/api/commerce/v1/payroll/novelties",new SavePayrollNoveltyRequest(
            Guid.NewGuid(),employmentId,conceptId,Option(PayrollCatalogCodes.NoveltyType,"Amount"),null,null,
            run.PeriodStart.AddMonths(1),run.PeriodEnd.AddMonths(1),0,null,0,"Otro período no pertenece al detalle",null));
        var detail=await GetAsync<PayrollRunView>(client,$"/api/commerce/v1/payroll/runs/{run.PayrollRunId}");
        Assert.All(detail.Employees,employee=>Assert.All(employee.Novelties,novelty=>Assert.Equal(employee.EmploymentId,novelty.EmploymentId)));
        Assert.DoesNotContain(detail.Employees.SelectMany(employee=>employee.Novelties),novelty=>novelty.NoveltyId==future.NoveltyId);
        Assert.Empty((await GetAsync<PayrollWorkspaceOptions>(client,"/api/commerce/v1/payroll/options")).Novelties);
        return detail;
    }

    private async Task AssertWorkedDaysAsync(HttpClient configurationClient,PayrollWorkspaceOptions options,PayrollService service,
        IPayrollStore store,PayrollUserIdentity user,PayrollRunView initial,Guid employmentId)
    {
        Guid Option(string catalog,string code)=>options.Catalogs[catalog].Single(value=>value.Code==code).OptionId;
        var conceptId=Guid.NewGuid();
        await PutAsync<PayrollConceptView>(configurationClient,$"/api/commerce/v1/payroll/concepts/{conceptId}",new SavePayrollConceptRequest(
            conceptId,Guid.NewGuid().ToString("N"),"Ausencia sin doble descuento",Option(PayrollCatalogCodes.ConceptNature,"Deduction"),
            Option(PayrollCatalogCodes.CalculationMethod,"FixedAmount"),Option(PayrollCatalogCodes.ConceptTreatment,"StatutoryDeduction"),null,
            Option(PayrollCatalogCodes.AccountingCategory,"EmployeeLoansReceivable"),null,false,false,false,false,false,
            new(2026,1,1),null,true,null));
        var noveltyId=Guid.NewGuid();
        await store.SaveNoveltyAsync(user,new(noveltyId,employmentId,conceptId,Option(PayrollCatalogCodes.NoveltyType,"Days"),null,null,
            initial.PeriodStart,initial.PeriodStart.AddDays(1),2,null,0,"Dos días ya descuentan el salario",null),CancellationToken.None);
        var stale=await Assert.ThrowsAsync<PayrollConflictException>(()=>service.ApproveRunAsync(user,initial.PayrollRunId,"new-absence-after-calculation",initial.RowVersion));
        Assert.Contains("novedades",stale.Message);
        Assert.Equal(initial.RowVersion,(await store.GetRunAsync(user,initial.PayrollRunId,CancellationToken.None)).RowVersion);
        var full=await service.CalculateRunAsync(user,initial.PayrollRunId);
        var worker=full.Employees.Single(value=>value.EmploymentId==employmentId);
        Assert.Equal(28,worker.WorkedDays);Assert.Equal(30,worker.BaseWorkedDays);
        Assert.Equal(noveltyId,Assert.Single(worker.Lines,line=>line.NoveltyId==noveltyId).NoveltyId);
        Assert.Equal(0,worker.Lines.Single(line=>line.NoveltyId==noveltyId).Amount);
        var unaffected=full.Employees.Where(value=>value.EmploymentId!=employmentId).ToDictionary(value=>value.EmploymentId);
        var current=full;var samples=new List<double>();var sqlSamples=new List<double[]>();
        var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        for(var index=0;index<20;index++)
        {
            var days=index%2==0?25:28;
            var elapsed=Stopwatch.StartNew();
            var commandTimes=new List<double>();
            using(var commands=new InventoryBalanceProcessingTests.CommandCounter(database,afterExecute:(_,time)=>commandTimes.Add(time)))
            {
                current=await service.CalculateRunAsync(user,initial.PayrollRunId,new CalculatePayrollRunRequest(current.RowVersion,
                    WorkedDaysChanges:[new(employmentId,days)]));
                Assert.Equal(10,commands.Count);
            }
            samples.Add(elapsed.Elapsed.TotalMilliseconds);
            sqlSamples.Add(commandTimes.ToArray());
            Assert.True(current.IsPartialCalculation);Assert.Equal(500,current.EmployeeCount);
            worker=Assert.Single(current.Employees);Assert.Equal(employmentId,worker.EmploymentId);
            Assert.Equal(days,worker.WorkedDays);Assert.Equal(days+2,worker.BaseWorkedDays);
            Assert.Equal(days*2000m,worker.Lines.Single(line=>line.ConceptCode=="HEALTH_EMPLOYEE").Amount);
        }
        var p95=samples.Order().ElementAt(18);
        var file=Path.Combine(AppContext.BaseDirectory,"TestResults","payroll-worked-days-500.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{workers=500,affected=1,commands=10,p95,samples,sqlSamples}));
        Assert.True(p95<1000,$"Individual recalculation exceeded its budget: {p95:F1} ms");
        PayrollRunView reread;
        using(var reads=new InventoryBalanceProcessingTests.CommandCounter(database))
        {
            reread=await store.GetRunAsync(user,initial.PayrollRunId,CancellationToken.None);
            Assert.Equal(1,reads.Count);
        }
        Assert.Equal(noveltyId,Assert.Single(reread.Employees.Single(employee=>employee.EmploymentId==employmentId).Novelties).NoveltyId);
        Assert.All(reread.Employees.Where(employee=>employee.EmploymentId!=employmentId),employee=>Assert.Empty(employee.Novelties));
        Assert.False(reread.IsPartialCalculation);Assert.Equal(500,reread.Employees.Count);
        Assert.Equal(reread.Employees.Sum(value=>value.Earnings),reread.TotalEarnings);
        Assert.Equal(reread.Employees.Sum(value=>value.NetPayable),reread.NetPayable);
        Assert.Equal(current.RowVersion,reread.RowVersion);
        foreach(var item in reread.Employees.Where(value=>value.EmploymentId!=employmentId))
        {
            Assert.Equal(unaffected[item.EmploymentId].PayrollRunEmployeeId,item.PayrollRunEmployeeId);
            Assert.Equal(unaffected[item.EmploymentId].NetPayable,item.NetPayable);
        }
        current=await service.CalculateRunAsync(user,initial.PayrollRunId,new CalculatePayrollRunRequest(current.RowVersion,WorkedDaysChanges:[new(employmentId,25)]));
        var savedVersion=current.RowVersion;
        await Assert.ThrowsAsync<PayrollValidationException>(()=>service.CalculateRunAsync(user,initial.PayrollRunId,
            new CalculatePayrollRunRequest(savedVersion,WorkedDaysChanges:[new(employmentId,29)])));
        await Assert.ThrowsAsync<PayrollValidationException>(()=>service.CalculateRunAsync(user,initial.PayrollRunId,
            new CalculatePayrollRunRequest(savedVersion,WorkedDaysChanges:[new(Guid.NewGuid(),20)])));
        Assert.Equal(savedVersion,(await store.GetRunAsync(user,initial.PayrollRunId,CancellationToken.None)).RowVersion);
        current=await service.CalculateRunAsync(user,initial.PayrollRunId);
        Assert.Equal(25,current.Employees.Single(value=>value.EmploymentId==employmentId).WorkedDays);
        current=await service.CalculateRunAsync(user,initial.PayrollRunId,new CalculatePayrollRunRequest(current.RowVersion,SelectionChanges:[new(employmentId,false)]));
        Assert.Equal(499,current.EmployeeCount);
        current=await service.CalculateRunAsync(user,initial.PayrollRunId,new CalculatePayrollRunRequest(current.RowVersion,SelectionChanges:[new(employmentId,true)]));
        Assert.Equal(25,current.Employees.Single(value=>value.EmploymentId==employmentId).WorkedDays);
    }

    private async Task AssertContractDayBoundsAsync(PayrollService service,IPayrollStore store,PayrollUserIdentity user,
        PayrollRunView initial,Guid employmentId,Guid halfMonthFrequency)
    {
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var update=new SqlCommand("UPDATE payroll.Employments SET StartDate=@Start WHERE EmploymentId=@Id AND TenantId=@Tenant;",connection);
            update.Parameters.AddWithValue("@Start",initial.PeriodStart.AddDays(18).ToDateTime(TimeOnly.MinValue));
            update.Parameters.AddWithValue("@Id",employmentId);update.Parameters.AddWithValue("@Tenant",user.TenantId);
            await update.ExecuteNonQueryAsync();
        }
        var current=await service.CalculateRunAsync(user,initial.PayrollRunId);
        var worker=current.Employees.Single(value=>value.EmploymentId==employmentId);
        Assert.Equal(30,worker.WorkedDays);Assert.Equal(12,worker.MaximumContractDays);
        var bounds=await Assert.ThrowsAsync<PayrollConflictException>(()=>service.ApproveRunAsync(user,current.PayrollRunId,"contract-days-outside-employment",current.RowVersion));
        Assert.Contains("vigencia contractual",bounds.Message);
        current=await service.CalculateRunAsync(user,current.PayrollRunId,new CalculatePayrollRunRequest(current.RowVersion,WorkedDaysChanges:[new(employmentId,0)]));
        worker=Assert.Single(current.Employees);Assert.Equal(0,worker.NetPayable);Assert.Empty(worker.Lines);
        current=await service.CalculateRunAsync(user,current.PayrollRunId,new CalculatePayrollRunRequest(current.RowVersion,WorkedDaysChanges:[new(employmentId,12)]));
        Assert.Equal(12,Assert.Single(current.Employees).WorkedDays);
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var update=new SqlCommand("UPDATE payroll.Employments SET PayFrequencyOptionId=@Frequency WHERE EmploymentId=@Id AND TenantId=@Tenant;",connection);
            update.Parameters.AddWithValue("@Frequency",halfMonthFrequency);update.Parameters.AddWithValue("@Id",employmentId);update.Parameters.AddWithValue("@Tenant",user.TenantId);
            await update.ExecuteNonQueryAsync();
        }
        var half=await store.CreateRunAsync(user,new(Guid.NewGuid(),user.BusinessId,initial.RuleSetId,halfMonthFrequency,"Regular",null,
            initial.PeriodStart.AddDays(15),initial.PeriodEnd,initial.PaymentDate),CancellationToken.None);
        half=await service.CalculateRunAsync(user,half.PayrollRunId);
        worker=Assert.Single(half.Employees);Assert.Equal(15,worker.WorkedDays);Assert.Equal(12,worker.MaximumContractDays);Assert.Equal(15,worker.PeriodDays);
        half=await service.CalculateRunAsync(user,half.PayrollRunId,new CalculatePayrollRunRequest(half.RowVersion,WorkedDaysChanges:[new(employmentId,12)]));
        Assert.Equal(24_000m,Assert.Single(half.Employees).Lines.Single(line=>line.ConceptCode=="HEALTH_EMPLOYEE").Amount);
    }
}
