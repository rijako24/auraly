using System.Data;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private static async Task<SourceEnvelope?> ResolveClosurePaymentMethodsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        DateTimeOffset processedAt, CancellationToken token)
    {
        var payload = ReadClosureReconciliation(source);
        var selected = (payload.PaymentCorrections ?? []).Where(item => item.PaymentMethodId is not null).ToArray();
        if (selected.Length == 0) return source;
        if (selected.Any(item => item.CorrectionId is null || source.AccountingEntryRequired &&
            (item.OriginalTenderCategory is null || !TryParsePostedAccountCategory(item.OriginalTenderCategory, out _))))
            throw new InvalidOperationException("La corrección no conserva la identidad y cuenta original del pago.");
        await using var command = new SqlCommand("""
            DECLARE @Resolved TABLE(CorrectionId uniqueidentifier PRIMARY KEY,PaymentMethodId uniqueidentifier,
              MethodCode nvarchar(32),Version int,Code nvarchar(64),Name nvarchar(160),AccountId uniqueidentifier,
              AccountCode nvarchar(32),AccountName nvarchar(200),BankAccountId uniqueidentifier);
            INSERT @Resolved
            SELECT input.CorrectionId,form.PaymentMethodId,type.Code,form.Version,form.Code,form.Name,
              account.AccountId,account.Code,account.Name,CASE WHEN type.Code=N'Transfer' THEN form.BankAccountId END
            FROM OPENJSON(@Rows) WITH(CorrectionId uniqueidentifier,PaymentMethodId uniqueidentifier,
              TenderMethodCode nvarchar(32),MovementType nvarchar(32),SourceDocumentType nvarchar(64)) input
            JOIN reference.PaymentMethods form WITH(UPDLOCK,HOLDLOCK) ON form.TenantId=@Tenant AND form.PaymentMethodId=input.PaymentMethodId
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId
              AND type.CatalogCode=N'payment-method' AND type.Code=input.TenderMethodCode AND type.IsActive=1
            JOIN dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts)) ON account.TenantId=@Tenant AND account.AccountId=form.AccountId
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) ready
            WHERE form.IsActive=1 AND ready.SourceType=CASE
                WHEN input.MovementType IN(N'PayablePayment',N'CashOut') THEN N'SupplierPaymentMethod'
                WHEN input.MovementType=N'ReceivablePayment' OR input.SourceDocumentType=N'ManualAccountingVoucher'
                  THEN N'CustomerPaymentMethod' ELSE N'PosPaymentMethod' END
              AND ((input.SourceDocumentType=N'ManualAccountingVoucher' AND form.UseForVouchers=1)
                OR (input.SourceDocumentType<>N'ManualAccountingVoucher' AND
                  ((input.MovementType=N'PayablePayment' AND form.UseForSupplierPayments=1)
                    OR (input.MovementType=N'ReceivablePayment' AND form.UseForReceipts=1)
                    OR (input.MovementType NOT IN(N'PayablePayment',N'ReceivablePayment') AND form.UseForSales=1))));
            UPDATE account SET ConfigurationLockedAt=@Now FROM dbo.AccountingAccounts account
            WHERE account.TenantId=@Tenant AND account.ConfigurationLockedAt IS NULL
              AND EXISTS(SELECT 1 FROM @Resolved resolved WHERE resolved.AccountId=account.AccountId);
            SELECT CorrectionId,PaymentMethodId,MethodCode,Version,Code,Name,AccountId,AccountCode,AccountName,BankAccountId
            FROM @Resolved;
            SELECT Status,ResolvedAccountMappingsJson FROM dbo.AccountingPostingJobs
            WHERE TenantId=@Tenant AND BusinessId=@Business AND SourceDocumentId=@Closure
              AND SourceDocumentType=N'WorkSessionCashDifference';
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", source.TenantId);
        command.Parameters.AddWithValue("@Business", source.BusinessId);
        command.Parameters.AddWithValue("@Closure", payload.WorkSessionClosureId);
        command.Parameters.AddWithValue("@Now", processedAt);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(selected);
        var bindings = new Dictionary<Guid, VoucherAppliedPaymentMethod>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var binding = new VoucherAppliedPaymentMethod(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetGuid(6), reader.GetString(7),
                reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetGuid(9));
            bindings.Add(binding.SourceLineId, binding);
        }
        if (bindings.Count != selected.Length) return null;
        // The closure may already have posted its blind-count differences.
        // Undo their actual auxiliaries, not today's remapped group accounts.
        var closureAccounts = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await reader.NextResultAsync(token);
        if (await reader.ReadAsync(token))
        {
            if (reader.GetString(0) is not ("Posted" or "CommercialEffectsApplied")) return null;
            if (!reader.IsDBNull(1))
            {
                using var mappings = JsonDocument.Parse(reader.GetString(1));
                foreach (var mapping in mappings.RootElement.EnumerateArray())
                    closureAccounts.Add(mapping.GetProperty("Category").GetString()!, mapping.GetProperty("AccountId").GetGuid());
            }
            if (reader.GetString(0) == "Posted" && closureAccounts.Count == 0) return null;
        }
        var resolved = payload with
        {
            Lines = payload.Lines.Select(line => closureAccounts.TryGetValue(line.AccountingCategory, out var original)
                ? line with { AccountingCategory = $"PostedAccount:{original:D}" } : line).ToArray(),
            PaymentCorrections = payload.PaymentCorrections!.Select(item => item.CorrectionId is { } id && bindings.TryGetValue(id, out var binding)
                ? item with { TenderCategory = $"PostedAccount:{binding.AccountId:D}" } : item).ToArray()
        };
        // Only this attempt's in-memory source changes. SQL retains the accepted
        // payload/hash; successful completion stores these outcome bindings.
        return source with
        {
            PayloadJson = JsonSerializer.Serialize(resolved, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            AppliedPaymentMethods = bindings.Values.ToArray()
        };
    }

    private async Task ApplyCashShortageClaimAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, WorkSessionCashShortageClaim claim,
        CancellationToken token)
    {
        await using var command = new SqlCommand(DeclareReceivableOpeningsSql + """
            IF NOT EXISTS(SELECT 1 FROM dbo.Parties
              WHERE PartyId=@PartyId AND TenantId=@TenantId)
              THROW 51607,'The accepted shortage debtor is outside the tenant.',1;

            IF EXISTS(SELECT 1 FROM dbo.Receivables WITH(UPDLOCK,HOLDLOCK)
              WHERE ReceivableId=@ReceivableId)
            BEGIN
              -- Before this cutover the closure acceptance wrote this pair.
              -- Preserve a matching historical obligation and its later payments.
              IF EXISTS(SELECT 1 FROM dbo.Receivables
                WHERE ReceivableId=@ReceivableId AND
                  (BusinessId<>@BusinessId OR PartyId IS NULL OR PartyId<>@PartyId
                   OR CustomerId IS NOT NULL OR SourceDocumentId<>@ReconciliationId
                   OR SourceDocumentType<>N'WorkSessionClosureReconciliation'
                   OR CurrencyCode<>N'COP' OR OriginalAmount<>@Amount))
                OR NOT EXISTS(SELECT 1 FROM dbo.ReceivableTransactions
                  WHERE ReceivableId=@ReceivableId AND TransactionType=N'Opening'
                    AND SourceDocumentId=@ReconciliationId AND Amount=@Amount)
                THROW 51607,'The historical shortage obligation does not match its source.',1;
            END
            ELSE
            BEGIN
              INSERT @ReceivableOpenings(ReceivableId,TransactionId,PartyId,DocumentId,DocumentType,Number,Amount,DueDate,OccurredAt)
              VALUES(@ReceivableId,@TransactionId,@PartyId,@ReconciliationId,N'WorkSessionClosureReconciliation',@DocumentNumber,@Amount,@At,@At);
            END;
            """ + OpenReceivablesSql, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        command.Parameters.AddWithValue("@BusinessId", payload.BusinessId);
        command.Parameters.AddWithValue("@ReceivableId", claim.ReceivableId);
        command.Parameters.AddWithValue("@PartyId", claim.EmployeePartyId);
        command.Parameters.AddWithValue("@ReconciliationId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@DocumentNumber", $"FALT-{payload.WorkSessionClosureId:N}");
        AddMoney(command, "@Amount", claim.Amount);
        command.Parameters.AddWithValue("@At", payload.ReconciledAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<IReadOnlyList<Guid>> ApplyClosureCorrectionFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, CancellationToken token)
    {
        if (payload.CashShortageClaim is { } claim)
            await ApplyCashShortageClaimAsync(connection, transaction, payload, claim, token);
        var changes = (payload.PaymentCorrections ?? [])
            .Where(item => item.Amount != item.OriginalAmount || item.MovementType == "CreditSale")
            .Select(item => new
            {
                item.MovementType, item.SourceId, item.SubledgerId, item.CustomerId,
                Delta = item.MovementType == "CreditSale" ? item.Amount : item.Amount - item.OriginalAmount,
                AdjustmentId = item.MovementType == "CreditSale"
                    ? item.CorrectionId ?? throw new InvalidOperationException("El recaudo de crédito requiere su identidad aceptada.") : ids.NewId(),
                NewReceivableId = ids.NewId(),
                NewOpeningId = ids.NewId(), NewCreditId = ids.NewId(), NewCreditTransactionId = ids.NewId()
            }).ToArray();
        if (changes.Length == 0) return [];

        await using var command = new SqlCommand(DeclareReceivableOpeningsSql + DeclareCustomerCreditOpeningsSql +
            DeclarePortfolioBalanceChangesSql + """
            CREATE TABLE #Changes(MovementType nvarchar(32),SourceId uniqueidentifier,
              SubledgerId uniqueidentifier,CustomerId uniqueidentifier,Delta decimal(19,4),
              AdjustmentId uniqueidentifier,NewReceivableId uniqueidentifier,
              NewOpeningId uniqueidentifier,NewCreditId uniqueidentifier,NewCreditTransactionId uniqueidentifier);
            INSERT #Changes SELECT MovementType,SourceId,SubledgerId,CustomerId,Delta,
              AdjustmentId,NewReceivableId,NewOpeningId,NewCreditId,NewCreditTransactionId
            FROM OPENJSON(@Rows) WITH(MovementType nvarchar(32),SourceId uniqueidentifier,
              SubledgerId uniqueidentifier,CustomerId uniqueidentifier,Delta decimal(19,4),
              AdjustmentId uniqueidentifier,NewReceivableId uniqueidentifier,
              NewOpeningId uniqueidentifier,NewCreditId uniqueidentifier,NewCreditTransactionId uniqueidentifier);
            IF @@ROWCOUNT<>@Count THROW 51607,'The closure correction payload is incomplete.',1;
            IF EXISTS(SELECT 1 FROM #Changes WHERE Delta=0 OR MovementType NOT IN
              (N'Sale',N'CreditSale',N'ReceivablePayment',N'PayablePayment',N'Refund',N'CashIn',N'CashOut'))
              THROW 51607,'The closure correction type is invalid.',1;

            CREATE TABLE #Receivable(ReceivableId uniqueidentifier PRIMARY KEY,
              Adjustment decimal(19,4),AdjustmentId uniqueidentifier);
            INSERT #Receivable SELECT SubledgerId,-SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),AdjustmentId)))
            FROM #Changes WHERE MovementType IN(N'Sale',N'CreditSale',N'ReceivablePayment')
              AND SubledgerId IS NOT NULL GROUP BY SubledgerId;
            DELETE FROM #Receivable WHERE Adjustment=0;
            INSERT @PortfolioChanges SELECT ReceivableId,Adjustment FROM #Receivable;
            """ + PortfolioBalanceChangesSql("Receivables", "ReceivableId", PortfolioBalanceChangeKind.Adjustment) + """
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,
              TransactionType,Amount,SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
            SELECT AdjustmentId,ReceivableId,N'Adjustment',Adjustment,@ReconciliationId,@CreditParentDocumentType,@At,@Now
            FROM #Receivable;

            CREATE TABLE #NewReceivable(SourceId uniqueidentifier PRIMARY KEY,
              CustomerId uniqueidentifier,Adjustment decimal(19,4),
              ReceivableId uniqueidentifier,OpeningId uniqueidentifier);
            INSERT #NewReceivable SELECT SourceId,
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),CustomerId))),-SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),NewReceivableId))),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),NewOpeningId)))
            FROM #Changes WHERE MovementType=N'Sale' AND SubledgerId IS NULL
            GROUP BY SourceId;
            DELETE FROM #NewReceivable WHERE Adjustment=0;
            IF EXISTS(SELECT 1 FROM #NewReceivable WHERE Adjustment<=0 OR CustomerId IS NULL)
              THROW 51607,'The sale cannot open an unidentified customer balance.',1;
            INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartySiteId,DocumentId,DocumentType,
              Number,Amount,DueDate,OccurredAt,TransactionSourceId)
            SELECT input.ReceivableId,input.OpeningId,input.CustomerId,sale.CustomerPartySiteId,
              sale.DocumentId,sale.DocumentType,sale.DocumentNumber,
              input.Adjustment,@At,@At,@ReconciliationId
            FROM #NewReceivable input JOIN dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
              ON sale.DocumentId=input.SourceId AND sale.BusinessId=@BusinessId
                AND sale.CustomerId=input.CustomerId AND sale.CustomerPartySiteId IS NOT NULL
            WHERE NOT EXISTS(SELECT 1 FROM dbo.Receivables existing WITH(UPDLOCK,HOLDLOCK)
              WHERE existing.SourceDocumentId=sale.DocumentId AND existing.SourceDocumentType=sale.DocumentType);
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #NewReceivable)
              THROW 51607,'The sale balance changed after closure acceptance.',1;
            """ + OpenReceivablesSql + """
            CREATE TABLE #Payable(PayableId uniqueidentifier PRIMARY KEY,
              Adjustment decimal(19,4),AdjustmentId uniqueidentifier);
            INSERT #Payable SELECT SubledgerId,SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),AdjustmentId)))
            FROM #Changes WHERE MovementType=N'PayablePayment' GROUP BY SubledgerId;
            DELETE FROM #Payable WHERE Adjustment=0;
            DELETE FROM @PortfolioChanges;
            INSERT @PortfolioChanges SELECT PayableId,Adjustment FROM #Payable;
            """ + PortfolioBalanceChangesSql("Payables", "PayableId", PortfolioBalanceChangeKind.Adjustment) + """
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,
              TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT AdjustmentId,PayableId,N'Adjustment',Adjustment,@ReconciliationId,@At,@Now
            FROM #Payable;

            INSERT @CustomerCreditOpenings(CreditId,TransactionId,CustomerId,ReturnId,Nature,Currency,Amount)
            SELECT input.NewCreditId,input.NewCreditTransactionId,input.CustomerId,returned.ReturnId,
              N'Credit',N'COP',input.Delta
            FROM #Changes input JOIN dbo.SalesReturns returned WITH(UPDLOCK,HOLDLOCK)
              ON input.MovementType=N'Refund' AND returned.ReturnId=input.SourceId
                AND returned.BusinessId=@BusinessId AND returned.CustomerId=input.CustomerId
                AND returned.TotalAmount>=input.Delta
            WHERE input.Delta>0 AND NOT EXISTS(SELECT 1 FROM dbo.CustomerCredits credit WITH(UPDLOCK,HOLDLOCK)
              WHERE credit.SourceReturnId=returned.ReturnId);
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #Changes WHERE MovementType=N'Refund')
              THROW 51607,'The return credit changed after closure acceptance.',1;

            """ + OpenCustomerCreditsSql, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(changes);
        command.Parameters.AddWithValue("@Count", changes.Length);
        command.Parameters.AddWithValue("@BusinessId", payload.BusinessId);
        command.Parameters.AddWithValue("@ReconciliationId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@CreditParentDocumentId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@CreditParentDocumentType", WorkSessionAccountingDocumentTypes.ClosureReconciliation);
        command.Parameters.AddWithValue("@CreditAt", payload.ReconciledAt);
        command.Parameters.AddWithValue("@At", payload.ReconciledAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
        return changes.Where(item => item.CustomerId.HasValue && item.MovementType is "Sale" or "CreditSale" or "ReceivablePayment" or "Refund")
            .Select(item => item.CustomerId!.Value).Distinct().ToArray();
    }
}
