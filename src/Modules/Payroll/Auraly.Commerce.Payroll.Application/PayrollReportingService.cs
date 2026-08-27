using Auraly.Commerce.Payroll.Contracts;

namespace Auraly.Commerce.Payroll.Application;

public interface IPayrollReportingStore
{
    Task<IReadOnlyList<PayrollReportDefinitionView>> ListDefinitionsAsync(
        PayrollUserIdentity user, CancellationToken ct);
    Task<PayrollReportResult> RunAsync(PayrollUserIdentity user, string code,
        DateOnly from, DateOnly to, Guid? partyId, CancellationToken ct);
}

public sealed class PayrollReportingService(IPayrollReportingStore store)
{
    public Task<IReadOnlyList<PayrollReportDefinitionView>> ListDefinitionsAsync(
        PayrollUserIdentity user, CancellationToken ct = default)
    {
        Demand(user);
        return store.ListDefinitionsAsync(user, ct);
    }

    public Task<PayrollReportResult> RunAsync(PayrollUserIdentity user, string code,
        DateOnly from, DateOnly to, Guid? partyId, CancellationToken ct = default)
    {
        Demand(user);
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 64 ||
            to < from || to.DayNumber - from.DayNumber > 370 || partyId == Guid.Empty)
            throw new PayrollValidationException(
                "El código, rango o trabajador del reporte no es válido.");
        return store.RunAsync(user, code.Trim(), from, to, partyId, ct);
    }

    private static void Demand(PayrollUserIdentity user)
    {
        if (!user.Permissions.Contains(PayrollPermissionCodes.Read))
            throw new PayrollForbiddenException(
                $"Se requiere el permiso '{PayrollPermissionCodes.Read}'.");
    }
}
