using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;

namespace Auraly.Foundation.Tests;

public sealed class PayrollReportingServiceTests
{
    [Fact]
    public async Task Read_permission_is_required_for_definitions_and_execution()
    {
        var store = new RecordingStore();
        var service = new PayrollReportingService(store);
        var user = User(new HashSet<string>());

        await Assert.ThrowsAsync<PayrollForbiddenException>(
            () => service.ListDefinitionsAsync(user));
        await Assert.ThrowsAsync<PayrollForbiddenException>(
            () => service.RunAsync(user, "payroll-summary", new(2026, 1, 1), new(2026, 1, 31), null));
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Execution_normalizes_code_and_preserves_tenant_scope()
    {
        var store = new RecordingStore();
        var service = new PayrollReportingService(store);
        var user = User(new HashSet<string> { PayrollPermissionCodes.Read });

        await service.RunAsync(user, "  payroll-summary  ",
            new(2026, 1, 1), new(2026, 1, 31), null);

        Assert.Equal("payroll-summary", store.Code);
        Assert.Equal(user.TenantId, store.User?.TenantId);
        Assert.Equal(user.BusinessId, store.User?.BusinessId);
    }

    [Theory]
    [InlineData("", "2026-01-01", "2026-01-31")]
    [InlineData("payroll-summary", "2026-02-01", "2026-01-31")]
    [InlineData("payroll-summary", "2025-01-01", "2026-02-01")]
    public async Task Invalid_report_inputs_are_rejected_before_query(
        string code, string from, string to)
    {
        var store = new RecordingStore();
        var service = new PayrollReportingService(store);

        await Assert.ThrowsAsync<PayrollValidationException>(() => service.RunAsync(
            User(new HashSet<string> { PayrollPermissionCodes.Read }), code,
            DateOnly.Parse(from), DateOnly.Parse(to), null));
        Assert.Equal(0, store.CallCount);
    }

    private static PayrollUserIdentity User(IReadOnlySet<string> permissions) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), permissions);

    private sealed class RecordingStore : IPayrollReportingStore
    {
        public int CallCount { get; private set; }
        public string? Code { get; private set; }
        public PayrollUserIdentity? User { get; private set; }

        public Task<IReadOnlyList<PayrollReportDefinitionView>> ListDefinitionsAsync(
            PayrollUserIdentity user, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<PayrollReportDefinitionView>>([]);
        }

        public Task<PayrollReportResult> RunAsync(PayrollUserIdentity user, string code,
            DateOnly from, DateOnly to, Guid? partyId, CancellationToken ct)
        {
            CallCount++;
            Code = code;
            User = user;
            return Task.FromResult(new PayrollReportResult(
                new(code, code, string.Empty, PayrollReportDataset.PayrollSummary, [], 1),
                from, to, []));
        }
    }
}
