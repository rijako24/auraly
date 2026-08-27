using System.Data;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Domain;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor(
    AccountingSqlConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    public async Task ProcessAsync(
        Guid documentId,
        string documentType,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        if (!AccountingProcessingPolicy.Supports(documentType)) return;
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

            transaction.Save("BeforeFinancialEffects");
            await ApplyFinancialEffectsAsync(
                connection, transaction, source, cancellationToken);

            var factsResult = source.DocumentType switch
            {
                "SalesInvoice" => FinancialFactsResult.Ready(
                    await LoadInvoiceFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "SalesReceipt" => FinancialFactsResult.Ready(
                    await LoadInvoiceFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "SalesReturn" => FinancialFactsResult.Ready(
                    await LoadReturnFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "SalesDebitNote" => FinancialFactsResult.Ready(
                    await LoadDebitNoteFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "GoodsReceipt" => await LoadGoodsReceiptFactsAsync(
                    connection, transaction, source, cancellationToken),
                "Expense" => await LoadExpenseFactsAsync(
                    connection, transaction, source, cancellationToken),
                "PurchaseReturn" => await LoadPurchaseReturnFactsAsync(
                    connection, transaction, source, cancellationToken),
                "PayablePayment" => FinancialFactsResult.Ready(
                    await LoadPayablePaymentFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "ReceivablePayment" => FinancialFactsResult.Ready(
                    await LoadReceivablePaymentFactsAsync(
                        connection, transaction, source, cancellationToken)),
                "CashReceipt" or "CashDisbursement" =>
                    await LoadCashMovementFactsAsync(
                        connection, transaction, source, cancellationToken),
                AccountingManualDocumentTypes.AccountAdjustment =>
                    await LoadAccountAdjustmentFactsAsync(
                        connection, transaction, source, cancellationToken),
                AccountingManualDocumentTypes.ManualVoucher =>
                    FinancialFactsResult.Ready(LoadManualVoucherFacts(source)),
                AccountingManualDocumentTypes.OpeningBalance =>
                    FinancialFactsResult.Ready(LoadManualVoucherFacts(source)),
                WorkSessionAccountingDocumentTypes.CashDifference =>
                    FinancialFactsResult.Ready(LoadWorkSessionCashDifferenceFacts(source)),
                PayrollAccountingDocumentTypes.Accrual or
                PayrollAccountingDocumentTypes.Payment or
                PayrollAccountingDocumentTypes.Adjustment =>
                    FinancialFactsResult.Ready(LoadPayrollFacts(source)),
                _ => throw new InvalidOperationException(
                    $"Document type '{source.DocumentType}' is not supported for accounting.")
            };
            if (factsResult.Facts is null)
            {
                transaction.Rollback("BeforeFinancialEffects");
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
                transaction.Rollback("BeforeFinancialEffects");
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
            if (source.DocumentType == AccountingManualDocumentTypes.OpeningBalance)
                await CompleteOpeningActivationAsync(
                    connection, transaction, source, cancellationToken);
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
            SELECT a.TenantId,a.BusinessId,a.SourceDocumentId,
                   a.SourceDocumentType,a.SourcePayloadHash,a.OccurredAt,s.PayloadJson
            FROM dbo.AccountingPostingJobs a WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.AccountingSourceDocuments s
              ON s.SourceDocumentId=a.SourceDocumentId
             AND s.SourceDocumentType=a.SourceDocumentType
             AND s.BusinessId=a.BusinessId
             AND s.TenantId=a.TenantId
             AND s.PayloadHash=a.SourcePayloadHash
            WHERE a.SourceDocumentId=@DocumentId
              AND a.SourceDocumentType=@DocumentType
              AND a.BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(5)) return null;
        return new SourceEnvelope(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetString(3), (byte[])reader[4], reader.GetDateTimeOffset(5),
            reader.GetString(6));
    }

    private static FinancialFacts LoadWorkSessionCashDifferenceFacts(
        SourceEnvelope source)
    {
        var payload = JsonSerializer.Deserialize<WorkSessionCashDifferencePayload>(
            source.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException(
                "The work-session cash-difference payload is invalid.");
        if (payload.WorkSessionClosureId != source.DocumentId ||
            payload.BusinessId != source.BusinessId || payload.TenantId != source.TenantId ||
            payload.Difference == 0 ||
            decimal.Round(payload.CountedCash - payload.ExpectedCash, 4) !=
            decimal.Round(payload.Difference, 4))
            throw new InvalidOperationException(
                "The work-session cash-difference payload is inconsistent.");
        var surplus = payload.Difference > 0;
        return FinancialFacts.CashDifference(
            payload.WorkSessionClosureId,
            payload.UserName,
            surplus,
            Math.Abs(payload.Difference));
    }

    private static FinancialFacts LoadPayrollFacts(SourceEnvelope source)
    {
        var payload = PayrollContractSerializer.DeserializeAccounting(source.PayloadJson);
        if (payload.PayrollRunId != source.DocumentId ||
            payload.BusinessId != source.BusinessId || payload.TenantId != source.TenantId ||
            payload.Lines.Count == 0 || payload.Lines.Any(line =>
                string.IsNullOrWhiteSpace(line.Category) || line.Category.Length > 64 ||
                line.Debit < 0 || line.Credit < 0 ||
                (line.Debit == 0) == (line.Credit == 0)) ||
            decimal.Round(payload.Lines.Sum(line => line.Debit), 4) !=
            decimal.Round(payload.Lines.Sum(line => line.Credit), 4))
            throw new InvalidOperationException("The payroll accounting payload is inconsistent.");

        return FinancialFacts.Payroll(payload.Description,
            payload.Lines.Select(line => new CategoryLineSpec(
                line.Category, line.Debit, line.Credit, line.PartyId,
                line.Description)).ToArray());
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
        var paymentSources = new List<(string MethodCode, decimal Amount)>();
        await using (var command = new SqlCommand("""
            SELECT MethodCode,SUM(Amount) FROM dbo.SalesPayments
            WHERE DocumentId=@DocumentId GROUP BY MethodCode ORDER BY MethodCode;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                paymentSources.Add((reader.GetString(0), reader.GetDecimal(1)));
        }
        var payments = new List<(string Category, decimal Amount)>();
        foreach (var payment in paymentSources)
            payments.Add((await ResolveSourceCategoryAsync(connection, transaction,
                "PosPaymentMethod", payment.MethodCode, cancellationToken), payment.Amount));
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
        string resolution; string? refundMethod; Guid? partyId;
        decimal receivableApplication; decimal customerCredit;
        await using (var command = new SqlCommand("""
                SELECT r.DocumentNumber,r.UntaxedAmount,r.TaxAmount,r.TotalAmount,
                       r.EconomicResolution,r.RefundMethodCode,c.PartyId,
                       COALESCE((SELECT SUM(a.Amount)
                         FROM dbo.SalesReturnReceivableApplications a
                         WHERE a.ReturnId=r.ReturnId),0),
                       COALESCE((SELECT SUM(cc.OriginalAmount)
                         FROM dbo.CustomerCredits cc
                         WHERE cc.SourceReturnId=r.ReturnId),0)
                FROM dbo.SalesReturns r
                LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
                WHERE r.ReturnId=@DocumentId AND r.BusinessId=@BusinessId;
                """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The sales return was not found for accounting.");
            number=reader.GetString(0); untaxed=reader.GetDecimal(1); tax=reader.GetDecimal(2);
            total=reader.GetDecimal(3); resolution=reader.GetString(4);
            refundMethod=reader.IsDBNull(5)?null:reader.GetString(5);
            partyId=reader.IsDBNull(6)?null:reader.GetGuid(6);
            receivableApplication=reader.GetDecimal(7); customerCredit=reader.GetDecimal(8);
        }
        var settlements = new List<(string Category, decimal Amount)>();
        if (resolution == "Refund")
            settlements.Add((await ResolveSourceCategoryAsync(connection, transaction,
                "PosPaymentMethod", refundMethod!, cancellationToken), total));
        else
        {
            if (receivableApplication > 0)
                settlements.Add((AccountingCategories.AccountsReceivable, receivableApplication));
            if (customerCredit > 0)
                settlements.Add((AccountingCategories.CustomerCreditsPayable, customerCredit));
        }
        if (decimal.Round(settlements.Sum(item => item.Amount),4) != decimal.Round(total,4))
            throw new InvalidOperationException("The sales return settlement does not reconcile with its total.");
        var cost = await InventoryCostAsync(connection, transaction, source.DocumentId, source.DocumentType, cancellationToken);
        return FinancialFacts.Return(number, partyId, untaxed, tax, total, cost, settlements);
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
        Guid partyId;
        await using (var command = new SqlCommand("""
            SELECT r.DocumentNumber,r.CurrencyCode,r.GrandTotal,r.CreatesPayable,s.PartyId
            FROM dbo.GoodsReceipts r
            INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
            WHERE r.GoodsReceiptId=@DocumentId AND r.BusinessId=@BusinessId;
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
            partyId = reader.GetGuid(4);
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

        var settlements = await LoadPurchaseWithholdingSettlementsAsync(
            connection, transaction, source, total, cancellationToken);
        return FinancialFactsResult.Ready(FinancialFacts.Purchase(
            number, partyId, inventory, expense, deductibleVat, total, settlements));
    }

    private static async Task<IReadOnlyList<(string Category, decimal Amount)>>
        LoadPurchaseWithholdingSettlementsAsync(
            SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
            decimal grossTotal, CancellationToken cancellationToken)
    {
        decimal? netAmount;
        await using (var command = new SqlCommand("""
            SELECT NetAmount FROM dbo.DocumentWithholdingSnapshots
            WHERE DocumentId=@DocumentId AND DocumentType=@DocumentType
              AND BusinessId=@BusinessId;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
            command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            netAmount = value is null or DBNull ? null : Convert.ToDecimal(value);
        }
        if (netAmount is null)
            return [(AccountingCategories.AccountsPayable, grossTotal)];

        var settlements = new List<(string Category, decimal Amount)>();
        var withholdingSources = new List<(string Kind, decimal Amount)>();
        if (netAmount.Value > 0)
            settlements.Add((AccountingCategories.AccountsPayable, netAmount.Value));
        await using (var command = new SqlCommand("""
            SELECT Kind,SUM(Amount) FROM dbo.DocumentWithholdingLines
            WHERE DocumentId=@DocumentId AND DocumentType=@DocumentType
            GROUP BY Kind ORDER BY Kind;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
            command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                withholdingSources.Add((reader.GetString(0), reader.GetDecimal(1)));
        }
        foreach (var withholding in withholdingSources)
            settlements.Add((await ResolveSourceCategoryAsync(connection, transaction,
                "PurchaseWithholdingKind", withholding.Kind, cancellationToken), withholding.Amount));
        if (decimal.Round(settlements.Sum(item => item.Amount), 4) != decimal.Round(grossTotal, 4))
            throw new InvalidOperationException("The payable and withholding settlements do not reconcile.");
        return settlements;
    }

    private static async Task<FinancialFactsResult> LoadExpenseFactsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT e.DocumentNumber,e.TaxExclusiveAmount,e.VatAmount,e.GrossAmount,
                   e.ExpenseAccountId,e.CostCenterId,s.PartyId
            FROM
            (
              SELECT x.DocumentNumber,x.TaxExclusiveAmount,x.VatAmount,x.GrossAmount,
                     c.ExpenseAccountId,x.CostCenterId,x.SupplierId,x.BusinessId,x.ExpenseId
              FROM dbo.Expenses x
              JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=x.ExpenseConceptId
            ) e
            JOIN dbo.Suppliers s ON s.SupplierId=e.SupplierId
            WHERE e.ExpenseId=@DocumentId AND e.BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The expense was not found for accounting.");
        var number=reader.GetString(0);var untaxed=reader.GetDecimal(1);var vat=reader.GetDecimal(2);
        var total=reader.GetDecimal(3);var accountId=reader.GetGuid(4);
        Guid? center=reader.IsDBNull(5)?null:reader.GetGuid(5);Guid? party=reader.IsDBNull(6)?null:reader.GetGuid(6);
        await reader.DisposeAsync();
        var settlements=await LoadPurchaseWithholdingSettlementsAsync(connection,transaction,source,total,cancellationToken);
        return FinancialFactsResult.Ready(FinancialFacts.Expense(number,party,untaxed,vat,total,
            settlements,accountId,center));
    }

    private static async Task<FinancialFactsResult> LoadPurchaseReturnFactsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        string number; string currency; decimal total; decimal payableCredit;
        decimal supplierCredit; Guid partyId;
        await using (var command = new SqlCommand("""
            SELECT r.DocumentNumber,r.CurrencyCode,r.TotalAmount,
                   e.PayableCreditAmount,e.SupplierCreditAmount,s.PartyId
            FROM dbo.PurchaseReturns r
            INNER JOIN dbo.PurchaseReturnFinancialEffects e
              ON e.PurchaseReturnId=r.PurchaseReturnId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
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
            supplierCredit=reader.GetDecimal(4); partyId=reader.GetGuid(5);
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
            number,partyId,inventory,expense,deductibleVat,total,settlements));
    }
    private static async Task<FinancialFacts> LoadPayablePaymentFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT s.PartyId,p.DocumentNumber,p.CurrencyCode,p.TotalAmount,p.PaymentMethod
            FROM dbo.SupplierPayments p
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            WHERE p.PaymentId=@DocumentId AND p.BusinessId=@BusinessId AND p.Status=N'Processed';
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "The processed supplier payment was not found for accounting.");
        var partyId = reader.GetGuid(0);
        var number = reader.GetString(1);
        var currency = reader.GetString(2);
        var amount = reader.GetDecimal(3);
        var method = reader.GetString(4);
        await reader.DisposeAsync();
        if (!string.Equals(currency, "COP", StringComparison.Ordinal))
            throw new InvalidOperationException("Supplier payment accounting currently requires COP.");
        var settlement = await ResolveSourceCategoryAsync(connection, transaction,
            "SupplierPaymentMethod", method, cancellationToken);
        return FinancialFacts.PayablePayment(number, partyId, amount, settlement);
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
        var method = reader.GetString(4);
        await reader.DisposeAsync();
        var settlement = await ResolveSourceCategoryAsync(connection, transaction,
            "CustomerPaymentMethod", method, cancellationToken);
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

    private static async Task<string> ResolveSourceCategoryAsync(
        SqlConnection connection, SqlTransaction transaction, string sourceType,
        string sourceCode, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT m.Category
            FROM dbo.AccountingConfigurationProfiles p
            INNER JOIN dbo.AccountingSourceCategoryMappings m ON m.ProfileCode=p.ProfileCode
            WHERE p.IsDefault=1 AND p.IsActive=1
              AND m.SourceType=@SourceType AND m.SourceCode=@SourceCode;
            """, connection, transaction);
        command.Parameters.AddWithValue("@SourceType", sourceType);
        command.Parameters.AddWithValue("@SourceCode", sourceCode);
        var category = await command.ExecuteScalarAsync(cancellationToken) as string;
        return category ?? throw new InvalidOperationException(
            $"Source '{sourceType}:{sourceCode}' has no accounting category mapping.");
    }

    private static async Task<FinancialFactsResult> LoadCashMovementFactsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT d.DocumentNumber,d.Direction,d.Amount,
                   r.CounterpartAccountingCategory,d.CostCenterId
            FROM dbo.CashMovementDocuments d
            INNER JOIN dbo.BusinessReasons r
              ON r.BusinessId=d.BusinessId AND r.ReasonId=d.ReasonId
            WHERE d.DocumentId=@DocumentId AND d.BusinessId=@BusinessId
              AND d.DocumentType=@DocumentType;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "The cash movement was not found for accounting.");
        if (reader.IsDBNull(3))
            return FinancialFactsResult.Pending(
                "CashReasonMappingMissing",
                "The cash movement reason has no counterpart accounting category.");
        return FinancialFactsResult.Ready(FinancialFacts.CashMovement(
            reader.GetString(0), reader.GetString(1) == "In", reader.GetDecimal(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4)));
    }

    private static async Task<FinancialFactsResult> LoadAccountAdjustmentFactsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ConfirmAccountAdjustmentRequest>(source.PayloadJson)
            ?? throw new InvalidOperationException("The account adjustment payload is invalid.");
        var category = request.SubledgerKind == AccountingSubledgerKinds.Receivable
            ? AccountingCategories.AccountsReceivable
            : AccountingCategories.AccountsPayable;
        var accounts = await ResolveAccountsAsync(
            connection, transaction, source, new HashSet<string>([category], StringComparer.Ordinal),
            cancellationToken);
        if (!accounts.TryGetValue(category, out var controlAccountId))
            return FinancialFactsResult.Pending(
                "AccountMappingMissing", $"Missing accounting mapping: {category}.");

        var partyId = await ReadAdjustmentPartyAsync(
            connection, transaction, source.BusinessId, request.SubledgerKind,
            request.SubledgerId, cancellationToken);
        var controlIsDebit = request.SubledgerKind == AccountingSubledgerKinds.Receivable
            ? request.Direction == AccountingAdjustmentDirections.Increase
            : request.Direction == AccountingAdjustmentDirections.Decrease;
        var lines = new[]
        {
            new ManualLineSpec(controlAccountId,
                controlIsDebit ? request.Amount : 0,
                controlIsDebit ? 0 : request.Amount,
                partyId, request.CostCenterId, request.Description),
            new ManualLineSpec(request.CounterpartAccountId,
                controlIsDebit ? 0 : request.Amount,
                controlIsDebit ? request.Amount : 0,
                partyId, request.CostCenterId, request.Description)
        };
        return FinancialFactsResult.Ready(FinancialFacts.Manual(request.Description, lines));
    }

    private static FinancialFacts LoadManualVoucherFacts(SourceEnvelope source)
    {
        var request = JsonSerializer.Deserialize<ConfirmManualAccountingVoucherRequest>(source.PayloadJson)
            ?? throw new InvalidOperationException("The manual voucher payload is invalid.");
        return FinancialFacts.Manual(request.Description, request.Lines.Select(line =>
            new ManualLineSpec(line.AccountId, line.Debit, line.Credit, line.PartyId,
                line.CostCenterId, line.Description)).ToArray());
    }

    private static async Task<Guid> ReadAdjustmentPartyAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        string subledgerKind, Guid subledgerId, CancellationToken cancellationToken)
    {
        var sql = subledgerKind == AccountingSubledgerKinds.Receivable
            ? """
              SELECT c.PartyId FROM dbo.Receivables r
              INNER JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
              WHERE r.ReceivableId=@SubledgerId AND r.BusinessId=@BusinessId;
              """
            : """
              SELECT s.PartyId FROM dbo.Payables p
              INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
              WHERE p.PayableId=@SubledgerId AND p.BusinessId=@BusinessId;
              """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SubledgerId", subledgerId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The adjustment subledger was not found."));
    }

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

    private static async Task<FinancialFacts> LoadDebitNoteFactsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT n.DocumentNumber,n.UntaxedAmount,n.TaxAmount,n.TotalAmount,c.PartyId
            FROM dbo.SalesDebitNotes n
            INNER JOIN dbo.Customers c ON c.CustomerId=n.CustomerId
            WHERE n.DebitNoteId=@DocumentId AND n.BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The sales debit note was not found for accounting.");
        return FinancialFacts.DebitNote(
            reader.GetString(0), reader.GetGuid(4), reader.GetDecimal(1),
            reader.GetDecimal(2), reader.GetDecimal(3));
    }

    private static async Task CompleteOpeningActivationAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken token)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.AccountingOpeningBalanceBatches
            SET Status=N'Posted',PostedAt=SYSDATETIMEOFFSET(),UpdatedAt=SYSDATETIMEOFFSET()
            WHERE BatchId=@DocumentId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND Status=N'Approved';
            IF @@ROWCOUNT<>1 THROW 51407,N'The opening balance approval is no longer valid.',1;

            UPDATE settings
            SET Status=N'Ready',ActivatedAt=SYSDATETIMEOFFSET(),
                ActivatedByUserId=settings.ActivationRequestedByUserId,
                UpdatedAt=SYSDATETIMEOFFSET()
            FROM dbo.AccountingTenantSettings settings
            WHERE settings.TenantId=@TenantId AND settings.Status=N'Configuring'
              AND settings.OpeningBalanceMode=N'ImportedAndApproved'
              AND NOT EXISTS(
                SELECT 1 FROM dbo.Businesses b
                WHERE b.TenantId=@TenantId AND b.IsActive=1 AND NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingOpeningBalanceBatches openingBatch
                  WHERE openingBatch.TenantId=@TenantId AND openingBatch.BusinessId=b.BusinessId
                    AND openingBatch.EffectiveOn=settings.EffectiveFrom
                    AND openingBatch.Status=N'Posted'));
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@TenantId", source.TenantId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        await command.ExecuteNonQueryAsync(token);
    }

    private static void AddSource(SqlCommand command, SourceEnvelope source)
    {
        command.Parameters.AddWithValue("@TenantId", source.TenantId); command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId); command.Parameters.AddWithValue("@DocumentType", source.DocumentType);
        command.Parameters.AddWithValue("@PayloadHash", source.PayloadHash); command.Parameters.AddWithValue("@OccurredAt", source.OccurredAt);
    }
    private static void AddMoney(SqlCommand command, string name, decimal value) { var parameter = command.Parameters.Add(name, SqlDbType.Decimal); parameter.Precision = 19; parameter.Scale = 4; parameter.Value = value; }
    private sealed record SourceEnvelope(
        Guid TenantId,
        Guid BusinessId,
        Guid DocumentId,
        string DocumentType,
        byte[] PayloadHash,
        DateTimeOffset OccurredAt,
        string PayloadJson);

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

    private sealed record FinancialFacts(string Description, Guid? PartyId,
        decimal Untaxed, decimal Tax, decimal Total, decimal Cost,
        IReadOnlyList<(string Category, decimal Amount)> Settlements,
        bool IsReturn, bool IsPurchase, bool IsPayablePayment, bool IsReceivablePayment,
        bool IsCashMovement = false, bool CashIsIn = false, Guid? PreferredCostCenterId = null,
        Guid? DirectExpenseAccountId = null,
        IReadOnlyList<ManualLineSpec>? DirectLines = null,
        IReadOnlyList<CategoryLineSpec>? DirectCategoryLines = null)
    {
        public IReadOnlySet<string> RequiredCategories
        {
            get
            {
                if (DirectLines is not null)
                    return new HashSet<string>(StringComparer.Ordinal);
                if (DirectCategoryLines is not null)
                    return DirectCategoryLines.Select(line => line.Category)
                        .ToHashSet(StringComparer.Ordinal);
                var values = new HashSet<string>(Settlements.Select(item => item.Category), StringComparer.Ordinal);
                if (IsCashMovement)
                {
                    values.Add(AccountingCategories.Cash);
                    return values;
                }
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
                    if (Untaxed > 0 && DirectExpenseAccountId is null) values.Add(AccountingCategories.PurchasesExpense);
                    if (Tax > 0) values.Add(AccountingCategories.InputVat);
                    foreach (var settlement in Settlements) values.Add(settlement.Category);
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
            if (DirectLines is not null)
            {
                foreach (var line in DirectLines)
                    yield return new JournalLine(line.AccountId, line.Debit, line.Credit,
                        line.PartyId, line.CostCenterId ?? costCenter, line.Description);
                yield break;
            }
            if (DirectCategoryLines is not null)
            {
                foreach (var line in DirectCategoryLines)
                    yield return new JournalLine(accounts[line.Category], line.Debit,
                        line.Credit, line.PartyId, costCenter, line.Description);
                yield break;
            }
            if (IsCashMovement)
            {
                var effectiveCostCenter = PreferredCostCenterId ?? costCenter;
                var counterpart = Settlements.Single();
                if (CashIsIn)
                {
                    yield return new(accounts[AccountingCategories.Cash], Total, 0,
                        PartyId, effectiveCostCenter, Description);
                    yield return new(accounts[counterpart.Category], 0, Total,
                        PartyId, effectiveCostCenter, Description);
                }
                else
                {
                    yield return new(accounts[counterpart.Category], Total, 0,
                        PartyId, effectiveCostCenter, Description);
                    yield return new(accounts[AccountingCategories.Cash], 0, Total,
                        PartyId, effectiveCostCenter, Description);
                }
                yield break;
            }
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
                var effectiveCostCenter = PreferredCostCenterId ?? costCenter;
                if (IsReturn)
                {
                    foreach (var settlement in Settlements)
                        yield return new(accounts[settlement.Category], settlement.Amount, 0, PartyId, costCenter, Description);
                    if (Cost > 0) yield return new(accounts[AccountingCategories.Inventory], 0, Cost, PartyId, costCenter, Description);
                    if (Untaxed > 0) yield return new(DirectExpenseAccountId ?? accounts[AccountingCategories.PurchasesExpense], 0, Untaxed, PartyId, effectiveCostCenter, Description);
                    if (Tax > 0) yield return new(accounts[AccountingCategories.InputVat], 0, Tax, PartyId, costCenter, Description);
                }
                else
                {
                    if (Cost > 0) yield return new(accounts[AccountingCategories.Inventory], Cost, 0, PartyId, costCenter, Description);
                    if (Untaxed > 0) yield return new(DirectExpenseAccountId ?? accounts[AccountingCategories.PurchasesExpense], Untaxed, 0, PartyId, effectiveCostCenter, Description);
                    if (Tax > 0) yield return new(accounts[AccountingCategories.InputVat], Tax, 0, PartyId, effectiveCostCenter, Description);
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
        public static FinancialFacts Return(string number, Guid? party, decimal untaxed, decimal tax, decimal total, decimal cost, IReadOnlyList<(string Category, decimal Amount)> settlements) => new($"Devolucion de venta {number}", party, untaxed, tax, total, cost, settlements, true, false, false, false);
        public static FinancialFacts DebitNote(string number, Guid party, decimal untaxed, decimal tax, decimal total) =>
            new($"Nota débito de venta {number}", party, untaxed, tax, total, 0,
                [(AccountingCategories.AccountsReceivable, total)], false, false, false, false);
        public static FinancialFacts Purchase(string number, Guid party, decimal inventory, decimal expense, decimal deductibleVat, decimal total, IReadOnlyList<(string Category, decimal Amount)> settlements) =>
            new($"Entrada de mercancia {number}", party, expense, deductibleVat, total, inventory, settlements, false, true, false, false);
        public static FinancialFacts Expense(string number, Guid? party, decimal untaxed, decimal vat,
            decimal total, IReadOnlyList<(string Category, decimal Amount)> settlements,
            Guid accountId, Guid? costCenter) => new($"Gasto {number}", party, untaxed, vat,
                total, 0, settlements, false, true, false, false, false, false, costCenter, accountId);
        public static FinancialFacts PurchaseReturn(string number, Guid party, decimal inventory, decimal expense, decimal deductibleVat, decimal total, IReadOnlyList<(string Category, decimal Amount)> settlements) => new($"Devolucion de compra {number}", party, expense, deductibleVat, total, inventory, settlements, true, true, false, false);
        public static FinancialFacts PayablePayment(string number, Guid party, decimal total, string settlement) => new($"Pago a proveedor {number}", party, 0, 0, total, 0, [(settlement, total)], false, false, true, false);
        public static FinancialFacts ReceivablePayment(string number, Guid partyId, decimal total, string settlement) => new($"Recaudo de cartera {number}", partyId, 0, 0, total, 0, [(settlement, total)], false, false, false, true);
        public static FinancialFacts CashMovement(
            string number, bool isIn, decimal amount, string counterpart, Guid? costCenter) =>
            new($"{(isIn ? "Ingreso" : "Egreso")} de caja {number}", null, 0, 0,
                amount, 0, [(counterpart, amount)], false, false, false, false, true, isIn, costCenter);
        public static FinancialFacts CashDifference(
            Guid closureId, string userName, bool surplus, decimal amount) =>
            new(
                $"{(surplus ? "Sobrante" : "Faltante")} de efectivo en cierre {closureId:D} · {userName}",
                null, 0, 0, amount, 0,
                [(surplus ? AccountingCategories.CashOverageIncome : AccountingCategories.CashShortageExpense, amount)],
                false, false, false, false, true, surplus);
        public static FinancialFacts Manual(string description, IReadOnlyList<ManualLineSpec> lines) =>
            new(description, null, 0, 0, 0, 0, [], false, false, false, false,
                DirectLines: lines);
        public static FinancialFacts Payroll(string description, IReadOnlyList<CategoryLineSpec> lines) =>
            new(description, null, 0, 0, 0, 0, [], false, false, false, false,
                DirectCategoryLines: lines);
    }

    private sealed record ManualLineSpec(
        Guid AccountId, decimal Debit, decimal Credit, Guid? PartyId,
        Guid? CostCenterId, string Description);

    private sealed record CategoryLineSpec(
        string Category, decimal Debit, decimal Credit, Guid? PartyId,
        string Description);
}
