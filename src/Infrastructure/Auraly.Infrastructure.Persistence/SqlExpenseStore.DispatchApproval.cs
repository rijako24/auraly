using Auraly.Application.Expenses;
using Auraly.Contracts.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    // Approval is part of the existing expense acceptance transaction. A report
    // cannot become Approved unless its real Expense and accounting source exist.
    private static async Task LockDispatchApprovalAsync(SqlConnection connection, SqlTransaction tx,
        ExpenseUserIdentity user, ConfirmExpenseRequest request, decimal cashSpent, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT report.Amount,(SELECT GrossCash FROM dispatch.PaymentTotals(@Dispatch))-
              (SELECT COALESCE(SUM(ApprovedAmount),0) FROM dbo.DispatchExpenses WHERE DispatchId=@Dispatch AND ApprovalStatus=N'Approved')
            FROM dbo.Dispatches dispatch WITH(UPDLOCK,HOLDLOCK)
            JOIN dispatch.Closures settlement WITH(UPDLOCK,HOLDLOCK) ON settlement.DispatchId=dispatch.DispatchId
            JOIN dbo.DispatchExpenses report WITH(UPDLOCK,HOLDLOCK) ON report.DispatchId=dispatch.DispatchId
            WHERE dispatch.DispatchId=@Dispatch AND dispatch.TenantId=@Tenant AND dispatch.BusinessId=@Business
              AND dispatch.Status=N'PendingSettlement' AND settlement.Status=N'PendingReview'
              AND report.DispatchExpenseId=@Report AND report.BusinessId=@Business AND report.ApprovalStatus=N'Pending'
              AND report.AcceptedExpenseId IS NULL OPTION(FORCE ORDER);
            SELECT 1 FROM reference.PaymentMethodDetails form WITH(HOLDLOCK) WHERE form.PaymentMethodId=@Form
              AND form.TenantId=@Tenant AND form.Version=@Version AND form.MethodCode=N'Cash'
              AND form.IsActive=1 AND form.IsReady=1 AND form.UseForSupplierPayments=1;
            """, connection, tx);
        ApprovalParameters(command, user, request);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ExpenseConflictException("El gasto no está pendiente de aprobación en este cierre de despacho.");
        if (cashSpent > reader.GetDecimal(0)) throw new ExpenseValidationException("El efectivo pagado del gasto no puede superar el valor reportado por el transportador.");
        if (cashSpent > reader.GetDecimal(1)) throw new ExpenseValidationException("Los gastos aprobados no pueden superar el efectivo recaudado del despacho.");
        await reader.NextResultAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ExpenseConflictException("La forma de efectivo cambió o no está habilitada para pagar gastos.");
    }

    private static async Task<DispatchExpenseApproval> ApplyDispatchApprovalAsync(SqlConnection connection, SqlTransaction tx,
        ExpenseUserIdentity user, ConfirmExpenseRequest request, decimal cashSpent, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.DispatchExpenses SET ApprovalStatus=N'Approved',ApprovedAmount=@Cash,
              AcceptedExpenseId=@Expense,CashPaymentMethodId=@Form,CashPaymentMethodVersion=@Version,
              ReviewedBy=@User,ReviewedAt=@Now,ReviewIdempotencyKey=CONCAT(N'expense:',CONVERT(nvarchar(36),@Expense))
            WHERE DispatchExpenseId=@Report AND DispatchId=@Dispatch AND BusinessId=@Business AND ApprovalStatus=N'Pending';
            IF @@ROWCOUNT<>1 THROW 51000,N'El gasto del despacho cambió antes de aceptar su aprobación.',1;
            UPDATE dispatch.Closures SET ExpectedCash=(SELECT GrossCash FROM dispatch.PaymentTotals(@Dispatch))-
              (SELECT COALESCE(SUM(ApprovedAmount),0) FROM dbo.DispatchExpenses WHERE DispatchId=@Dispatch AND ApprovalStatus=N'Approved')
            OUTPUT inserted.ExpectedCash
            WHERE DispatchId=@Dispatch AND Status=N'PendingReview';
            """, connection, tx);
        ApprovalParameters(command, user, request);
        command.Parameters.AddWithValue("@Expense", request.ExpenseId); command.Parameters.AddWithValue("@User", user.UserId);
        command.Parameters.AddWithValue("@Now", now); command.Parameters.AddWithValue("@Cash", cashSpent);
        var expected = await command.ExecuteScalarAsync(ct);
        if (expected is not decimal amount) throw new ExpenseConflictException("El cierre cambió antes de guardar la aprobación del gasto.");
        return new(request.DispatchId!.Value, request.DispatchExpenseId!.Value, cashSpent, amount);
    }

    private static void ApprovalParameters(SqlCommand command, ExpenseUserIdentity user, ConfirmExpenseRequest request)
    {
        command.Parameters.AddWithValue("@Tenant", user.TenantId); command.Parameters.AddWithValue("@Business", user.BusinessId);
        command.Parameters.AddWithValue("@Dispatch", request.DispatchId!.Value); command.Parameters.AddWithValue("@Report", request.DispatchExpenseId!.Value);
        command.Parameters.AddWithValue("@Form", request.DispatchCashPaymentMethodId!.Value); command.Parameters.AddWithValue("@Version", request.DispatchCashPaymentMethodVersion!.Value);
    }
}
