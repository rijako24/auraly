using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayrollVerticalSliceTests
{
    private async Task AssertRunSelectionAsync(PayrollService service, IPayrollStore store,
        PayrollUserIdentity user, Guid runId, Guid first, Guid second)
    {
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var samples = new List<double>();
        PayrollRunEmploymentPage? firstPage = null;
        var pageIds = new HashSet<Guid>();
        for (var index = 0; index < 20; index++)
        {
            var page = index % 2 + 1;
            var elapsed = Stopwatch.StartNew();
            PayrollRunEmploymentPage result;
            using (var commands = new InventoryBalanceProcessingTests.CommandCounter(database))
            {
                result = await service.PageRunEmploymentsAsync(user, runId, page, 50, null);
                Assert.Equal(1, commands.Count);
            }
            samples.Add(elapsed.Elapsed.TotalMilliseconds);
            Assert.Equal(501, result.TotalCount); Assert.Equal(501, result.EligibleCount);
            Assert.Equal(501, result.SelectedCount); Assert.Equal(50, result.Items.Count);
            Assert.All(result.Items, item => Assert.True(item.IsSelected));
            if (index == 0) { firstPage = result; pageIds.UnionWith(result.Items.Select(item => item.EmploymentId)); }
            if (index == 1) Assert.DoesNotContain(result.Items, item => pageIds.Contains(item.EmploymentId));
        }
        var p95 = samples.Order().ElementAt(18);
        Assert.True(p95 < 1000, $"Worker selection page p95 exceeded its budget: {p95:F1} ms");
        var version = firstPage!.RowVersion;
        var filtered = await service.PageRunEmploymentsAsync(user, runId, 1, 50, first.ToString());
        Assert.Equal(501, filtered.EligibleCount); Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(first, Assert.Single(filtered.Items).EmploymentId);
        var combined = await service.PageRunEmploymentsAsync(user, runId, 1, 50, "Trabajador", filter: new(first.ToString(), "contractNumber", "desc"));
        Assert.Equal(1, combined.TotalCount); Assert.Equal(first, Assert.Single(combined.Items).EmploymentId);
        double filteredP95;
        using(var configurationClient=fixture.CreateAdminClient(AccountingPermissionCodes.Configure))
        {
            var centerId=Guid.NewGuid();
            await PostAsync<AccountingCostCenterView>(configurationClient,"/api/commerce/v1/accounting/cost-centers",
                new CreateCostCenterRequest(centerId,user.TenantId,"SF"+centerId.ToString("N")[..18],"Centro de selección",null,false));
            await using(var seedConnection=new SqlConnection(fixture.ConnectionString))
            {
                await seedConnection.OpenAsync();await using var seed=new SqlCommand("""
                    UPDATE payroll.Employments SET CostCenterId=@Center,
                      WorkSite=CASE WHEN EmploymentId=@First THEN N'Zona_%[A]~ Norte' ELSE N'Zona otra Norte' END
                    WHERE TenantId=@Tenant AND BusinessId=@Business AND EmploymentId IN(@First,@Second);
                    """,seedConnection);
                seed.Parameters.AddWithValue("@Center",centerId);seed.Parameters.AddWithValue("@Tenant",user.TenantId);
                seed.Parameters.AddWithValue("@Business",user.BusinessId);seed.Parameters.AddWithValue("@First",first);seed.Parameters.AddWithValue("@Second",second);
                Assert.Equal(2,await seed.ExecuteNonQueryAsync());
            }
            using(var calculateClient=fixture.CreateAdminClient(PayrollPermissionCodes.Calculate))
            {
                var centers=await calculateClient.GetFromJsonAsync<AccountingCostCenterPage>(
                    $"/api/commerce/v1/accounting/cost-centers?costCenterId={centerId}&pageSize=1");
                Assert.Equal(centerId,Assert.Single(centers!.Items).CostCenterId);
                using var deniedMutation=await calculateClient.PostAsJsonAsync("/api/commerce/v1/accounting/cost-centers",
                    new CreateCostCenterRequest(Guid.NewGuid(),user.TenantId,"NO-CREATE","No permitido",null,false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,deniedMutation.StatusCode);
            }
            using(var deniedClient=fixture.CreateAdminClient(PayrollPermissionCodes.Read))
            {
                using var deniedRead=await deniedClient.GetAsync("/api/commerce/v1/accounting/cost-centers?pageSize=1");
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,deniedRead.StatusCode);
            }
            var centerFilter=new PayrollRunEmploymentFilter(CostCenterId:centerId);
            var centerPage=await service.PageRunEmploymentsAsync(user,runId,1,1,null,filter:centerFilter);
            var centerNext=await service.PageRunEmploymentsAsync(user,runId,2,1,null,filter:centerFilter);
            Assert.Equal(2,centerPage.TotalCount);Assert.Equal(2,centerNext.TotalCount);
            Assert.Equal(new[]{first,second}.Order(),centerPage.Items.Concat(centerNext.Items).Select(value=>value.EmploymentId).Order());
            var siteFilter=centerFilter with{WorkSite="_%[A]~"};
            var filterSamples=new List<double>();
            for(var sample=0;sample<20;sample++)
            {
                var watch=Stopwatch.StartNew();
                using var commands=new InventoryBalanceProcessingTests.CommandCounter(database);
                var site=await service.PageRunEmploymentsAsync(user,runId,1,1,"Trabajador",filter:siteFilter);
                watch.Stop();Assert.Equal(1,commands.Count);filterSamples.Add(watch.Elapsed.TotalMilliseconds);
                Assert.Equal(first,Assert.Single(site.Items).EmploymentId);Assert.Equal(1,site.TotalCount);
                Assert.Equal(501,site.EligibleCount);Assert.Equal(501,site.SelectedCount);
            }
            filteredP95=filterSamples.Order().ElementAt(18);
            Assert.True(filteredP95<1000,$"Filtered worker page exceeded one second: {filteredP95:F1} ms.");
            Assert.Empty((await service.PageRunEmploymentsAsync(user,runId,1,1,null,filter:siteFilter with{CostCenterId=Guid.NewGuid()})).Items);
            Assert.Empty((await service.PageRunEmploymentsAsync(user,runId,1,1,null,filter:siteFilter with{WorkSite="No existe"})).Items);
            await Assert.ThrowsAsync<PayrollValidationException>(()=>service.PageRunEmploymentsAsync(user,runId,1,1,null,filter:centerFilter with{CostCenterId=Guid.Empty}));
            await Assert.ThrowsAsync<PayrollValidationException>(()=>service.PageRunEmploymentsAsync(user,runId,1,1,null,filter:centerFilter with{WorkSite=new string('a',201)}));
        }
        var ordered = await service.PageRunEmploymentsAsync(user, runId, 1, 50, null, filter: new(null, "contractNumber", "desc"));
        var orderedNext = await service.PageRunEmploymentsAsync(user, runId, 2, 50, null, filter: new(null, "contractNumber", "desc"));
        var orderedContracts = ordered.Items.Concat(orderedNext.Items).Select(item => item.ContractNumber).ToArray();
        Assert.Equal(orderedContracts.OrderDescending(StringComparer.OrdinalIgnoreCase), orderedContracts);
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.PageRunEmploymentsAsync(user, runId, 1, 50, null, filter: new(null, "notAColumn")));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.PageRunEmploymentsAsync(user, runId, 1, 50, null, filter: new(null, "employeeName", "invalid")));
        Assert.Empty((await service.PageRunEmploymentsAsync(user, runId, 1, 50, "%")).Items);
        Assert.Empty((await service.PageRunEmploymentsAsync(user, runId, 50, 50, null)).Items);
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.PageRunEmploymentsAsync(user, runId, 1, 51, null));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.PageRunEmploymentsAsync(user, runId, int.MaxValue, 50, null));
        await Assert.ThrowsAsync<PayrollForbiddenException>(() => service.PageRunEmploymentsAsync(user with { Permissions = new HashSet<string>() }, runId, 1, 50, null));
        await Assert.ThrowsAsync<PayrollNotFoundException>(() => service.PageRunEmploymentsAsync(user with { BusinessId = fixture.BusinessId }, runId, 1, 50, null));
        await Assert.ThrowsAsync<PayrollNotFoundException>(() => service.PageRunEmploymentsAsync(user with { TenantId = Guid.NewGuid() }, runId, 1, 50, null));

        var calculated = await service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(version, false, [new(first, true), new(second, true)]));
        Assert.Equal(2, calculated.Employees.Count);
        var persisted = await service.PageRunEmploymentsAsync(user, runId, 1, 50, null);
        Assert.Equal(2, persisted.SelectedCount); Assert.Equal(calculated.RowVersion, persisted.RowVersion);
        Assert.True(Assert.Single((await service.PageRunEmploymentsAsync(user, runId, 1, 50, first.ToString())).Items).IsSelected);
        await Assert.ThrowsAsync<PayrollConflictException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(version, true)));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, true)));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, false)));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(null, false, [new(first, true)])));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, null, [null!])));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, null, [new(first, true), new(first, false)])));
        await Assert.ThrowsAsync<PayrollValidationException>(() => service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, null, [new(Guid.NewGuid(), false)])));
        Assert.Equal(calculated.RowVersion, (await store.GetRunAsync(user, runId, CancellationToken.None)).RowVersion);

        calculated = await service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, null, [new(first, false)]));
        Assert.Equal(second, Assert.Single(calculated.Employees).EmploymentId);
        calculated = await service.CalculateRunAsync(user, runId);
        Assert.Equal(second, Assert.Single(calculated.Employees).EmploymentId);
        var staleData = await store.LoadCalculationDataAsync(user, runId, CancellationToken.None);
        var oldCalculation = staleData.Employees.Select(new PayrollCalculator().Calculate).ToArray();
        calculated = await service.CalculateRunAsync(user, runId, new CalculatePayrollRunRequest(calculated.RowVersion, null, [new(first, true)]));
        Assert.Equal(2, calculated.Employees.Count);
        await Assert.ThrowsAsync<PayrollConflictException>(() => store.SaveCalculationAsync(user, staleData, oldCalculation, CancellationToken.None));
        var afterConflict = await store.GetRunAsync(user, runId, CancellationToken.None);
        Assert.Equal(calculated.RowVersion, afterConflict.RowVersion);
        Assert.Equal(calculated.Employees.Select(value => value.PayrollRunEmployeeId), afterConflict.Employees.Select(value => value.PayrollRunEmployeeId));
        Assert.Equal(2, (await service.PageRunEmploymentsAsync(user, runId, 1, 50, null)).SelectedCount);
        var path = Path.Combine(AppContext.BaseDirectory, "TestResults", "payroll-selection-page.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { workers = 501, pageSize = 50, commands = 1, p95, filteredP95, samples }));
    }
}
