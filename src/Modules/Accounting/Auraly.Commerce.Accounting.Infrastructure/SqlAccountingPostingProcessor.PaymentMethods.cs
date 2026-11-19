using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    // Resolve inside the posting transaction/savepoint. Failed configuration or
    // journal validation rolls this evidence back; the next attempt reads the
    // then-current catalog. Completed jobs return before entering this method.
    private static async Task<SourceEnvelope?> ResolveConfiguredPaymentMethodsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source, DateTimeOffset processedAt, CancellationToken token)
    {
        if (source.DocumentType == Auraly.Contracts.Dispatching.DispatchAccountingDocumentTypes.CashDifference)
            return await ResolveDispatchCollectionsAsync(connection, transaction, source, processedAt, token);
        if (source.DocumentType == Auraly.Contracts.WorkSessions.WorkSessionAccountingDocumentTypes.ClosureReconciliation)
            return await ResolveClosurePaymentMethodsAsync(connection, transaction, source, processedAt, token);
        if (source.DocumentType == AccountingManualDocumentTypes.ManualVoucher)
            return await ResolveManualPaymentMethodsAsync(connection, transaction, source, processedAt, token);
        if (source.DocumentType is "RestaurantTip" or "RestaurantTipSettlement")
            return await ResolveRestaurantTipPaymentMethodAsync(connection, transaction, source, processedAt, token);
        if (source.DocumentType == Auraly.Contracts.Expenses.ExpenseDocumentTypes.Expense)
        {
            var expense = ReadExpenseSource(source);
            return expense.Payment?.PaymentMethodId is {} formId
                ? await ResolveSinglePaymentMethodAsync(connection,transaction,source,formId,expense.Payment.MethodCode,true,processedAt,token)
                : source;
        }
        var target = source.DocumentType switch
        {
            "SalesInvoice" or "SalesReceipt" or "ServiceInvoice" =>
                (Schema:"dbo",Table: "SalesPayments", Key: "DocumentId", Line: "PaymentNumber", Method:"MethodCode",Usage: "PosPaymentMethod"),
            "ReceivablePayment" =>
                (Schema:"dbo",Table: "CustomerPaymentTenders", Key: "PaymentId", Line: "LineNumber",Method:"MethodCode", Usage: "CustomerPaymentMethod"),
            "PayablePayment" =>
                (Schema:"dbo",Table: "SupplierPaymentTenders", Key: "PaymentId", Line: "LineNumber",Method:"MethodCode", Usage: "SupplierPaymentMethod"),
            "PayrollPayment" => (Schema:"payroll",Table:"PaymentBatches",Key:"PaymentBatchId",Line:(string?)null,Method:"PaymentMethodTypeCode",Usage:"SupplierPaymentMethod"),
            _ => default
        };
        if (target.Table is null) return source;
        using var payload = JsonDocument.Parse(source.PayloadJson);
        static bool Configured(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
            (value.TryGetProperty("PaymentMethodId", out var id) || value.TryGetProperty("paymentMethodId", out id)) &&
            id.ValueKind != JsonValueKind.Null;
        var configuredCount = source.DocumentType=="PayrollPayment"&&Configured(payload.RootElement)?1:0;
        if (payload.RootElement.TryGetProperty("Payments", out var payments) || payload.RootElement.TryGetProperty("payments", out payments))
            configuredCount = payments.EnumerateArray().Count(Configured);
        else if (payload.RootElement.TryGetProperty("Payment", out var payment) || payload.RootElement.TryGetProperty("payment", out payment))
            configuredCount = Configured(payment) ? 1 : 0;
        if ((payload.RootElement.TryGetProperty("Credit", out var credit) || payload.RootElement.TryGetProperty("credit", out credit)) && Configured(credit))
            configuredCount++;
        if (configuredCount == 0) return source;

        // Table/column names come exclusively from the closed source-type switch.
        // All tender identities are resolved and persisted in one bounded command.
        await using var command = new SqlCommand($"""
            DECLARE @Resolved TABLE(Line int PRIMARY KEY,MethodCode nvarchar(32),FormId uniqueidentifier,
              AccountId uniqueidentifier,Version int,Code nvarchar(64),Name nvarchar(160),BankId uniqueidentifier);
            INSERT @Resolved(Line,MethodCode,FormId)
            SELECT {target.Line??"1"},{target.Method},PaymentMethodId FROM {target.Schema}.{target.Table} WITH(UPDLOCK,HOLDLOCK)
            WHERE {target.Key}=@Document AND PaymentMethodId IS NOT NULL;
            -- Financing belongs to the existing sale header, not collected
            -- money. Line zero is an internal selector for that one header.
            IF @Usage=N'PosPaymentMethod'
              INSERT @Resolved(Line,MethodCode,FormId)
              SELECT 0,N'Credit',CreditPaymentMethodId FROM dbo.SalesDocuments WITH(UPDLOCK,HOLDLOCK)
              WHERE DocumentId=@Document AND BusinessId=@Business AND CreditPaymentMethodId IS NOT NULL;
            UPDATE requested SET AccountId=form.AccountId,Version=form.Version,Code=form.Code,Name=form.Name,
              BankId=CASE WHEN requested.MethodCode=N'Transfer' THEN account.BankAccountId END
            FROM @Resolved requested
            JOIN reference.PaymentMethods form WITH(HOLDLOCK) ON form.TenantId=@Tenant AND form.PaymentMethodId=requested.FormId
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
              AND type.IsActive=1 AND type.Code=requested.MethodCode
            JOIN dbo.AccountingAccounts account WITH(HOLDLOCK) ON account.TenantId=@Tenant AND account.AccountId=form.AccountId
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) readiness
            WHERE form.IsActive=1 AND readiness.SourceType=@Usage
              AND ((@Payroll=1 AND form.UseForPayroll=1) OR (@Payroll=0 AND ((@Usage=N'PosPaymentMethod' AND form.UseForSales=1)
                OR (@Usage=N'CustomerPaymentMethod' AND form.UseForReceipts=1)
                OR (@Usage=N'SupplierPaymentMethod' AND form.UseForSupplierPayments=1))));
            IF (SELECT COUNT(*) FROM @Resolved)<>@Expected OR EXISTS(SELECT 1 FROM @Resolved WHERE AccountId IS NULL)
            BEGIN SELECT CONVERT(bit,0); RETURN; END;
            UPDATE tender SET AccountId=resolved.AccountId,PaymentMethodVersion=resolved.Version,
              PaymentMethodCode=resolved.Code,PaymentMethodName=resolved.Name,BankAccountId=resolved.BankId
            FROM {target.Schema}.{target.Table} tender JOIN @Resolved resolved ON resolved.Line={ (target.Line is null?"1":$"tender.{target.Line}") }
            WHERE tender.{target.Key}=@Document;
            IF @Usage=N'PosPaymentMethod'
              UPDATE sale SET CreditAccountId=resolved.AccountId,CreditPaymentMethodVersion=resolved.Version,
                CreditPaymentMethodCode=resolved.Code,CreditPaymentMethodName=resolved.Name
              FROM dbo.SalesDocuments sale JOIN @Resolved resolved ON resolved.Line=0
              WHERE sale.DocumentId=@Document AND sale.BusinessId=@Business;
            UPDATE account SET ConfigurationLockedAt=@Now FROM dbo.AccountingAccounts account
            WHERE account.TenantId=@Tenant AND account.ConfigurationLockedAt IS NULL
              AND EXISTS(SELECT 1 FROM @Resolved resolved WHERE resolved.AccountId=account.AccountId);
            SELECT CONVERT(bit,1);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Document", source.DocumentId);
        command.Parameters.AddWithValue("@Tenant", source.TenantId);
        command.Parameters.AddWithValue("@Business", source.BusinessId);
        command.Parameters.AddWithValue("@Usage", target.Usage);
        command.Parameters.AddWithValue("@Payroll",source.DocumentType=="PayrollPayment");
        command.Parameters.AddWithValue("@Expected", configuredCount);
        command.Parameters.AddWithValue("@Now", processedAt);
        return (bool)(await command.ExecuteScalarAsync(token))! ? source : null;
    }

    private static async Task<SourceEnvelope?> ResolveRestaurantTipPaymentMethodAsync(SqlConnection connection,
        SqlTransaction transaction, SourceEnvelope source, DateTimeOffset processedAt, CancellationToken token)
    {
        var isSettlement = source.DocumentType == "RestaurantTipSettlement";
        var tip = !isSettlement ? JsonSerializer.Deserialize<RestaurantTipAccountingPayload>(source.PayloadJson) : null;
        var settlement = isSettlement ? JsonSerializer.Deserialize<RestaurantTipSettlementAccountingPayload>(source.PayloadJson) : null;
        var formId = isSettlement ? settlement?.PaymentMethodId : tip?.Payment.PaymentMethodId;
        if (formId is not { } id) return source;
        var methodCode = isSettlement ? settlement!.PaymentMethodCode : tip!.Payment.MethodCode;
        return await ResolveSinglePaymentMethodAsync(connection,transaction,source,id,methodCode,isSettlement,processedAt,token);
    }

    private static async Task<SourceEnvelope?> ResolveSinglePaymentMethodAsync(SqlConnection connection,
        SqlTransaction transaction,SourceEnvelope source,Guid id,string methodCode,bool isSupplierPayment,
        DateTimeOffset processedAt,CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @Resolved table(Version int,FormCode nvarchar(64),FormName nvarchar(160),
              AccountId uniqueidentifier PRIMARY KEY,AccountCode nvarchar(32),AccountName nvarchar(200),BankAccountId uniqueidentifier);
            INSERT @Resolved
            SELECT form.Version,form.Code,form.Name,account.AccountId,account.Code,account.Name,account.BankAccountId
            FROM reference.PaymentMethods form WITH(HOLDLOCK)
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId
              AND type.CatalogCode=N'payment-method' AND type.IsActive=1 AND type.Code=@Method
            JOIN dbo.AccountingAccounts account WITH(HOLDLOCK) ON account.TenantId=form.TenantId AND account.AccountId=form.AccountId
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) ready
            WHERE form.TenantId=@Tenant AND form.PaymentMethodId=@Id AND form.IsActive=1
              AND ((@Settlement=0 AND form.UseForSales=1 AND ready.SourceType=N'PosPaymentMethod')
                OR (@Settlement=1 AND form.UseForSupplierPayments=1 AND ready.SourceType=N'SupplierPaymentMethod'));
            UPDATE account SET ConfigurationLockedAt=@Now FROM dbo.AccountingAccounts account
            JOIN @Resolved resolved ON resolved.AccountId=account.AccountId
            WHERE account.TenantId=@Tenant AND account.ConfigurationLockedAt IS NULL;
            SELECT Version,FormCode,FormName,AccountId,AccountCode,AccountName,BankAccountId FROM @Resolved;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", source.TenantId);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Method", methodCode);
        command.Parameters.AddWithValue("@Settlement", isSupplierPayment);
        command.Parameters.AddWithValue("@Now", processedAt);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var applied = new VoucherAppliedPaymentMethod(source.DocumentId,id,methodCode,reader.GetInt32(0),
            reader.GetString(1),reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),
            methodCode == "Transfer" && !reader.IsDBNull(6) ? reader.GetGuid(6) : null);
        await reader.NextResultAsync(token);
        return source with { AppliedPaymentMethods = [applied] };
    }

    private static async Task<SourceEnvelope?> ResolveManualPaymentMethodsAsync(SqlConnection connection,
        SqlTransaction transaction, SourceEnvelope source, DateTimeOffset processedAt, CancellationToken token)
    {
        var request = JsonSerializer.Deserialize<ConfirmManualAccountingVoucherRequest>(source.PayloadJson)
            ?? throw new InvalidOperationException("The manual payment source is invalid.");
        var selected = request.Lines.Where(line => line.PaymentEvidence?.PaymentMethodId is not null).ToArray();
        if (selected.Length == 0) return source;
        if (request.PayloadVersion != 4 || request.FinancialEffects is null)
            throw new InvalidOperationException("Configured manual payments require their accepted financial effects.");
        // One read/update batch for the selected lines, including direct receipts
        // without a portfolio payment header. The immutable source is untouched.
        await using var command = new SqlCommand("""
            DECLARE @Resolved TABLE(SourceLineId uniqueidentifier PRIMARY KEY,FormId uniqueidentifier,MethodCode nvarchar(32),
              AccountId uniqueidentifier,Version int,Code nvarchar(64),Name nvarchar(160),BankId uniqueidentifier,
              AccountCode nvarchar(32),AccountName nvarchar(200));
            INSERT @Resolved(SourceLineId,FormId,MethodCode,AccountId,Version,Code,Name,BankId,AccountCode,AccountName)
            SELECT input.LineId,input.PaymentMethodId,input.MethodCode,account.AccountId,form.Version,form.Code,form.Name,
              CASE WHEN input.MethodCode=N'Transfer' THEN account.BankAccountId END,account.Code,account.Name
            FROM OPENJSON(@Selected) WITH(LineId uniqueidentifier,PaymentMethodId uniqueidentifier,MethodCode nvarchar(32),Payable bit) input
            JOIN reference.PaymentMethods form WITH(HOLDLOCK) ON form.TenantId=@Tenant AND form.PaymentMethodId=input.PaymentMethodId
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
              AND type.IsActive=1 AND type.Code=input.MethodCode
            JOIN dbo.AccountingAccounts account WITH(HOLDLOCK) ON account.TenantId=@Tenant AND account.AccountId=form.AccountId
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) readiness
            WHERE form.IsActive=1 AND ((@Usage=N'Receipts' AND form.UseForReceipts=1)
                OR (@Usage=N'SupplierPayments' AND form.UseForSupplierPayments=1)
                OR (@Usage=N'Vouchers' AND form.UseForVouchers=1))
              AND readiness.SourceType=CASE WHEN input.Payable=1 THEN N'SupplierPaymentMethod' ELSE N'CustomerPaymentMethod' END;
            IF (SELECT COUNT(*) FROM @Resolved)<>@Expected
            BEGIN SELECT CONVERT(bit,0); RETURN; END;
            DECLARE @Updated int;
            UPDATE tender SET AccountId=resolved.AccountId,PaymentMethodVersion=resolved.Version,
              PaymentMethodCode=resolved.Code,PaymentMethodName=resolved.Name,BankAccountId=resolved.BankId
            FROM dbo.CustomerPaymentTenders tender JOIN dbo.CustomerPayments payment ON payment.PaymentId=tender.PaymentId
            JOIN @Resolved resolved ON resolved.SourceLineId=tender.SourceLineId AND resolved.FormId=tender.PaymentMethodId
            WHERE payment.BusinessId=@Business AND payment.ParentSourceDocumentId=@Document
              AND payment.ParentSourceDocumentType=N'ManualAccountingVoucher';
            SET @Updated=@@ROWCOUNT;
            UPDATE tender SET AccountId=resolved.AccountId,PaymentMethodVersion=resolved.Version,
              PaymentMethodCode=resolved.Code,PaymentMethodName=resolved.Name,BankAccountId=resolved.BankId
            FROM dbo.SupplierPaymentTenders tender JOIN dbo.SupplierPayments payment ON payment.PaymentId=tender.PaymentId
            JOIN @Resolved resolved ON resolved.SourceLineId=tender.SourceLineId AND resolved.FormId=tender.PaymentMethodId
            WHERE payment.BusinessId=@Business AND payment.ParentSourceDocumentId=@Document
              AND payment.ParentSourceDocumentType=N'ManualAccountingVoucher';
            IF @Updated+@@ROWCOUNT<>@ExpectedGrouped
              THROW 51607,'The accepted payment form references do not match their parent source.',1;
            UPDATE account SET ConfigurationLockedAt=@Now FROM dbo.AccountingAccounts account
            WHERE account.TenantId=@Tenant AND account.ConfigurationLockedAt IS NULL
              AND EXISTS(SELECT 1 FROM @Resolved resolved WHERE resolved.AccountId=account.AccountId);
            SELECT CONVERT(bit,1);
            SELECT SourceLineId,FormId,MethodCode,Version,Code,Name,AccountId,AccountCode,AccountName,BankId
              FROM @Resolved ORDER BY SourceLineId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", source.TenantId);
        command.Parameters.AddWithValue("@Business", source.BusinessId);
        command.Parameters.AddWithValue("@Document", source.DocumentId);
        command.Parameters.AddWithValue("@Now", processedAt);
        command.Parameters.AddWithValue("@Expected", selected.Length);
        command.Parameters.AddWithValue("@ExpectedGrouped", request.FinancialEffects.Payments.SelectMany(group => group.Tenders)
            .Count(tender => tender.PaymentMethodId is not null));
        command.Parameters.AddWithValue("@Usage", request.VoucherTypeCode switch { "CashReceipt" => "Receipts", "Disbursement" => "SupplierPayments", _ => "Vouchers" });
        command.Parameters.Add("@Selected", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(selected.Select(line => new
        {
            line.LineId, line.PaymentEvidence!.PaymentMethodId, MethodCode = line.PaymentEvidence.PaymentMethodTypeCode, Payable = line.Credit > 0
        }));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Payment form resolution returned no result.");
        if (!reader.GetBoolean(0)) return null;
        var applied = new List<VoucherAppliedPaymentMethod>(selected.Length);
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
            applied.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetInt32(3),
                reader.GetString(4),reader.GetString(5),reader.GetGuid(6),reader.GetString(7),reader.GetString(8),
                reader.IsDBNull(9)?null:reader.GetGuid(9)));
        if (applied.Count != selected.Length) throw new InvalidOperationException("The applied payment form evidence is incomplete.");
        return source with { AppliedPaymentMethods = applied };
    }
}
