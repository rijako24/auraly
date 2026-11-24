using System.Data;
using System.Text.Json;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore
{
    // One additional, bounded batch for configured corrections. The source
    // account is evidence of the original posting; the destination is NOT
    // frozen here and is resolved by the accounting processor on each attempt.
    private static async Task ValidateConfiguredClosureCorrectionsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid closureId, Guid businessId,
        List<WorkSessionAppliedPaymentCorrection> corrections, bool lockType, DateTimeOffset now, CancellationToken token)
    {
        var selected = corrections.Where(item => item.PaymentMethodId is not null).ToArray();
        if (selected.Length == 0) return;
        await using var command = new SqlCommand("""
            SELECT input.VerificationKey,form.PaymentMethodId,form.Name,original.AccountId,job.AccountingEntryRequired
            FROM OPENJSON(@Rows) WITH(VerificationKey nvarchar(200),PaymentMethodId uniqueidentifier,
              PaymentMethodVersion int,TenderMethodCode nvarchar(32),MovementType nvarchar(32),
              SourceId uniqueidentifier,SourceNumber int,SourceDocumentType nvarchar(64),
              OriginalTenderCategory nvarchar(128)) input
            JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionClosureId=@Closure
            JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId AND session.Kind=N'Normal' AND session.BusinessId=@Business
            CROSS APPLY (VALUES(CASE
              WHEN input.SourceDocumentType=N'ManualAccountingVoucher' THEN N'Vouchers'
              WHEN input.MovementType=N'PayablePayment' THEN N'SupplierPayments'
              WHEN input.MovementType=N'ReceivablePayment' THEN N'Receipts'
              ELSE N'Sales' END,
              CASE WHEN input.MovementType=N'PayablePayment' OR input.MovementType=N'CashOut'
                THEN N'SupplierPaymentMethod'
                WHEN input.MovementType=N'ReceivablePayment' OR input.SourceDocumentType=N'ManualAccountingVoucher'
                  THEN N'CustomerPaymentMethod'
                ELSE N'PosPaymentMethod' END)) usage(UsageCode,SourceType)
            LEFT JOIN reference.PaymentMethods form WITH(HOLDLOCK) ON form.TenantId=session.TenantId
              AND form.PaymentMethodId=input.PaymentMethodId AND form.Version=input.PaymentMethodVersion AND form.IsActive=1
              AND ((usage.UsageCode=N'Vouchers' AND form.UseForVouchers=1)
                OR (usage.UsageCode=N'SupplierPayments' AND form.UseForSupplierPayments=1)
                OR (usage.UsageCode=N'Receipts' AND form.UseForReceipts=1)
                OR (usage.UsageCode=N'Sales' AND form.UseForSales=1))
            LEFT JOIN reference.Options type ON type.OptionId=form.PaymentTypeId
              AND type.CatalogCode=N'payment-method' AND type.IsActive=1 AND type.Code=input.TenderMethodCode
            OUTER APPLY accounting.PaymentMethodAccountReadiness(session.TenantId,type.Code,form.AccountId) ready
            LEFT JOIN dbo.WorkSessionMovements movement ON movement.WorkSessionMovementId=input.SourceId
              AND movement.WorkSessionId=session.WorkSessionId
            LEFT JOIN dbo.AccountingPostingJobs job ON job.TenantId=session.TenantId AND job.BusinessId=session.BusinessId
              AND job.SourceDocumentId=COALESCE(movement.DocumentId,input.SourceId)
              AND job.SourceDocumentType=CASE WHEN input.MovementType=N'ReceivablePayment' THEN N'ReceivablePayment'
                WHEN input.MovementType=N'PayablePayment' THEN N'PayablePayment' ELSE input.SourceDocumentType END
              AND job.Status IN(N'Posted',N'CommercialEffectsApplied')
            LEFT JOIN dbo.SalesPayments sale ON input.MovementType=N'Sale' AND sale.DocumentId=job.SourceDocumentId
              AND sale.PaymentNumber=input.SourceNumber
            OUTER APPLY (SELECT binding.AccountId FROM OPENJSON(job.ResolvedAccountMappingsJson)
              WITH(Category nvarchar(128),AccountId uniqueidentifier) binding
              WHERE binding.Category=input.OriginalTenderCategory) mapping
            OUTER APPLY (SELECT binding.AccountId FROM OPENJSON(job.ResolvedBankAccountsJson)
              WITH(BankAccountId uniqueidentifier,AccountId uniqueidentifier) binding
              WHERE input.OriginalTenderCategory=CONCAT(N'BankAccount:',CONVERT(nvarchar(36),binding.BankAccountId))) bank
            OUTER APPLY (SELECT account.AccountId FROM dbo.AccountingAccounts account
              WHERE account.TenantId=session.TenantId AND account.AccountId=COALESCE(
                CASE WHEN input.OriginalTenderCategory LIKE N'PostedAccount:%'
                  THEN TRY_CONVERT(uniqueidentifier,SUBSTRING(input.OriginalTenderCategory,15,36)) END,
                sale.AccountId,mapping.AccountId,bank.AccountId)) original
            WHERE ready.SourceType=usage.SourceType
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments direct
                WHERE direct.TenantId=session.TenantId AND direct.BusinessId=session.BusinessId
                  AND direct.CorrectsSaleDocumentId=input.SourceId AND direct.PaymentCorrectionVerificationKey=input.VerificationKey);
            IF @LockType=1
              UPDATE form SET TypeLockedAt=@Now
              FROM reference.PaymentMethods form
              JOIN dbo.Businesses business ON business.TenantId=form.TenantId AND business.BusinessId=@Business
              WHERE form.TypeLockedAt IS NULL AND EXISTS(SELECT 1 FROM OPENJSON(@Rows)
                WITH(PaymentMethodId uniqueidentifier) input WHERE input.PaymentMethodId=form.PaymentMethodId);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Closure", closureId);
        command.Parameters.AddWithValue("@Business", businessId);
        command.Parameters.AddWithValue("@LockType", lockType);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(selected);
        var validated = new Dictionary<string, (string Name, Guid? Original)>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.IsDBNull(1) || reader.IsDBNull(2))
                throw new WorkSessionValidationException("La forma de pago cambió, pertenece a otra empresa o no está habilitada para esta operación.");
            if (reader.IsDBNull(4) || reader.IsDBNull(3) && reader.GetBoolean(4))
                throw new WorkSessionValidationException("No se identificó la cuenta original del pago. Revisa su contabilización antes de corregir el medio de recaudo.");
            if (!validated.TryAdd(reader.GetString(0), (reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3))))
                throw new WorkSessionValidationException("La cuenta original del pago no es inequívoca.");
        }
        if (validated.Count != selected.Length)
            throw new WorkSessionValidationException("La forma de pago no tiene una cuenta válida para esta operación.");
        for (var index = 0; index < corrections.Count; index++)
        {
            var correction = corrections[index];
            if (correction.PaymentMethodId is null) continue;
            var evidence = validated[correction.VerificationKey];
            corrections[index] = correction with
            {
                OriginalTenderCategory = evidence.Original is { } original
                    ? $"PostedAccount:{original:D}" : correction.OriginalTenderCategory,
                PaymentMethodName = evidence.Name
            };
        }
    }
}
