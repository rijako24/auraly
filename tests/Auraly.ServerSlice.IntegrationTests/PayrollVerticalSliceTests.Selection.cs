using System.Diagnostics;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
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
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { workers = 501, pageSize = 50, commands = 1, p95, samples }));
    }
}
