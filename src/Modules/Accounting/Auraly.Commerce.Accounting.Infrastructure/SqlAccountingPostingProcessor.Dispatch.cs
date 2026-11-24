using System.Data;
using System.Text.Json;
using Auraly.Contracts.Dispatching;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private static async Task<Guid?> ReadOriginalCardRefundAccountAsync(SqlConnection connection,
        SqlTransaction transaction,SourceEnvelope source,int? paymentNumber,Guid? dispatchPaymentId,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            SELECT original.EffectiveAccountId FROM dbo.SalesReturns returned
            CROSS APPLY accounting.SalePaymentPostingFacts(@Tenant,@Business,returned.OriginalDocumentId) original
            WHERE returned.ReturnId=@Return AND returned.BusinessId=@Business AND @DispatchPayment IS NULL
              AND original.PaymentNumber=@PaymentNumber AND original.EffectivePaymentReady=1
              AND original.PostingStatus IN(N'Posted',N'CommercialEffectsApplied')
            UNION ALL
            SELECT applied.AccountId FROM dbo.SalesReturns returned
            JOIN dbo.DispatchSourceDocuments document ON document.SourceDocumentId=returned.OriginalDocumentId
            JOIN dbo.DispatchDeliveryPayments payment ON payment.DispatchSourceDocumentId=document.DispatchSourceDocumentId
              AND payment.DispatchDeliveryPaymentId=@DispatchPayment AND payment.BusinessId=@Business
            JOIN dbo.WorkSessions session ON session.DispatchId=document.DispatchId AND session.Kind=N'Dispatch'
              AND session.TenantId=@Tenant AND session.BusinessId=@Business
            JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
            JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=closure.WorkSessionClosureId
              AND job.SourceDocumentType=N'DispatchCashDifference' AND job.BusinessId=@Business AND job.TenantId=@Tenant
              AND job.Status IN(N'Posted',N'CommercialEffectsApplied')
            CROSS APPLY OPENJSON(job.AppliedPaymentMethodsJson) WITH(SourceLineId uniqueidentifier,AccountId uniqueidentifier) applied
            WHERE returned.ReturnId=@Return AND returned.BusinessId=@Business AND applied.SourceLineId=@DispatchPayment;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",source.TenantId);
        command.Parameters.AddWithValue("@Business",source.BusinessId);
        command.Parameters.AddWithValue("@Return",source.DocumentId);
        command.Parameters.AddWithValue("@PaymentNumber",(object?)paymentNumber??DBNull.Value);
        command.Parameters.AddWithValue("@DispatchPayment",(object?)dispatchPaymentId??DBNull.Value);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token) || reader.IsDBNull(0)) return null;
        var account=reader.GetGuid(0);
        if(await reader.ReadAsync(token)) throw new InvalidOperationException("La reversión tiene más de un auxiliar de tarjeta original.");
        return account;
    }

    private static async Task<SourceEnvelope?> ResolveDispatchCollectionsAsync(SqlConnection connection,
        SqlTransaction transaction, SourceEnvelope source, DateTimeOffset processedAt, CancellationToken token)
    {
        var payload = JsonSerializer.Deserialize<DispatchCashDifferencePayload>(source.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("La conciliación del despacho no conserva su fuente.");
        await using (var dependencies = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM dispatch.SettlementDocuments(@Dispatch,@Business) dependency
            LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=dependency.DocumentId
              AND job.SourceDocumentType=dependency.DocumentType AND job.TenantId=@Tenant AND job.BusinessId=@Business
            WHERE job.Status IS NULL OR job.Status NOT IN(N'Posted',N'CommercialEffectsApplied');
            """,connection,transaction))
        {
            dependencies.Parameters.AddWithValue("@Tenant",source.TenantId);
            dependencies.Parameters.AddWithValue("@Business",source.BusinessId);
            dependencies.Parameters.AddWithValue("@Dispatch",payload.DispatchId);
            if(Convert.ToInt64(await dependencies.ExecuteScalarAsync(token))!=0) return null;
        }
        if(payload.Collections is null) return source;
        var collections=payload.Collections;
        if(collections.Count==0 && payload.Difference==0) return source;
        var differenceForm=payload.CashDifferencePaymentMethodId;
        if(payload.Difference!=0 && differenceForm is null)
        {
            var cashForms=collections.Where(item=>item.MethodCode=="Cash").Select(item=>item.PaymentMethodId).Distinct().ToArray();
            if(cashForms.Length!=1) throw new InvalidOperationException("La diferencia no conserva una forma de efectivo inequívoca.");
            differenceForm=cashForms[0];
        }
        if (collections.Count > DispatchLimits.MaximumCollectedPayments || collections.Any(item => item.PaymentId == Guid.Empty ||
                item.SourceDocumentId == Guid.Empty || item.PaymentMethodId == Guid.Empty || item.Amount <= 0) ||
            collections.Select(item => item.PaymentId).Distinct().Count() != collections.Count)
            throw new InvalidOperationException("Los recaudos aceptados del despacho no son válidos.");

        // One bounded batch for every tender and original sale. No lookup or
        // account resolution per invoice, and no current-map fallback for an
        // original posted account.
        await using var command = new SqlCommand("""
            DECLARE @Requested TABLE(PaymentId uniqueidentifier PRIMARY KEY,SourceDocumentId uniqueidentifier,
              PaymentMethodId uniqueidentifier,MethodCode nvarchar(32),Amount decimal(19,4));
            INSERT @Requested SELECT PaymentId,SourceDocumentId,PaymentMethodId,MethodCode,Amount
            FROM OPENJSON(@Rows) WITH(PaymentId uniqueidentifier,SourceDocumentId uniqueidentifier,
              PaymentMethodId uniqueidentifier,MethodCode nvarchar(32),Amount decimal(19,4));
            DECLARE @Resolved TABLE(PaymentId uniqueidentifier PRIMARY KEY,SourceDocumentId uniqueidentifier,
              FormId uniqueidentifier,MethodCode nvarchar(32),Version int,Code nvarchar(64),Name nvarchar(160),
              AccountId uniqueidentifier,AccountCode nvarchar(32),AccountName nvarchar(200),BankAccountId uniqueidentifier,
              Amount decimal(19,4));
            INSERT @Resolved
            SELECT input.PaymentId,input.SourceDocumentId,form.PaymentMethodId,type.Code,form.Version,form.Code,form.Name,
              account.AccountId,account.Code,account.Name,account.BankAccountId,input.Amount
            FROM @Requested input
            JOIN dbo.DispatchSourceDocuments document ON document.SourceDocumentId=input.SourceDocumentId AND document.DispatchId=@Dispatch
            JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=document.DispatchId AND dispatch.TenantId=@Tenant AND dispatch.BusinessId=@Business
            JOIN dbo.DispatchDeliveryPayments payment ON payment.DispatchDeliveryPaymentId=input.PaymentId
              AND payment.DispatchSourceDocumentId=document.DispatchSourceDocumentId AND payment.Amount=input.Amount
            JOIN reference.PaymentMethods form WITH(HOLDLOCK) ON form.PaymentMethodId=input.PaymentMethodId AND form.TenantId=@Tenant
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId AND type.Code=input.MethodCode
              AND type.CatalogCode=N'payment-method' AND type.IsActive=1
            JOIN dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts)) ON account.AccountId=form.AccountId AND account.TenantId=@Tenant
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) ready
            WHERE form.IsActive=1 AND form.UseForReceipts=1 AND ready.SourceType=N'CustomerPaymentMethod';
            DECLARE @DifferenceAccount uniqueidentifier;
            SELECT @DifferenceAccount=form.AccountId FROM reference.PaymentMethods form WITH(HOLDLOCK)
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
              AND type.Code=N'Cash' AND type.IsActive=1
            JOIN dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts))
              ON account.TenantId=@Tenant AND account.AccountId=form.AccountId
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) ready
            WHERE form.PaymentMethodId=@DifferenceForm AND form.TenantId=@Tenant AND form.IsActive=1
              AND form.UseForReceipts=1 AND ready.SourceType=N'CustomerPaymentMethod';
            UPDATE account SET ConfigurationLockedAt=@Now
            FROM dbo.AccountingAccounts account WHERE account.TenantId=@Tenant AND account.ConfigurationLockedAt IS NULL
              AND (account.AccountId=@DifferenceAccount OR EXISTS(SELECT 1 FROM @Resolved resolved WHERE resolved.AccountId=account.AccountId));
            SELECT * FROM @Resolved;

            SELECT original.DocumentId,original.PaymentNumber,original.Amount,
              original.AccountingEntryRequired,original.PostingStatus,original.AccountId,
              original.ReversedUncollectedAmount
            FROM (SELECT DISTINCT SourceDocumentId FROM @Requested) requested
            JOIN dbo.SalesDocuments sale ON sale.DocumentId=requested.SourceDocumentId AND sale.BusinessId=@Business
              AND sale.CollectionMode=N'Dispatch'
            CROSS APPLY accounting.SalePaymentPostingFacts(@Tenant,@Business,sale.DocumentId) original;
            SELECT @DifferenceAccount;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", source.TenantId);
        command.Parameters.AddWithValue("@Business", source.BusinessId);
        command.Parameters.AddWithValue("@Dispatch", payload.DispatchId);
        command.Parameters.AddWithValue("@Now", processedAt);
        command.Parameters.AddWithValue("@DifferenceForm",payload.Difference==0?DBNull.Value:(object?)differenceForm??DBNull.Value);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(collections);
        var actual = new List<(Guid Document, decimal Amount, Guid Account)>();
        var bindings = new List<VoucherAppliedPaymentMethod>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            actual.Add((reader.GetGuid(1), reader.GetDecimal(11), reader.GetGuid(7)));
            bindings.Add(new(reader.GetGuid(0), reader.GetGuid(2), reader.GetString(3), reader.GetInt32(4),
                reader.GetString(5), reader.GetString(6), reader.GetGuid(7), reader.GetString(8),
                reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetGuid(10)));
        }
        if (bindings.Count != collections.Count) return null;
        await reader.NextResultAsync(token);
        var originals = new List<(Guid Document, decimal Amount, Guid? Account, bool Required)>();
        var keys = new HashSet<(Guid, int)>();
        while (await reader.ReadAsync(token))
        {
            if (!keys.Add((reader.GetGuid(0), reader.GetInt32(1))) || reader.IsDBNull(3) || reader.IsDBNull(4) ||
                reader.GetString(4) is not ("Posted" or "CommercialEffectsApplied")) return null;
            var required = reader.GetBoolean(3);
            if (required && reader.IsDBNull(5)) return null;
            var amount = reader.GetDecimal(2) - reader.GetDecimal(6);
            if (amount < 0) throw new InvalidOperationException("Las devoluciones superan el pago original del pedido.");
            originals.Add((reader.GetGuid(0), amount, reader.IsDBNull(5) ? null : reader.GetGuid(5), required));
        }
        await reader.NextResultAsync(token);
        Guid? differenceAccount=await reader.ReadAsync(token) && !reader.IsDBNull(0)?reader.GetGuid(0):null;
        if(payload.Difference!=0 && differenceAccount is null) return null;
        var originalTotals = originals.GroupBy(item => item.Document).ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var actualTotals = actual.GroupBy(item => item.Document).ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        if (actualTotals.Any(item => !originalTotals.TryGetValue(item.Key, out var originalTotal) || originalTotal != item.Value))
            return null;
        var lines = new List<CategoryLineSpec>();
        foreach (var original in originals.Where(item => item.Required && item.Amount > 0))
            lines.Add(new($"PostedAccount:{original.Account:D}", 0, original.Amount, null, "Reclasificación del recaudo del pedido"));
        var accountedDocuments = originals.Where(item => item.Required).Select(item => item.Document).ToHashSet();
        foreach (var payment in actual.Where(item => accountedDocuments.Contains(item.Document)))
            lines.Add(new($"PostedAccount:{payment.Account:D}", payment.Amount, 0, null, "Recaudo confirmado del pedido"));
        return source with { DispatchCollectionLines = lines, AppliedPaymentMethods = bindings, DispatchCashDifferenceAccountId=differenceAccount };
    }
}
