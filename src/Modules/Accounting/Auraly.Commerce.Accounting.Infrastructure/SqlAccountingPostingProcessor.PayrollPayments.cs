using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private static async Task<FinancialFactsResult> LoadPayrollPaymentFactsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source, CancellationToken token)
    {
        var payload = LoadPayrollPayload(source);
        var credits = payload.Lines.Where(line => line.Credit > 0).ToArray();
        var debits = payload.Lines.Where(line => line.Debit > 0).ToArray();
        if (payload.RunKind != "Payment" || credits.Length != 1 || credits[0].PartyId is not null ||
            debits.Any(line => line.Category != PayrollAccountingCategories.NetPayable || line.PartyId is null))
            throw new InvalidOperationException("The payroll payment lines are inconsistent.");
        var requested = debits.GroupBy(line => line.PartyId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Debit));

        // One bounded read for the entire batch. Payroll category lines preserve
        // source order through BuildLines/AccountingJournal/InsertEntryAsync.
        // Match that ordinal AND party/amount to the posted accrual, rather than
        // guessing an account from its code or today's NetPayrollPayable mapping.
        await using var command = new SqlCommand("""
            DECLARE @Requested TABLE(PartyId uniqueidentifier PRIMARY KEY,Amount decimal(19,4));
            DECLARE @Run uniqueidentifier,@RunCount int;
            INSERT @Requested SELECT employee.PartyId,SUM(line.Amount)
            FROM payroll.PaymentBatches batch
            JOIN payroll.PaymentLines line ON line.PaymentBatchId=batch.PaymentBatchId
            JOIN payroll.RunEmployees employee ON employee.PayrollRunEmployeeId=line.PayrollRunEmployeeId AND employee.TenantId=batch.TenantId
            JOIN payroll.Runs run ON run.PayrollRunId=employee.PayrollRunId AND run.TenantId=batch.TenantId AND run.BusinessId=batch.BusinessId
            WHERE batch.PaymentBatchId=@DocumentId AND batch.TenantId=@TenantId AND batch.BusinessId=@BusinessId AND batch.Status=N'Confirmed'
            GROUP BY employee.PartyId;
            SELECT @RunCount=COUNT(DISTINCT employee.PayrollRunId) FROM payroll.PaymentLines line
            JOIN payroll.RunEmployees employee ON employee.PayrollRunEmployeeId=line.PayrollRunEmployeeId
            JOIN payroll.Runs run ON run.PayrollRunId=employee.PayrollRunId AND run.TenantId=@TenantId AND run.BusinessId=@BusinessId
            WHERE line.PaymentBatchId=@DocumentId;
            SELECT TOP(1) @Run=employee.PayrollRunId FROM payroll.PaymentLines line
            JOIN payroll.RunEmployees employee ON employee.PayrollRunEmployeeId=line.PayrollRunEmployeeId
            WHERE line.PaymentBatchId=@DocumentId;
            SELECT PaymentMethodId,PaymentMethodTypeCode,AccountId,TotalAmount FROM payroll.PaymentBatches
            WHERE PaymentBatchId=@DocumentId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND Status=N'Confirmed' AND @RunCount=1 AND (SELECT COUNT(*) FROM @Requested) BETWEEN 1 AND @Limit;

            DECLARE @Original TABLE(PartyId uniqueidentifier,AccountId uniqueidentifier,CostCenterId uniqueidentifier,Credit decimal(19,4));
            INSERT @Original
            SELECT posted.PartyId,posted.AccountId,posted.CostCenterId,posted.Credit
            FROM payroll.Runs run
            JOIN dbo.AccountingSourceDocuments original ON original.TenantId=run.TenantId AND original.BusinessId=run.BusinessId
              AND original.SourceDocumentId=run.PayrollRunId
              AND original.SourceDocumentType=CASE WHEN run.RunKind=N'Adjustment' THEN N'PayrollAdjustment' ELSE N'PayrollAccrual' END
            JOIN dbo.AccountingPostingJobs job ON job.TenantId=original.TenantId AND job.BusinessId=original.BusinessId
              AND job.SourceDocumentId=original.SourceDocumentId AND job.SourceDocumentType=original.SourceDocumentType AND job.Status=N'Posted'
              AND job.SourcePayloadHash=original.PayloadHash
            JOIN dbo.AccountingEntries entry ON entry.TenantId=original.TenantId AND entry.BusinessId=original.BusinessId
              AND entry.SourceDocumentId=original.SourceDocumentId AND entry.SourceDocumentType=original.SourceDocumentType
              AND entry.SourcePayloadHash=original.PayloadHash
            CROSS APPLY OPENJSON(original.PayloadJson,'$.lines') ordinal
            CROSS APPLY OPENJSON(ordinal.value) WITH(Category nvarchar(64) '$.category',PartyId uniqueidentifier '$.partyId',
              Debit decimal(19,4) '$.debit',Credit decimal(19,4) '$.credit') fact
            JOIN dbo.AccountingEntryLines posted ON posted.EntryId=entry.EntryId AND posted.LineNumber=CONVERT(int,ordinal.[key])+1
              AND posted.PartyId=fact.PartyId AND posted.Debit=0 AND posted.Credit=fact.Credit
            JOIN dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts))
              ON account.AccountId=posted.AccountId AND account.TenantId=@TenantId
                AND account.IsActive=1 AND account.AllowsPosting=1
            WHERE run.PayrollRunId=@Run AND run.TenantId=@TenantId AND run.BusinessId=@BusinessId
              AND fact.Category=@Category AND fact.Debit=0 AND fact.Credit>0;
            -- Materialize the aggregate once before matching workers. A joined
            -- aggregate can otherwise be scanned again for every requested party.
            DECLARE @OriginalAccounts TABLE(PartyId uniqueidentifier,AccountId uniqueidentifier,CostCenterId uniqueidentifier,
              Credit decimal(19,4),AccountCount int);
            INSERT @OriginalAccounts
            SELECT PartyId,AccountId,CostCenterId,SUM(Credit),COUNT(*) OVER(PARTITION BY PartyId)
            FROM @Original GROUP BY PartyId,AccountId,CostCenterId OPTION(RECOMPILE);
            SELECT requested.PartyId,original.AccountId,requested.Amount,original.CostCenterId
            FROM @Requested requested LEFT JOIN @OriginalAccounts original
              ON original.PartyId=requested.PartyId AND original.AccountCount=1 AND original.Credit>=requested.Amount
            OPTION(RECOMPILE);
            """, connection, transaction);
        AddSource(command, source);
        command.Parameters.AddWithValue("@Limit", PayrollPaymentLimits.MaximumEmployees);
        command.Parameters.AddWithValue("@Category", PayrollAccountingCategories.NetPayable);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("The accepted payroll payment batch is missing or inconsistent.");
        Guid? formId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
        var formType = reader.IsDBNull(1) ? null : reader.GetString(1);
        Guid? settlementAccount = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        if (formId != payload.PaymentMethodId || formType != payload.PaymentMethodTypeCode ||
            reader.GetDecimal(3) != credits[0].Credit || formId is not null && settlementAccount is null)
            throw new InvalidOperationException("The accepted payroll payment form does not match its source.");

        var originalPayables = new List<ManualLineSpec>(requested.Count);
        var seen = new HashSet<Guid>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            var partyId = reader.GetGuid(0);
            if (reader.IsDBNull(1) || !seen.Add(partyId))
                return FinancialFactsResult.Pending("PayrollOriginalPayableMissing",
                    "No se puede identificar una única cuenta y centro de la causación original para cada trabajador.");
            if (!requested.TryGetValue(partyId, out var amount) || amount != reader.GetDecimal(2))
                throw new InvalidOperationException("The payroll payment members do not match their accepted source.");
            originalPayables.Add(new(reader.GetGuid(1), amount, 0, partyId, reader.IsDBNull(3)?null:reader.GetGuid(3), payload.Description));
        }
        if (originalPayables.Count != requested.Count)
            throw new InvalidOperationException("The payroll payment members are incomplete.");
        if (settlementAccount is { } accountId)
        {
            originalPayables.Add(new(accountId, 0, credits[0].Credit, null, null, credits[0].Description!));
            return FinancialFactsResult.Ready(FinancialFacts.Manual(payload.Description, originalPayables));
        }
        // Historical requests without a tenant form keep their settlement category;
        // they still cancel the original payable through the same posting path.
        return FinancialFactsResult.Ready(FinancialFacts.Payroll(payload.Description,
            [new(credits[0].Category, 0, credits[0].Credit, null, credits[0].Description!)], originalPayables));
    }
}
