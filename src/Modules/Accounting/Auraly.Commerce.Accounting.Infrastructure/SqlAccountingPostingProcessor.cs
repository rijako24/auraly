using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Domain;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlAccountingCompletionObserver(
    SqlAccountingPostingProcessor processor) : IDocumentProcessingCompletionObserver
{
    public Task ObserveAsync(DocumentProcessingSignal signal, CancellationToken cancellationToken) =>
        processor.ProcessAsync(signal.DocumentId, signal.DocumentType, signal.BusinessId, cancellationToken);
}

public sealed class SqlAccountingPostingProcessor(
    AccountingSqlConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> SupportedTypes =
        ["SalesInvoice", "SalesReturn", "GoodsReceipt", "PurchaseReturn", "PayablePayment", "ReceivablePayment"];

    public async Task ProcessAsync(
        Guid documentId,
        string documentType,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        if (!SupportedTypes.Contains(documentType)) return;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var source = await LoadSourceEnvelopeAsync(
                connection, transaction, documentId, documentType, businessId,
                cancellationToken);
            if (source is null)
                throw new InvalidOperationException(
                    "The completed document has no immutable accounting source.");
            await EnsurePostingJobAsync(connection, transaction, source, cancellationToken);

            var status = await LockPostingStatusAsync(
                connection, transaction, source, cancellationToken);
            if (status == AccountingPostingStatuses.Posted)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var periodId = await FindOpenPeriodAsync(
                connection, transaction, source.TenantId,
                DateOnly.FromDateTime(source.OccurredAt.Date), cancellationToken);
            if (periodId is null)
            {
                await MarkPendingConfigurationAsync(connection, transaction, source,
                    "OpenPeriodMissing",
                    "No open accounting period contains the document date.",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var factsResult = source.DocumentType switch
            {
                "SalesInvoice" => FinancialFactsResult.Ready(
                    await LoadInvoiceFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "SalesReturn" => FinancialFactsResult.Ready(
                    await LoadReturnFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "GoodsReceipt" => await LoadGoodsReceiptFactsAsync(
                    connection, transaction, source, cancellationToken),
                "PurchaseReturn" => await LoadPurchaseReturnFactsAsync(
                    connection, transaction, source, cancellationToken),
                "PayablePayment" => FinancialFactsResult.Ready(
                    await LoadPayablePaymentFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "ReceivablePayment" => FinancialFactsResult.Ready(
                    await LoadReceivablePaymentFactsAsync(
                        connection, transaction, source, cancellationToken)),
                _ => throw new InvalidOperationException(
                    $"Document type '{source.DocumentType}' is not supported for accounting.")
            };
            if (factsResult.Facts is null)
            {
                await MarkPendingConfigurationAsync(
                    connection, transaction, source,
                    factsResult.ErrorCode!, factsResult.ErrorMessage!,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var facts = factsResult.Facts;
            var accountIds = await ResolveAccountsAsync(
                connection, transaction, source, facts.RequiredCategories,
                cancellationToken);
            var missing = facts.RequiredCategories
                .Where(category => !accountIds.ContainsKey(category))
                .OrderBy(category => category, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                await MarkPendingConfigurationAsync(connection, transaction, source,
                    "AccountMappingMissing",
                    $"Missing accounting mappings: {string.Join(", ", missing)}.",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var costCenterId = await FindDefaultCostCenterAsync(
                connection, transaction, source.BusinessId, cancellationToken);
            var lines = AccountingJournal.Validate(
                facts.BuildLines(accountIds, costCenterId));
            await InsertEntryAsync(
                connection, transaction, source, periodId.Value, facts.Description,
                lines, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<SourceEnvelope?> LoadSourceEnvelopeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid documentId,
        string documentType,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.TenantId,p.BusinessId,p.DocumentId,p.DocumentType,
                   p.PayloadHash,
                   CASE WHEN p.DocumentType=N'SalesInvoice' THEN s.IssuedAt
                        WHEN p.DocumentType=N'SalesReturn' THEN r.ReturnedAt
                        WHEN p.DocumentType=N'GoodsReceipt' THEN g.ReceivedAt
                        WHEN p.DocumentType=N'PurchaseReturn' THEN pr.ReturnedAt
                        WHEN p.DocumentType=N'PayablePayment' THEN pp.PaidAt
                        WHEN p.DocumentType=N'ReceivablePayment' THEN cp.PaidAt END
            FROM dbo.DocumentProcessingPayloads p WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j WITH (UPDLOCK,HOLDLOCK)
              ON j.DocumentId=p.DocumentId AND j.DocumentType=p.DocumentType
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            LEFT JOIN dbo.SalesDocuments s
              ON s.DocumentId=p.DocumentId AND p.DocumentType=N'SalesInvoice'
            LEFT JOIN dbo.SalesReturns r
              ON r.ReturnId=p.DocumentId AND p.DocumentType=N'SalesReturn'
            LEFT JOIN dbo.GoodsReceipts g
              ON g.GoodsReceiptId=p.DocumentId AND p.DocumentType=N'GoodsReceipt'
            LEFT JOIN dbo.PurchaseReturns pr
              ON pr.PurchaseReturnId=p.DocumentId AND p.DocumentType=N'PurchaseReturn'
            LEFT JOIN dbo.SupplierPayments pp
              ON pp.PaymentId=p.DocumentId AND p.DocumentType=N'PayablePayment'
            LEFT JOIN dbo.CustomerPayments cp
              ON cp.PaymentId=p.DocumentId AND p.DocumentType=N'ReceivablePayment'
            WHERE p.DocumentId=@DocumentId AND p.DocumentType=@DocumentType
              AND p.BusinessId=@BusinessId AND j.Status=N'Completed';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(5)) return null;
        return new SourceEnvelope(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetString(3), (byte[])reader[4], reader.GetDateTimeOffset(5));
    }

    private async Task EnsurePostingJobAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.AccountingPostingJobs WITH (UPDLOCK,HOLDLOCK)
                WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType
            )
            INSERT dbo.AccountingPostingJobs
            (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
             SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,
             CreatedAt)
            VALUES(@JobId,@TenantId,@BusinessId,@DocumentId,@DocumentType,
                   @PayloadHash,@OccurredAt,N'Pending',0,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@JobId", ids.NewId());
        AddSource(command, source);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> LockPostingStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT Status FROM dbo.AccountingPostingJobs WITH (UPDLOCK,HOLDLOCK)
            WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        return (string)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The accounting posting job was not persisted."));
    }

    private static async Task<Guid?> FindOpenPeriodAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        DateOnly occurredOn,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT PeriodId FROM dbo.AccountingPeriods WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND Status=N'Open'
              AND StartsOn<=@OccurredOn AND EndsOn>=@OccurredOn;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@OccurredOn", occurredOn.ToDateTime(TimeOnly.MinValue));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private static async Task<Guid?> FindDefaultCostCenterAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT CostCenterId FROM dbo.AccountingCostCenters
            WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private static async Task<FinancialFacts> LoadInvoiceFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        Guid? partyId;
        decimal untaxed;
        decimal tax;
        decimal total;
        string number;
        await using (var command = new SqlCommand("""
            SELECT s.DocumentNumber,s.UntaxedAmount,s.TaxAmount,s.PayableAmount,c.PartyId
            FROM dbo.SalesDocuments s
            LEFT JOIN dbo.Customers c ON c.CustomerId=s.CustomerId
            WHERE s.DocumentId=@DocumentId AND s.BusinessId=@BusinessId;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The sale was not found for accounting.");
            number = reader.GetString(0); untaxed = reader.GetDecimal(1); tax = reader.GetDecimal(2); total = reader.GetDecimal(3);
            partyId = reader.IsDBNull(4) ? null : reader.GetGuid(4);
        }
        var payments = new List<(string Category, decimal Amount)>();
        await using (var command = new SqlCommand("""
            SELECT MethodCode,SUM(Amount) FROM dbo.SalesPayments
            WHERE DocumentId=@DocumentId GROUP BY MethodCode ORDER BY MethodCode;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                payments.Add((PaymentCategory(reader.GetString(0)), reader.GetDecimal(1)));
        }
        var paid = payments.Sum(payment => payment.Amount);
        if (paid > total) throw new InvalidOperationException("Payments exceed the immutable invoice total.");
        if (paid < total) payments.Add((AccountingCategories.AccountsReceivable, total - paid));
        var cost = await InventoryCostAsync(connection, transaction, source.DocumentId, source.DocumentType, cancellationToken);
        return FinancialFacts.Invoice(number, partyId, untaxed, tax, total, cost, payments);
    }

    private static async Task<FinancialFacts> LoadReturnFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        string number; decimal untaxed; decimal tax; decimal total;
        string settlementCategory; Guid? partyId;
        await using (var command = new SqlCommand("""
                SELECT r.DocumentNumber,r.UntaxedAmount,r.TaxAmount,r.TotalAmount,
                       r.EconomicResolution,r.RefundMethodCode,c.PartyId
                FROM dbo.SalesReturns r
                LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
                WHERE r.ReturnId=@DocumentId AND r.BusinessId=@BusinessId;
                """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The sales return was not found for accounting.");
            number = reader.GetString(0); untaxed = reader.GetDecimal(1); tax = reader.GetDecimal(2); total = reader.GetDecimal(3);
            settlementCategory = reader.GetString(4) == "CustomerCredit" ? AccountingCategories.CustomerCreditsPayable : PaymentCategory(reader.GetString(5));
            partyId = reader.IsDBNull(6) ? null : reader.GetGuid(6);
        }
        var cost = await InventoryCostAsync(connection, transaction, source.DocumentId, source.DocumentType, cancellationToken);
        return FinancialFacts.Return(number, partyId, untaxed, tax, total, cost, settlementCategory);
    }

    private static async Task<FinancialFactsResult> LoadGoodsReceiptFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        string number;
        string currencyCode;
        decimal total;
        bool createsPayable;
        await using (var command = new SqlCommand("""
            SELECT DocumentNumber,CurrencyCode,GrandTotal,CreatesPayable
            FROM dbo.GoodsReceipts
            WHERE GoodsReceiptId=@DocumentId AND BusinessId=@BusinessId;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The goods receipt was not found for accounting.");
            number = reader.GetString(0);
            currencyCode = reader.GetString(1);
            total = reader.GetDecimal(2);
            createsPayable = reader.GetBoolean(3);
        }

        if (!createsPayable)
            return FinancialFactsResult.Pending(
                "SettlementSourceMissing",
                "The goods receipt has no payable and no settlement evidence to credit.");
        if (!string.Equals(currencyCode, "COP", StringComparison.Ordinal))
            return FinancialFactsResult.Pending(
                "ForeignCurrencyUnsupported",
                $"Currency '{currencyCode}' requires an exchange-rate accounting policy.");

        decimal deductibleVat;
        decimal acquisitionAmount;
        await using (var command = new SqlCommand("""
            SELECT
              COALESCE(SUM(CASE WHEN TaxTreatment=N'DeductibleInputVat'
                                THEN TaxAmount ELSE 0 END),0),
              COALESCE(SUM(NetAmount +
                    CASE WHEN TaxTreatment=N'CapitalizedCost'
                         THEN TaxAmount ELSE 0 END),0)
            FROM dbo.GoodsReceiptLines
            WHERE GoodsReceiptId=@DocumentId;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The goods receipt lines were not found for accounting.");
            deductibleVat = reader.GetDecimal(0);
            acquisitionAmount = reader.GetDecimal(1);
        }

        var inventory = await InventoryCostAsync(
            connection, transaction, source.DocumentId, source.DocumentType,
            cancellationToken);
        var expense = decimal.Round(
            acquisitionAmount - inventory, 4, MidpointRounding.AwayFromZero);
        var accountedTotal = decimal.Round(
            inventory + expense + deductibleVat, 4,
            MidpointRounding.AwayFromZero);
        if (expense < 0 || accountedTotal != decimal.Round(total, 4))
            throw new InvalidOperationException(
                "The immutable goods receipt does not reconcile with its inventory, expense and tax effects.");

        return FinancialFactsResult.Ready(FinancialFacts.Purchase(
            number, inventory, expense, deductibleVat, total));
    }

    private static async Task<FinancialFactsResult> LoadPurchaseReturnFactsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        string number; string currency; decimal total; decimal payableCredit;
        decimal supplierCredit;
        await using (var command = new SqlCommand("""
            SELECT r.DocumentNumber,r.CurrencyCode,r.TotalAmount,
                   e.PayableCreditAmount,e.SupplierCreditAmount
            FROM dbo.PurchaseReturns r
            INNER JOIN dbo.PurchaseReturnFinancialEffects e
              ON e.PurchaseReturnId=r.PurchaseReturnId
            WHERE r.PurchaseReturnId=@DocumentId AND r.BusinessId=@BusinessId;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The purchase return financial effects were not found for accounting.");
            number=reader.GetString(0); currency=reader.GetString(1);
            total=reader.GetDecimal(2); payableCredit=reader.GetDecimal(3);
            supplierCredit=reader.GetDecimal(4);
        }
        if (currency != "COP")
            return FinancialFactsResult.Pending("ForeignCurrencyUnsupported",
                $"Currency '{currency}' requires an exchange-rate accounting policy.");
        decimal deductibleVat; decimal acquisitionAmount;
        await using (var command = new SqlCommand("""
            SELECT COALESCE(SUM(CASE WHEN TaxTreatment=N'DeductibleInputVat'
                       THEN TaxAmount ELSE 0 END),0),
                   COALESCE(SUM(NetAmount+CASE WHEN TaxTreatment=N'CapitalizedCost'
                       THEN TaxAmount ELSE 0 END),0)
            FROM dbo.PurchaseReturnLines WHERE PurchaseReturnId=@DocumentId;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            deductibleVat=reader.GetDecimal(0); acquisitionAmount=reader.GetDecimal(1);
        }
        var inventory=decimal.Abs(await InventoryCostAsync(connection,transaction,
            source.DocumentId,source.DocumentType,cancellationToken));
        var expense=decimal.Round(acquisitionAmount-inventory,4,
            MidpointRounding.AwayFromZero);
        if(expense<0 || decimal.Round(inventory+expense+deductibleVat,4)!=total ||
           decimal.Round(payableCredit+supplierCredit,4)!=total)
            throw new InvalidOperationException(
                "The purchase return does not reconcile with its inventory, tax and financial effects.");
        var settlements=new List<(string Category,decimal Amount)>();
        if(payableCredit>0)settlements.Add((AccountingCategories.AccountsPayable,payableCredit));
        if(supplierCredit>0)settlements.Add((AccountingCategories.SupplierCreditsReceivable,supplierCredit));
        return FinancialFactsResult.Ready(FinancialFacts.PurchaseReturn(
            number,inventory,expense,deductibleVat,total,settlements));
    }
    private static async Task<FinancialFacts> LoadPayablePaymentFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT DocumentNumber,CurrencyCode,TotalAmount,PaymentMethod
            FROM dbo.SupplierPayments
            WHERE PaymentId=@DocumentId AND BusinessId=@BusinessId AND Status=N'Processed';
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "The processed supplier payment was not found for accounting.");
        var number = reader.GetString(0);
        var currency = reader.GetString(1);
        var amount = reader.GetDecimal(2);
        var method = reader.GetString(3);
        if (!string.Equals(currency, "COP", StringComparison.Ordinal))
            throw new InvalidOperationException("Supplier payment accounting currently requires COP.");
        var settlement = method switch
        {
            "Cash" => AccountingCategories.Cash,
            "BankTransfer" => AccountingCategories.Bank,
            _ => throw new InvalidOperationException(
                $"Supplier payment method '{method}' has no accounting category.")
        };
        return FinancialFacts.PayablePayment(number, amount, settlement);
    }

    private static async Task<FinancialFacts> LoadReceivablePaymentFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT c.PartyId,p.DocumentNumber,p.CurrencyCode,p.TotalAmount,p.PaymentMethod
            FROM dbo.CustomerPayments p
            INNER JOIN dbo.Customers c ON c.CustomerId=p.CustomerId
            WHERE p.PaymentId=@DocumentId AND p.BusinessId=@BusinessId AND p.Status=N'Processed';
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "The processed customer receipt was not found for accounting.");
        var partyId = reader.GetGuid(0);
        var number = reader.GetString(1);
        if (reader.GetString(2) != "COP")
            throw new InvalidOperationException("Customer receipt accounting currently requires COP.");
        var amount = reader.GetDecimal(3);
        var settlement = reader.GetString(4) switch
        {
            "Cash" => AccountingCategories.Cash,
            "BankTransfer" => AccountingCategories.Bank,
            "DebitCard" => AccountingCategories.DebitCardClearing,
            "CreditCard" => AccountingCategories.CreditCardClearing,
            var method => throw new InvalidOperationException($"Customer receipt method '{method}' has no accounting category.")
        };
        return FinancialFacts.ReceivablePayment(number, partyId, amount, settlement);
    }

    private static async Task<decimal> InventoryCostAsync(
        SqlConnection connection, SqlTransaction transaction, Guid documentId,
        string documentType, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COALESCE(ABS(SUM(ValueChange)),0) FROM dbo.InventoryMovements
            WHERE DocumentId=@DocumentId AND DocumentType=@DocumentType;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        return Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string PaymentCategory(string methodCode) =>
        methodCode.ToUpperInvariant() switch
        {
            "CASH" => AccountingCategories.Cash,
            "DEBITCARD" => AccountingCategories.DebitCardClearing,
            "CREDITCARD" => AccountingCategories.CreditCardClearing,
            "TRANSFER" => AccountingCategories.TransferClearing,
            _ => throw new InvalidOperationException($"Payment method '{methodCode}' has no accounting category.")
        };

    private static async Task<Dictionary<string, Guid>> ResolveAccountsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        IReadOnlySet<string> categories, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var occurredOn = DateOnly.FromDateTime(source.OccurredAt.Date).ToDateTime(TimeOnly.MinValue);
        foreach (var category in categories)
        {
            await using var command = new SqlCommand("""
                SELECT TOP(1) m.AccountId
                FROM dbo.AccountingAccountMappings m
                INNER JOIN dbo.AccountingAccounts a ON a.AccountId=m.AccountId
                WHERE m.TenantId=@TenantId AND m.Category=@Category
                  AND (m.BusinessId=@BusinessId OR m.BusinessId IS NULL)
                  AND m.EffectiveFrom<=@OccurredOn
                  AND (m.EffectiveTo IS NULL OR m.EffectiveTo>=@OccurredOn)
                  AND a.IsActive=1 AND a.AllowsPosting=1
                ORDER BY CASE WHEN m.BusinessId=@BusinessId THEN 0 ELSE 1 END,
                         m.EffectiveFrom DESC;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", source.TenantId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            command.Parameters.AddWithValue("@Category", category);
            command.Parameters.AddWithValue("@OccurredOn", occurredOn);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is Guid id) result[category] = id;
        }
        return result;
    }

    private async Task InsertEntryAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        Guid periodId, string description, IReadOnlyList<JournalLine> lines,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var number = await NextVoucherNumberAsync(connection, transaction, source.TenantId, now, cancellationToken);
        var entryId = ids.NewId();
        var debit = decimal.Round(lines.Sum(line => line.Debit), 4);
        await using (var command = new SqlCommand("""
            INSERT dbo.AccountingEntries
            (EntryId,TenantId,BusinessId,PeriodId,SourceDocumentId,SourceDocumentType,
             EntryNumber,OccurredAt,PostedAt,Description,DebitTotal,CreditTotal,
             SourcePayloadHash,RuleVersion)
            VALUES(@EntryId,@TenantId,@BusinessId,@PeriodId,@DocumentId,@DocumentType,
                   @Number,@OccurredAt,@PostedAt,@Description,@Total,@Total,@PayloadHash,1);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@EntryId", entryId); command.Parameters.AddWithValue("@PeriodId", periodId);
            AddSource(command, source); command.Parameters.AddWithValue("@Number", number); command.Parameters.AddWithValue("@PostedAt", now);
            command.Parameters.AddWithValue("@Description", description); AddMoney(command, "@Total", debit);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            await using var command = new SqlCommand("""
                INSERT dbo.AccountingEntryLines
                (EntryId,LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit)
                VALUES(@EntryId,@LineNumber,@AccountId,@PartyId,@CostCenterId,@Description,@Debit,@Credit);
                """, connection, transaction);
            command.Parameters.AddWithValue("@EntryId", entryId); command.Parameters.AddWithValue("@LineNumber", index + 1);
            command.Parameters.AddWithValue("@AccountId", line.AccountId); command.Parameters.AddWithValue("@PartyId", (object?)line.PartyId ?? DBNull.Value);
            command.Parameters.AddWithValue("@CostCenterId", (object?)line.CostCenterId ?? DBNull.Value); command.Parameters.AddWithValue("@Description", line.Description);
            AddMoney(command, "@Debit", line.Debit); AddMoney(command, "@Credit", line.Credit);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var complete = new SqlCommand("""
            UPDATE dbo.AccountingPostingJobs SET Status=N'Posted',AttemptCount=AttemptCount+1,
                LastAttemptAt=@Now,CompletedAt=@Now,LastErrorCode=NULL,LastErrorMessage=NULL
            WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType;
            """, connection, transaction);
        complete.Parameters.AddWithValue("@Now", now); complete.Parameters.AddWithValue("@DocumentId", source.DocumentId); complete.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        await complete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> NextVoucherNumberAsync(SqlConnection connection, SqlTransaction transaction, Guid tenantId, DateTimeOffset now, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingVoucherCursors WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId)
              INSERT dbo.AccountingVoucherCursors(TenantId,LastAssignedNumber,UpdatedAt) VALUES(@TenantId,0,@Now);
            UPDATE dbo.AccountingVoucherCursors SET LastAssignedNumber=LastAssignedNumber+1,UpdatedAt=@Now
              OUTPUT inserted.LastAssignedNumber WHERE TenantId=@TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId); command.Parameters.AddWithValue("@Now", now);
        var value = Convert.ToInt64(await command.ExecuteScalarAsync(token));
        return $"ASI-{value:D10}";
    }

    private static async Task MarkPendingConfigurationAsync(SqlConnection connection, SqlTransaction transaction, SourceEnvelope source, string code, string message, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.AccountingPostingJobs
            SET Status=N'AccountingPendingConfiguration',AttemptCount=AttemptCount+1,
                LastAttemptAt=SYSDATETIMEOFFSET(),LastErrorCode=@Code,LastErrorMessage=@Message
            WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Code", code); command.Parameters.AddWithValue("@Message", message);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId); command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        await command.ExecuteNonQueryAsync(token);
    }

    private static void AddSource(SqlCommand command, SourceEnvelope source)
    {
        command.Parameters.AddWithValue("@TenantId", source.TenantId); command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId); command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        command.Parameters.AddWithValue("@PayloadHash", source.PayloadHash); command.Parameters.AddWithValue("@OccurredAt", source.OccurredAt);
    }
    private static void AddMoney(SqlCommand command, string name, decimal value) { var parameter = command.Parameters.Add(name, SqlDbType.Decimal); parameter.Precision = 19; parameter.Scale = 4; parameter.Value = value; }
    private sealed record SourceEnvelope(Guid TenantId, Guid BusinessId, Guid DocumentId, string DocumentType, byte[] PayloadHash, DateTimeOffset OccurredAt);

    private sealed record FinancialFactsResult(
        FinancialFacts? Facts,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static FinancialFactsResult Ready(FinancialFacts facts) =>
            new(facts, null, null);

        public static FinancialFactsResult Pending(string code, string message) =>
            new(null, code, message);
    }

    private sealed record FinancialFacts(string Description, Guid? PartyId, decimal Untaxed, decimal Tax, decimal Total, decimal Cost, IReadOnlyList<(string Category, decimal Amount)> Settlements, bool IsReturn, bool IsPurchase, bool IsPayablePayment, bool IsReceivablePayment)
    {
        public IReadOnlySet<string> RequiredCategories
        {
            get
            {
                var values = new HashSet<string>(Settlements.Select(item => item.Category), StringComparer.Ordinal);
                if (IsPayablePayment)
                {
                    values.Add(AccountingCategories.AccountsPayable);
                    return values;
                }
                if (IsReceivablePayment)
                {
                    values.Add(AccountingCategories.AccountsReceivable);
                    return values;
                }
                if (IsPurchase)
                {
                    if (Cost > 0) values.Add(AccountingCategories.Inventory);
                    if (Untaxed > 0) values.Add(AccountingCategories.PurchasesExpense);
                    if (Tax > 0) values.Add(AccountingCategories.InputVat);
                    return values;
                }
                values.Add(IsReturn ? AccountingCategories.SalesReturns : AccountingCategories.SalesRevenue);
                if (Tax > 0) values.Add(AccountingCategories.OutputVat);
                if (Cost > 0) { values.Add(AccountingCategories.Inventory); values.Add(AccountingCategories.CostOfGoodsSold); }
                return values;
            }
        }
        public IEnumerable<JournalLine> BuildLines(IReadOnlyDictionary<string, Guid> accounts, Guid? costCenter)
        {
            if (IsPayablePayment)
            {
                yield return new(accounts[AccountingCategories.AccountsPayable], Total, 0, PartyId, costCenter, Description);
                foreach (var settlement in Settlements)
                    yield return new(accounts[settlement.Category], 0, settlement.Amount, PartyId, costCenter, Description);
                yield break;
            }
            if (IsReceivablePayment)
            {
                foreach (var settlement in Settlements)
                    yield return new(accounts[settlement.Category], settlement.Amount, 0, PartyId, costCenter, Description);
                yield return new(accounts[AccountingCategories.AccountsReceivable], 0, Total, PartyId, costCenter, Description);
                yield break;
            }

            if (IsPurchase)
            {
                if (IsReturn)
                {
                    foreach (var settlement in Settlements)
                        yield return new(accounts[settlement.Category], settlement.Amount, 0, PartyId, costCenter, Description);
                    if (Cost > 0) yield return new(accounts[AccountingCategories.Inventory], 0, Cost, PartyId, costCenter, Description);
                    if (Untaxed > 0) yield return new(accounts[AccountingCategories.PurchasesExpense], 0, Untaxed, PartyId, costCenter, Description);
                    if (Tax > 0) yield return new(accounts[AccountingCategories.InputVat], 0, Tax, PartyId, costCenter, Description);
                }
                else
                {
                    if (Cost > 0) yield return new(accounts[AccountingCategories.Inventory], Cost, 0, PartyId, costCenter, Description);
                    if (Untaxed > 0) yield return new(accounts[AccountingCategories.PurchasesExpense], Untaxed, 0, PartyId, costCenter, Description);
                    if (Tax > 0) yield return new(accounts[AccountingCategories.InputVat], Tax, 0, PartyId, costCenter, Description);
                    foreach (var settlement in Settlements)
                        yield return new(accounts[settlement.Category], 0, settlement.Amount, PartyId, costCenter, Description);
                }
                yield break;
            }
            if (!IsReturn)
            {
                foreach (var settlement in Settlements) yield return new(accounts[settlement.Category], settlement.Amount, 0, PartyId, costCenter, Description);
                yield return new(accounts[AccountingCategories.SalesRevenue], 0, Untaxed, PartyId, costCenter, Description);
                if (Tax > 0) yield return new(accounts[AccountingCategories.OutputVat], 0, Tax, PartyId, costCenter, Description);
                if (Cost > 0) { yield return new(accounts[AccountingCategories.CostOfGoodsSold], Cost, 0, PartyId, costCenter, Description); yield return new(accounts[AccountingCategories.Inventory], 0, Cost, PartyId, costCenter, Description); }
            }
            else
            {
                yield return new(accounts[AccountingCategories.SalesReturns], Untaxed, 0, PartyId, costCenter, Description);
                if (Tax > 0) yield return new(accounts[AccountingCategories.OutputVat], Tax, 0, PartyId, costCenter, Description);
                foreach (var settlement in Settlements) yield return new(accounts[settlement.Category], 0, settlement.Amount, PartyId, costCenter, Description);
                if (Cost > 0) { yield return new(accounts[AccountingCategories.Inventory], Cost, 0, PartyId, costCenter, Description); yield return new(accounts[AccountingCategories.CostOfGoodsSold], 0, Cost, PartyId, costCenter, Description); }
            }
        }
        public static FinancialFacts Invoice(string number, Guid? party, decimal untaxed, decimal tax, decimal total, decimal cost, IReadOnlyList<(string Category, decimal Amount)> settlements) => new($"Factura de venta {number}", party, untaxed, tax, total, cost, settlements, false, false, false, false);
        public static FinancialFacts Return(string number, Guid? party, decimal untaxed, decimal tax, decimal total, decimal cost, string settlement) => new($"Devolucion de venta {number}", party, untaxed, tax, total, cost, [(settlement, total)], true, false, false, false);
        public static FinancialFacts Purchase(string number, decimal inventory, decimal expense, decimal deductibleVat, decimal total) => new($"Entrada de mercancia {number}", null, expense, deductibleVat, total, inventory, [(AccountingCategories.AccountsPayable, total)], false, true, false, false);
        public static FinancialFacts PurchaseReturn(string number, decimal inventory, decimal expense, decimal deductibleVat, decimal total, IReadOnlyList<(string Category, decimal Amount)> settlements) => new($"Devolucion de compra {number}", null, expense, deductibleVat, total, inventory, settlements, true, true, false, false);
        public static FinancialFacts PayablePayment(string number, decimal total, string settlement) => new($"Pago a proveedor {number}", null, 0, 0, total, 0, [(settlement, total)], false, false, true, false);
        public static FinancialFacts ReceivablePayment(string number, Guid partyId, decimal total, string settlement) => new($"Recaudo de cartera {number}", partyId, 0, 0, total, 0, [(settlement, total)], false, false, false, true);
    }
}
