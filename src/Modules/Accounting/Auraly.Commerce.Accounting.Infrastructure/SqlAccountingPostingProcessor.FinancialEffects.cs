using System.Data;
using System.Text.Json;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    // Runs inside the existing journal-lines command and transaction. The account
    // remains authoritative on the immutable line; the portfolio stores its FK only.
    private const string BindSourceOpeningLinesSql = """
        DECLARE @Openings TABLE(Kind int,PositionId uniqueidentifier,PartyId uniqueidentifier,
          Amount decimal(19,4),OpeningEntryId uniqueidentifier,OpeningEntryLineNumber int,
          OpeningDocumentId uniqueidentifier,ExistingPositionId uniqueidentifier,SourceLineId uniqueidentifier,
          PRIMARY KEY(Kind,PositionId));
        INSERT @Openings
        SELECT @PayableOpening,balance.PayableId,party.PartyId,
          COALESCE(balance.FunctionalOriginalAmount,
            CASE WHEN balance.CurrencyCode=N'COP' THEN balance.OriginalAmount END),
          balance.OpeningEntryId,balance.OpeningEntryLineNumber,NULL,NULL,balance.SourceLineId
        FROM dbo.Payables balance
        LEFT JOIN dbo.Suppliers supplier ON supplier.SupplierId=balance.SupplierId
          AND supplier.TenantId=@TenantId
        LEFT JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.TenantId=@TenantId
        WHERE balance.SourceDocumentId=@DocumentId AND balance.SourceDocumentType=@DocumentType
          AND balance.BusinessId=@BusinessId;
        DECLARE @ReceivableOrigins TABLE(PositionId uniqueidentifier PRIMARY KEY,OpeningDocumentId uniqueidentifier);
        INSERT @ReceivableOrigins
        SELECT ReceivableId,NULL FROM dbo.Receivables
        WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType AND BusinessId=@BusinessId;
        IF @DocumentType=N'WorkSessionClosureReconciliation'
          INSERT @ReceivableOrigins
          SELECT DISTINCT balance.ReceivableId,line.OpeningDocumentId
          FROM @JournalLines line
          JOIN dbo.Receivables balance ON balance.SourceDocumentId=line.OpeningDocumentId
            AND balance.SourceDocumentType IN(N'SalesReceipt',N'SalesInvoice') AND balance.BusinessId=@BusinessId
          WHERE line.OpeningKind=@ReceivableOpening AND line.OpeningDocumentId IS NOT NULL
            AND EXISTS(SELECT 1 FROM dbo.ReceivableTransactions opening
              WHERE opening.ReceivableId=balance.ReceivableId AND opening.TransactionType=N'Opening'
                AND opening.SourceDocumentId=@DocumentId);
        INSERT @Openings
        SELECT @ReceivableOpening,balance.ReceivableId,party.PartyId,balance.OriginalAmount,
          balance.OpeningEntryId,balance.OpeningEntryLineNumber,origin.OpeningDocumentId,NULL,balance.SourceLineId
        FROM @ReceivableOrigins origin JOIN dbo.Receivables balance ON balance.ReceivableId=origin.PositionId
        LEFT JOIN dbo.Customers customer ON customer.CustomerId=balance.CustomerId
          AND customer.TenantId=@TenantId
        LEFT JOIN dbo.Parties party ON party.PartyId=COALESCE(balance.PartyId,customer.PartyId)
          AND party.TenantId=@TenantId;

        INSERT @Openings
        SELECT @SupplierCreditOpening,credit.SupplierCreditId,party.PartyId,
          CASE WHEN credit.CurrencyCode=N'COP' THEN credit.OriginalAmount END,
          credit.OpeningEntryId,credit.OpeningEntryLineNumber,
          CASE WHEN credit.SourceLineId IS NULL THEN COALESCE(credit.SourceDocumentId,credit.SourcePurchaseReturnId) END,
          NULL,credit.SourceLineId
        FROM dbo.SupplierCreditTransactions opening
        JOIN dbo.SupplierCredits credit ON credit.SupplierCreditId=opening.SupplierCreditId AND credit.BusinessId=@BusinessId
        JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId AND supplier.TenantId=@TenantId
        JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.TenantId=@TenantId
        WHERE opening.SourceDocumentId=@DocumentId AND opening.SourceDocumentType=@DocumentType AND opening.TransactionType=N'Opening';

        INSERT @Openings
        SELECT @CustomerCreditOpening,credit.CustomerCreditId,party.PartyId,
          CASE WHEN credit.CurrencyCode=N'COP' THEN credit.OriginalAmount END,
          credit.OpeningEntryId,credit.OpeningEntryLineNumber,
          CASE WHEN credit.SourceLineId IS NULL THEN COALESCE(credit.SourceDocumentId,credit.SourceReturnId) END,
          NULL,credit.SourceLineId
        FROM dbo.CustomerCreditTransactions opening
        JOIN dbo.CustomerCredits credit ON credit.CustomerCreditId=opening.CustomerCreditId AND credit.BusinessId=@BusinessId
        LEFT JOIN dbo.Customers customer ON customer.CustomerId=credit.CustomerId AND customer.TenantId=@TenantId
        LEFT JOIN dbo.Parties party ON party.PartyId=COALESCE(credit.PartyId,customer.PartyId) AND party.TenantId=@TenantId
        WHERE opening.SourceDocumentId=@DocumentId AND opening.SourceDocumentType=@DocumentType AND opening.TransactionType=N'Opening';

        -- The shared opening validator already read and locked these existing
        -- positions in this transaction. Reuse that proof instead of reading
        -- their balances again or creating another portfolio opening.
        IF @DocumentType=N'AccountingOpeningBalance'
          INSERT @Openings
          SELECT line.OpeningKind,line.ExistingPositionId,line.PartyId,
            CASE WHEN line.OpeningKind IN(@PayableOpening,@CustomerCreditOpening) THEN line.Credit ELSE line.Debit END,
            NULL,NULL,NULL,line.ExistingPositionId,NULL
          FROM @JournalLines line WHERE line.ExistingPositionId IS NOT NULL;

        IF EXISTS(SELECT 1 FROM @Openings WHERE PartyId IS NULL OR Amount IS NULL OR Amount<=0
            OR OpeningEntryId IS NOT NULL OR OpeningEntryLineNumber IS NOT NULL)
          THROW 51844,'The opening position has invalid scope, value or an existing journal binding.',1;
        IF EXISTS(SELECT 1 FROM @JournalLines line WHERE line.OpeningDocumentId IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM @Openings opening WHERE opening.Kind=line.OpeningKind
            AND opening.OpeningDocumentId=line.OpeningDocumentId AND opening.PartyId=line.PartyId))
          THROW 51844,'The identified opening document has no matching position in this source.',1;
        IF EXISTS(SELECT 1 FROM @JournalLines line WHERE line.ExistingPositionId IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM @Openings opening WHERE opening.Kind=line.OpeningKind
            AND opening.ExistingPositionId=line.ExistingPositionId AND opening.PartyId=line.PartyId))
          THROW 51844,'The reconciled position has no validated opening binding.',1;

        DECLARE @OpeningLines TABLE(Kind int,PositionId uniqueidentifier,LineNumber int,
          PRIMARY KEY(Kind,PositionId));
        INSERT @OpeningLines
        SELECT opening.Kind,opening.PositionId,MIN(line.LineNumber)
        FROM @Openings opening
        JOIN @JournalLines line ON line.OpeningKind=opening.Kind AND line.PartyId=opening.PartyId
          AND (line.OpeningDocumentId=opening.OpeningDocumentId
            OR line.OpeningDocumentId IS NULL AND opening.OpeningDocumentId IS NULL)
          AND (line.ExistingPositionId=opening.ExistingPositionId
            OR line.ExistingPositionId IS NULL AND opening.ExistingPositionId IS NULL)
          AND (opening.SourceLineId IS NULL OR line.SourceLineId=opening.SourceLineId)
        GROUP BY opening.Kind,opening.PositionId
        HAVING COUNT(*)=1;
        IF (SELECT COUNT(*) FROM @OpeningLines)<>(SELECT COUNT(*) FROM @Openings)
          THROW 51844,'Each opening position requires one identified journal line.',1;
        IF EXISTS(SELECT 1 FROM @OpeningLines binding
          JOIN @Openings opening ON opening.Kind=binding.Kind AND opening.PositionId=binding.PositionId
          JOIN @JournalLines line ON line.LineNumber=binding.LineNumber
          WHERE opening.Amount<>CASE WHEN opening.Kind IN(@PayableOpening,@CustomerCreditOpening) THEN line.Credit ELSE line.Debit END)
          THROW 51844,'The opening position and its journal line have different values.',1;

        UPDATE balance SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=binding.LineNumber
        FROM dbo.Payables balance JOIN @OpeningLines binding ON binding.Kind=@PayableOpening
          AND binding.PositionId=balance.PayableId
        WHERE balance.BusinessId=@BusinessId AND balance.OpeningEntryId IS NULL;
        IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Openings WHERE Kind=@PayableOpening)
          THROW 51844,'The payable opening binding changed.',1;
        UPDATE balance SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=binding.LineNumber
        FROM dbo.Receivables balance JOIN @OpeningLines binding ON binding.Kind=@ReceivableOpening
          AND binding.PositionId=balance.ReceivableId
        WHERE balance.BusinessId=@BusinessId AND balance.OpeningEntryId IS NULL;
        IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Openings WHERE Kind=@ReceivableOpening)
          THROW 51844,'The receivable opening binding changed.',1;
        UPDATE credit SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=binding.LineNumber
        FROM dbo.SupplierCredits credit JOIN @OpeningLines binding ON binding.Kind=@SupplierCreditOpening
          AND binding.PositionId=credit.SupplierCreditId
        WHERE credit.BusinessId=@BusinessId AND credit.OpeningEntryId IS NULL;
        IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Openings WHERE Kind=@SupplierCreditOpening)
          THROW 51844,'The supplier credit opening binding changed.',1;
        UPDATE credit SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=binding.LineNumber
        FROM dbo.CustomerCredits credit JOIN @OpeningLines binding ON binding.Kind=@CustomerCreditOpening
          AND binding.PositionId=credit.CustomerCreditId
        WHERE credit.BusinessId=@BusinessId AND credit.OpeningEntryId IS NULL;
        IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @Openings WHERE Kind=@CustomerCreditOpening)
          THROW 51844,'The customer credit opening binding changed.',1;
        """;

    // Source-specific preparation stays in each existing operation. All new
    // receivables and Opening transactions are written by this same SQL batch,
    // including closure corrections whose transaction source differs from the sale.
    private const string DeclareReceivableOpeningsSql = """
        DECLARE @ReceivableOpenings TABLE(ReceivableId uniqueidentifier PRIMARY KEY,
          TransactionId uniqueidentifier,CustomerId uniqueidentifier,PartyId uniqueidentifier,
          PartySiteId uniqueidentifier,DocumentId uniqueidentifier,DocumentType nvarchar(64),
          SourceLineId uniqueidentifier,Number nvarchar(64),Amount decimal(19,4),
          DueDate datetimeoffset(7),OccurredAt datetimeoffset(7),TransactionSourceId uniqueidentifier);
        """;

    private const string OpenReceivablesSql = """
        DECLARE @ReceivableOpeningCount int=(SELECT COUNT(*) FROM @ReceivableOpenings);
        INSERT dbo.Receivables(ReceivableId,BusinessId,CustomerId,PartyId,PartySiteId,
          SourceDocumentId,SourceDocumentType,SourceLineId,DocumentNumber,CurrencyCode,
          OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
        SELECT opening.ReceivableId,@BusinessId,opening.CustomerId,opening.PartyId,opening.PartySiteId,
          opening.DocumentId,opening.DocumentType,opening.SourceLineId,opening.Number,N'COP',
          opening.Amount,opening.Amount,opening.DueDate,N'Open',@Now
        FROM @ReceivableOpenings opening JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
        LEFT JOIN dbo.Customers customer ON customer.CustomerId=opening.CustomerId AND customer.TenantId=business.TenantId
        LEFT JOIN dbo.PartySites site ON site.PartySiteId=opening.PartySiteId AND site.PartyId=customer.PartyId
        LEFT JOIN dbo.Parties party ON party.PartyId=opening.PartyId AND party.TenantId=business.TenantId
        WHERE opening.CustomerId IS NOT NULL AND opening.PartyId IS NULL AND customer.CustomerId IS NOT NULL AND site.PartySiteId IS NOT NULL
          OR opening.CustomerId IS NULL AND opening.PartySiteId IS NULL AND party.PartyId IS NOT NULL;
        IF @@ROWCOUNT<>@ReceivableOpeningCount THROW 51845,'A receivable debtor or site is outside the source scope.',1;
        INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,TransactionType,Amount,
          SourceDocumentId,OccurredAt,CreatedAt)
        SELECT TransactionId,ReceivableId,N'Opening',Amount,COALESCE(TransactionSourceId,DocumentId),OccurredAt,@Now
        FROM @ReceivableOpenings;
        IF @@ROWCOUNT<>@ReceivableOpeningCount THROW 51845,'The receivable opening transactions are incomplete.',1;
        """;

    private const string DeclareSupplierCreditOpeningsSql = """
        DECLARE @SupplierCreditOpenings TABLE(CreditId uniqueidentifier PRIMARY KEY,TransactionId uniqueidentifier,
          SupplierId uniqueidentifier,PartySiteId uniqueidentifier,PurchaseReturnId uniqueidentifier,
          DocumentId uniqueidentifier,DocumentType nvarchar(40),SourceLineId uniqueidentifier,
          Nature nvarchar(16),Currency char(3),Amount decimal(19,4));
        """;

    private const string OpenSupplierCreditsSql = """
        DECLARE @SupplierCreditOpeningCount int=(SELECT COUNT(*) FROM @SupplierCreditOpenings);
        INSERT dbo.SupplierCredits(SupplierCreditId,BusinessId,SupplierId,PartySiteId,SourcePurchaseReturnId,
          SourceDocumentId,SourceDocumentType,SourceLineId,CreditNature,CurrencyCode,OriginalAmount,AvailableAmount,Status,CreatedAt)
        SELECT input.CreditId,@BusinessId,input.SupplierId,input.PartySiteId,input.PurchaseReturnId,
          input.DocumentId,input.DocumentType,input.SourceLineId,input.Nature,input.Currency,input.Amount,input.Amount,N'Open',@Now
        FROM @SupplierCreditOpenings input JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
        JOIN dbo.Suppliers supplier ON supplier.SupplierId=input.SupplierId AND supplier.TenantId=business.TenantId
        JOIN dbo.AccountingPostingJobs origin ON origin.SourceDocumentId=@CreditParentDocumentId
          AND origin.SourceDocumentType=@CreditParentDocumentType AND origin.BusinessId=@BusinessId AND origin.TenantId=business.TenantId
        LEFT JOIN dbo.PartySites site ON site.PartySiteId=input.PartySiteId AND site.PartyId=supplier.PartyId
        WHERE input.Nature IN(N'Credit',N'Advance') AND input.Currency IS NOT NULL AND LEN(input.Currency)=3
          AND (input.PartySiteId IS NULL OR site.PartySiteId IS NOT NULL);
        IF @@ROWCOUNT<>@SupplierCreditOpeningCount THROW 51846,'The supplier credit opening has invalid source, party, site or currency.',1;
        INSERT dbo.SupplierCreditTransactions(SupplierCreditTransactionId,SupplierCreditId,TransactionType,Amount,
          SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
        SELECT TransactionId,CreditId,N'Opening',Amount,@CreditParentDocumentId,@CreditParentDocumentType,
          SourceLineId,@CreditAt,@Now FROM @SupplierCreditOpenings;
        IF @@ROWCOUNT<>@SupplierCreditOpeningCount THROW 51846,'The supplier credit opening transactions are incomplete.',1;
        """;

    private const string DeclareCustomerCreditOpeningsSql = """
        DECLARE @CustomerCreditOpenings TABLE(CreditId uniqueidentifier PRIMARY KEY,TransactionId uniqueidentifier,
          CustomerId uniqueidentifier,PartyId uniqueidentifier,PartySiteId uniqueidentifier,ReturnId uniqueidentifier,
          DocumentId uniqueidentifier,DocumentType nvarchar(64),SourceLineId uniqueidentifier,
          Nature nvarchar(16),Currency char(3),Amount decimal(19,4));
        """;

    private const string OpenCustomerCreditsSql = """
        DECLARE @CustomerCreditOpeningCount int=(SELECT COUNT(*) FROM @CustomerCreditOpenings);
        INSERT dbo.CustomerCredits(CustomerCreditId,BusinessId,CustomerId,PartyId,PartySiteId,SourceReturnId,
          SourceDocumentId,SourceDocumentType,SourceLineId,CreditNature,CurrencyCode,OriginalAmount,AvailableAmount,Status,CreatedAt)
        SELECT input.CreditId,@BusinessId,input.CustomerId,input.PartyId,input.PartySiteId,input.ReturnId,
          input.DocumentId,input.DocumentType,input.SourceLineId,input.Nature,input.Currency,input.Amount,input.Amount,N'Open',@Now
        FROM @CustomerCreditOpenings input JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
        JOIN dbo.AccountingPostingJobs origin ON origin.SourceDocumentId=@CreditParentDocumentId
          AND origin.SourceDocumentType=@CreditParentDocumentType AND origin.BusinessId=@BusinessId AND origin.TenantId=business.TenantId
        LEFT JOIN dbo.Customers customer ON customer.CustomerId=input.CustomerId AND customer.TenantId=business.TenantId
        LEFT JOIN dbo.PartySites site ON site.PartySiteId=input.PartySiteId AND site.PartyId=customer.PartyId
        LEFT JOIN dbo.Parties party ON party.PartyId=input.PartyId AND party.TenantId=business.TenantId
        WHERE input.Nature IN(N'Credit',N'Advance') AND input.Currency=N'COP' AND (
          input.CustomerId IS NOT NULL AND customer.CustomerId IS NOT NULL AND input.PartyId IS NULL
            AND (input.PartySiteId IS NULL OR site.PartySiteId IS NOT NULL)
          OR input.CustomerId IS NULL AND party.PartyId IS NOT NULL AND input.PartySiteId IS NULL);
        IF @@ROWCOUNT<>@CustomerCreditOpeningCount THROW 51847,'The customer credit opening has invalid source, party, site or currency.',1;
        INSERT dbo.CustomerCreditTransactions(CustomerCreditTransactionId,CustomerCreditId,TransactionType,Amount,
          SourceDocumentId,SourceDocumentType,SourceLineId,OccurredAt,CreatedAt)
        SELECT TransactionId,CreditId,N'Opening',Amount,@CreditParentDocumentId,@CreditParentDocumentType,
          SourceLineId,@CreditAt,@Now FROM @CustomerCreditOpenings;
        IF @@ROWCOUNT<>@CustomerCreditOpeningCount THROW 51847,'The customer credit opening transactions are incomplete.',1;
        """;

    private async Task ApplyFinancialEffectsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        Guid? affectedCustomerId = null;
        IReadOnlyList<Guid> affectedCustomerIds = [];
        switch (source.DocumentType)
        {
            case "SalesInvoice":
            case "SalesReceipt":
                var sale = PosSaleContractSerializer.Deserialize(source.PayloadJson);
                await ApplySaleFinancialEffectsAsync(
                    connection, transaction, sale, source.DocumentType, cancellationToken);
                affectedCustomerId = sale.Credit?.CustomerId;
                break;
            case "ServiceInvoice":
                var serviceInvoice = ServiceInvoiceSnapshotSerializer.Deserialize(source.PayloadJson);
                await ApplyServiceInvoiceFinancialEffectsAsync(
                    connection, transaction, serviceInvoice, cancellationToken);
                affectedCustomerId = serviceInvoice.CustomerId;
                break;
            case "SalesReturn":
                var salesReturn = SalesReturnContractSerializer.Deserialize(source.PayloadJson);
                await ApplySalesReturnFinancialEffectsAsync(
                    connection, transaction, salesReturn, cancellationToken);
                affectedCustomerId = salesReturn.CustomerId;
                break;
            case "SalesDebitNote":
                var debitNote = SalesDebitNoteContractSerializer.Deserialize(source.PayloadJson);
                await ApplySalesDebitNoteFinancialEffectsAsync(
                    connection, transaction, debitNote, cancellationToken);
                affectedCustomerId = debitNote.CustomerId;
                break;
            case "ReceivablePayment":
                var customerPayment = CustomerPaymentContractSerializer.Deserialize(source.PayloadJson);
                await ApplyReceivablePaymentFinancialEffectsAsync(
                    connection, transaction, customerPayment, cancellationToken);
                affectedCustomerId = customerPayment.CustomerId;
                break;
            case "PreexistingReceivable":
                var opening=PreexistingReceivableContractSerializer.Deserialize(source.PayloadJson);
                await ApplyPreexistingReceivableAsync(connection,transaction,opening,cancellationToken);
                affectedCustomerId=opening.CustomerId;
                break;
            case "GoodsReceipt":
                await ApplyGoodsReceiptFinancialEffectsAsync(
                    connection, transaction,
                    GoodsReceiptContractSerializer.Deserialize(source.PayloadJson), cancellationToken);
                break;
            case "GoodsReceiptCostDocument":
                await ApplyGoodsReceiptCostDocumentFinancialEffectsAsync(
                    connection, transaction,
                    GoodsReceiptContractSerializer.DeserializeCostDocument(source.PayloadJson), cancellationToken);
                break;
            case "Expense":
                await ApplyExpenseFinancialEffectsAsync(
                    connection, transaction,
                    ExpenseContractSerializer.Deserialize(source.PayloadJson), cancellationToken);
                break;
            case "ExpenseCancellation":
                await ApplyExpenseCancellationFinancialEffectsAsync(connection, transaction,
                    ExpenseCancellationSerializer.Deserialize(source.PayloadJson), cancellationToken);
                break;
            case "PurchaseReturn":
                await ApplyPurchaseReturnFinancialEffectsAsync(
                    connection, transaction,
                    PurchaseReturnContractSerializer.Deserialize(source.PayloadJson), cancellationToken);
                break;
            case "PayablePayment":
                await ApplyPayablePaymentFinancialEffectsAsync(
                    connection, transaction,
                    SupplierPaymentContractSerializer.Deserialize(source.PayloadJson), cancellationToken);
                break;
            case "CashReceipt":
            case "CashDisbursement":
                await ApplyCashMovementFinancialEffectsAsync(
                    connection, transaction,
                    CashMovementContractSerializer.Deserialize(source.PayloadJson), cancellationToken);
                break;
            case AccountingManualDocumentTypes.AccountAdjustment:
                affectedCustomerId = await ReadAdjustedCustomerIdAsync(
                    connection, transaction, source, cancellationToken);
                await ApplyAccountAdjustmentFinancialEffectsAsync(
                    connection, transaction, source, cancellationToken);
                break;
            case AccountingManualDocumentTypes.ManualVoucher:
                affectedCustomerIds = await ApplyManualVoucherFinancialEffectsAsync(connection, transaction, source, cancellationToken);
                break;
            case WorkSessionAccountingDocumentTypes.ClosureReconciliation:
                var closure = ReadClosureReconciliation(source);
                affectedCustomerIds = await ApplyClosureCorrectionFinancialEffectsAsync(
                    connection, transaction, closure, cancellationToken);
                break;
        }

        if (affectedCustomerId.HasValue || affectedCustomerIds.Count > 0)
            await SqlAccountingPosSynchronizationOutbox.InsertCustomersAsync(
                connection, transaction, source.BusinessId,
                affectedCustomerId is Guid customerId ? [customerId] : affectedCustomerIds,
                ids, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static async Task<Guid?> ReadAdjustedCustomerIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<ConfirmAccountAdjustmentRequest>(
            source.PayloadJson) ?? throw new InvalidOperationException(
                "The account adjustment payload is invalid.");
        if (request.SubledgerKind != AccountingSubledgerKinds.Receivable) return null;
        await using var command = new SqlCommand("""
            SELECT CustomerId
            FROM dbo.Receivables
            WHERE ReceivableId=@ReceivableId AND BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ReceivableId", request.SubledgerId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private async Task ApplyServiceInvoiceFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        ServiceInvoiceSnapshot invoice, CancellationToken token)
    {
        var creditAmount = decimal.Round(
            invoice.CommercialSnapshot.PayableAmount - invoice.Payment.Amount, 4);
        if (creditAmount <= 0) return;

        await using var receivable = new SqlCommand(DeclareReceivableOpeningsSql + """
            INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartySiteId,DocumentId,DocumentType,
              Number,Amount,DueDate,OccurredAt)
            VALUES(@ReceivableId,@TransactionId,@CustomerId,@PartySiteId,@DocumentId,N'ServiceInvoice',
              @Number,@Amount,@DueDate,@OccurredAt);
            """ + OpenReceivablesSql, connection, transaction);
        receivable.Parameters.AddWithValue("@ReceivableId", ids.NewId());
        receivable.Parameters.AddWithValue("@TransactionId", ids.NewId());
        receivable.Parameters.AddWithValue("@BusinessId", invoice.BusinessId);
        receivable.Parameters.AddWithValue("@CustomerId", invoice.CustomerId);
        receivable.Parameters.AddWithValue("@PartySiteId",
            (object?)invoice.CustomerPartySiteId ?? DBNull.Value);
        receivable.Parameters.AddWithValue("@DocumentId", invoice.DocumentId);
        receivable.Parameters.AddWithValue("@Number", invoice.DocumentNumber.FullNumber);
        AddMoney(receivable, "@Amount", creditAmount);
        receivable.Parameters.AddWithValue(
            "@DueDate", invoice.UblSnapshot.DueDate.ToDateTime(TimeOnly.MinValue));
        receivable.Parameters.AddWithValue("@OccurredAt", invoice.CommercialSnapshot.IssuedAt);
        receivable.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await receivable.ExecuteNonQueryAsync(token);
    }

    private async Task ApplySalesDebitNoteFinancialEffectsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SalesDebitNoteDocumentPayload value,
        CancellationToken token)
    {
        await using var command = new SqlCommand(DeclareReceivableOpeningsSql + """
            INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartySiteId,DocumentId,DocumentType,
              Number,Amount,DueDate,OccurredAt)
            SELECT @ReceivableId,@TransactionId,@CustomerId,document.CustomerPartySiteId,@DocumentId,N'SalesDebitNote',
              @Number,@Amount,@DueDate,@OccurredAt
            FROM dbo.SalesDocuments document
            WHERE document.DocumentId=@OriginalDocumentId AND document.BusinessId=@BusinessId;
            IF @@ROWCOUNT<>1 THROW 51845,'The debit-note original is unavailable.',1;
            """ + OpenReceivablesSql, connection, transaction);
        command.Parameters.AddWithValue("@ReceivableId", ids.NewId());
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@CustomerId", value.CustomerId);
        command.Parameters.AddWithValue("@DocumentId", value.DebitNoteId);
        command.Parameters.AddWithValue("@OriginalDocumentId", value.OriginalDocumentId);
        command.Parameters.AddWithValue("@Number", value.DocumentNumber);
        AddMoney(command, "@Amount", value.TotalAmount);
        command.Parameters.AddWithValue("@DueDate", value.DueAt);
        command.Parameters.AddWithValue("@OccurredAt", value.IssuedAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplyAccountAdjustmentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction, SourceEnvelope source,
        CancellationToken token)
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<ConfirmAccountAdjustmentRequest>(
            source.PayloadJson) ?? throw new InvalidOperationException(
                "The account adjustment payload is invalid.");
        var delta = request.Direction == AccountingAdjustmentDirections.Increase
            ? request.Amount : -request.Amount;
        var now = timeProvider.GetUtcNow();
        var isReceivable = request.SubledgerKind == AccountingSubledgerKinds.Receivable;
        var positionTable = isReceivable ? "Receivables" : "Payables";
        var positionId = isReceivable ? "ReceivableId" : "PayableId";
        var transactionTable = isReceivable ? "ReceivableTransactions" : "PayableTransactions";
        var transactionId = isReceivable ? "ReceivableTransactionId" : "PayableTransactionId";
        var sql = DeclarePortfolioBalanceChangesSql + """
            INSERT @PortfolioChanges VALUES(@SubledgerId,@Delta);
            """ + PortfolioBalanceChangesSql(positionTable, positionId, PortfolioBalanceChangeKind.Adjustment) + $"""
            INSERT dbo.[{transactionTable}]([{transactionId}],[{positionId}],TransactionType,Amount,
              SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@SubledgerId,N'Adjustment',@Delta,@DocumentId,@OccurredAt,@Now);
            IF @@ROWCOUNT<>1 THROW 51607,'The portfolio adjustment history is incomplete.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SubledgerId", request.SubledgerId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
        command.Parameters.AddWithValue("@Now", now);
        AddMoney(command, "@Delta", delta);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplySaleFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        PosSaleUploadRequest sale, string sourceDocumentType, CancellationToken token)
    {
        await using var sessionCommand = new SqlCommand("""
            SELECT WorkSessionId
            FROM dbo.SalesDocuments WITH(UPDLOCK,HOLDLOCK)
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            """, connection, transaction);
        sessionCommand.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
        sessionCommand.Parameters.AddWithValue("@BusinessId", sale.BusinessId);
        var canonicalWorkSessionId = await sessionCommand.ExecuteScalarAsync(token) as Guid?
            ?? throw new InvalidOperationException(
                "The processed sale has no canonical work session.");

        if (sale.Credit is not null)
        {
            await using var receivable = new SqlCommand(DeclareReceivableOpeningsSql + """
                INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartySiteId,DocumentId,DocumentType,
                  Number,Amount,DueDate,OccurredAt)
                VALUES(@ReceivableId,@TransactionId,@CustomerId,@PartySiteId,@DocumentId,@DocumentType,
                  @Number,@Amount,@DueDate,@OccurredAt);
                """ + OpenReceivablesSql, connection, transaction);
            receivable.Parameters.AddWithValue("@ReceivableId", ids.NewId());
            receivable.Parameters.AddWithValue("@TransactionId", ids.NewId());
            receivable.Parameters.AddWithValue("@BusinessId", sale.BusinessId);
            receivable.Parameters.AddWithValue("@CustomerId", sale.Credit.CustomerId);
            receivable.Parameters.AddWithValue("@PartySiteId",
                (object?)sale.Credit.PartySiteId ?? DBNull.Value);
            receivable.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
            receivable.Parameters.AddWithValue("@DocumentType", sourceDocumentType);
            receivable.Parameters.AddWithValue("@Number", sale.DocumentNumber.FullNumber);
            AddMoney(receivable, "@Amount", sale.Credit.Amount);
            receivable.Parameters.AddWithValue("@DueDate", sale.Credit.DueDate);
            receivable.Parameters.AddWithValue("@OccurredAt", sale.CommercialSnapshot.IssuedAt);
            receivable.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await receivable.ExecuteNonQueryAsync(token);
        }

        if (sale.Payments.Count > 0)
        {
            await using var movement = new SqlCommand("""
                INSERT dbo.WorkSessionMovements
                  (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
                   BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,SourceKey,
                   OccurredAt,RecordedByUserId)
                SELECT p.Id,@SessionId,@DocumentId,p.PaymentNumber,@Date,N'SalePayment',p.MethodCode,
                  p.Amount,p.Reference,p.SourceKey,@OccurredAt,@UserId
                FROM OPENJSON(@Payments) WITH(Id uniqueidentifier,PaymentNumber int,
                  MethodCode nvarchar(32),Amount decimal(19,4),Reference nvarchar(max),SourceKey nvarchar(160)) p;
                IF @@ROWCOUNT<>@Count THROW 51607,'The sale payments were not recorded atomically.',1;
                """, connection, transaction);
            movement.Parameters.AddWithValue("@SessionId", canonicalWorkSessionId);
            movement.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
            movement.Parameters.AddWithValue("@Date", sale.CommercialSnapshot.IssuedAt.Date);
            movement.Parameters.AddWithValue("@OccurredAt", sale.CommercialSnapshot.IssuedAt);
            movement.Parameters.AddWithValue("@UserId", sale.SoldByUserId);
            movement.Parameters.AddWithValue("@Count", sale.Payments.Count);
            movement.Parameters.Add("@Payments", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
                sale.Payments.Select(payment => new { Id = ids.NewId(), payment.PaymentNumber,
                    payment.MethodCode, Amount = payment.CollectedAmount, payment.Reference,
                    SourceKey = $"sale:{sale.DocumentId:D}:{payment.PaymentNumber}" }));
            await movement.ExecuteNonQueryAsync(token);
        }
    }

    private async Task ApplySalesReturnFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        SalesReturnDocumentPayload value, CancellationToken token)
    {
        await using (var settlement = new SqlCommand("""
            INSERT dbo.SalesReturnSettlements
              (ReturnId,SettlementNumber,SettlementType,MethodCode,OriginalDocumentId,
               OriginalPaymentNumber,Amount,Reference,Notes,CardFranchiseCode,ApprovalNumber,BankAccountId,OccurredAt)
            VALUES(@ReturnId,1,@Type,@Method,@OriginalId,@PaymentNumber,@Amount,@Reference,
               @Notes,@CardFranchiseCode,@ApprovalNumber,@BankAccountId,@At);
            """, connection, transaction))
        {
            settlement.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            settlement.Parameters.AddWithValue("@Type", value.EconomicResolution);
            settlement.Parameters.AddWithValue("@Method", (object?)value.RefundMethodCode ?? DBNull.Value);
            settlement.Parameters.AddWithValue("@OriginalId", value.OriginalDocumentId);
            settlement.Parameters.AddWithValue("@PaymentNumber", (object?)value.OriginalPaymentNumber ?? DBNull.Value);
            AddMoney(settlement, "@Amount", value.TotalAmount);
            settlement.Parameters.AddWithValue("@Reference",
                (object?)value.SettlementReference ?? value.DocumentNumber);
            settlement.Parameters.AddWithValue("@Notes", (object?)value.SettlementNotes ?? DBNull.Value);
            settlement.Parameters.AddWithValue("@CardFranchiseCode", (object?)value.CardFranchiseCode ?? DBNull.Value);
            settlement.Parameters.AddWithValue("@ApprovalNumber", (object?)value.ApprovalNumber ?? DBNull.Value);
            settlement.Parameters.AddWithValue("@BankAccountId", (object?)value.BankAccountId ?? DBNull.Value);
            settlement.Parameters.AddWithValue("@At", value.ReturnedAt);
            await settlement.ExecuteNonQueryAsync(token);
        }

        await ApplySalesReturnChargeFinancialEffectsAsync(
            connection, transaction, value, token);

        if (value.EconomicResolution == ReturnEconomicResolutions.Refund)
        {
            if (value.RefundMethodCode != SalesReturnRefundMethods.Cash) return;
            // A cash refund created from administration is a treasury/accounting
            // settlement, not a cashier drawer movement. POS cash refunds carry
            // their immutable work session and are included in that closure.
            if (value.WorkSessionId is null) return;
            await using var refund = new SqlCommand("""
                INSERT dbo.WorkSessionMovements
                  (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
                   MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
                VALUES(@Id,@SessionId,NULL,NULL,@Date,N'Refund',N'Cash',
                   @Amount,@Reference,@SourceKey,@At,@UserId);
                """, connection, transaction);
            refund.Parameters.AddWithValue("@Id", ids.NewId());
            refund.Parameters.AddWithValue("@SessionId", value.WorkSessionId.Value);
            refund.Parameters.AddWithValue("@Date", value.ReturnedAt.Date);
            AddMoney(refund, "@Amount", -value.TotalAmount);
            refund.Parameters.AddWithValue("@Reference", value.DocumentNumber);
            refund.Parameters.AddWithValue("@SourceKey", $"sales-return:{value.ReturnId:N}");
            refund.Parameters.AddWithValue("@At", value.ReturnedAt);
            refund.Parameters.AddWithValue("@UserId", value.CreatedByUserId);
            await refund.ExecuteNonQueryAsync(token);
            return;
        }

        if (value.CustomerId is null)
            throw new InvalidOperationException("Customer credit requires an identified customer.");
        Guid? receivableId = null;
        decimal availableAmount = 0;
        await using (var load = new SqlCommand("""
            SELECT TOP(1) receivable.ReceivableId,available.AvailableAmount
            FROM dbo.Receivables receivable WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.SalesDocuments sale
              ON sale.DocumentId=receivable.SourceDocumentId
             AND sale.BusinessId=receivable.BusinessId
             AND sale.DocumentType=receivable.SourceDocumentType
            CROSS APPLY accounting.ObligationAvailability(receivable.BusinessId,N'Receivable',receivable.ReceivableId,
              receivable.OutstandingAmount,NULL) available
            WHERE receivable.BusinessId=@BusinessId AND receivable.CustomerId=@CustomerId
              AND receivable.SourceDocumentId=@OriginalId
              AND receivable.Status IN(N'Open',N'PartiallyPaid')
            ORDER BY receivable.CreatedAt;
            """, connection, transaction))
        {
            load.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            load.Parameters.AddWithValue("@CustomerId", value.CustomerId.Value);
            load.Parameters.AddWithValue("@OriginalId", value.OriginalDocumentId);
            await using var reader = await load.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                receivableId = reader.GetGuid(0);
                availableAmount = reader.GetDecimal(1);
            }
        }
        if (availableAmount < 0)
            throw new InvalidOperationException("La cartera y sus reservas requieren conciliación antes de aplicar la devolución.");
        var applied = decimal.Min(value.TotalAmount, availableAmount);
        var credit = value.TotalAmount - applied;
        var now = timeProvider.GetUtcNow();
        if (receivableId is not null && applied > 0)
        {
            await using var apply = new SqlCommand(DeclarePortfolioBalanceChangesSql + """
                INSERT @PortfolioChanges VALUES(@ReceivableId,-@Amount);
                """ + PortfolioBalanceChangesSql("Receivables", "ReceivableId", PortfolioBalanceChangeKind.Settlement) + """
                INSERT dbo.ReceivableTransactions
                  (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@ReceivableId,N'Reversal',@Amount,@ReturnId,@At,@Now);
                IF @@ROWCOUNT<>1 THROW 51607,'The return transaction was not persisted.',1;
                INSERT dbo.SalesReturnReceivableApplications
                  (ReturnId,ReceivableId,Amount,AppliedAt)
                VALUES(@ReturnId,@ReceivableId,@Amount,@Now);
                SELECT @@ROWCOUNT;
                """, connection, transaction);
            apply.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            apply.Parameters.AddWithValue("@ReceivableId", receivableId.Value);
            apply.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(apply, "@Amount", applied);
            apply.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            apply.Parameters.AddWithValue("@At", value.ReturnedAt);
            apply.Parameters.AddWithValue("@Now", now);
            if (await apply.ExecuteScalarAsync(token) is not int written || written != 1)
                throw new DBConcurrencyException("The return was not applied to its receivable.");
        }
        if (credit > 0)
        {
            await using var createCredit = new SqlCommand(DeclareCustomerCreditOpeningsSql + """
                INSERT @CustomerCreditOpenings(CreditId,TransactionId,CustomerId,ReturnId,Nature,Currency,Amount)
                VALUES(@Id,@CreditTransactionId,@CustomerId,@ReturnId,N'Credit',N'COP',@Amount);
                """ + OpenCustomerCreditsSql, connection, transaction);
            createCredit.Parameters.AddWithValue("@Id", ids.NewId());
            createCredit.Parameters.AddWithValue("@CreditTransactionId", ids.NewId());
            createCredit.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            createCredit.Parameters.AddWithValue("@CustomerId", value.CustomerId.Value);
            createCredit.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            createCredit.Parameters.AddWithValue("@CreditParentDocumentId", value.ReturnId);
            createCredit.Parameters.AddWithValue("@CreditParentDocumentType", "SalesReturn");
            createCredit.Parameters.AddWithValue("@CreditAt", value.ReturnedAt);
            AddMoney(createCredit, "@Amount", credit);
            createCredit.Parameters.AddWithValue("@Now", now);
            await createCredit.ExecuteNonQueryAsync(token);
        }
    }

    private async Task ApplyPreexistingReceivableAsync(SqlConnection connection,SqlTransaction transaction,
        PreexistingReceivablePayload value,CancellationToken token)
    {
        await using var command=new SqlCommand(DeclareReceivableOpeningsSql + """
            INSERT @ReceivableOpenings(ReceivableId,TransactionId,CustomerId,PartySiteId,DocumentId,DocumentType,
              Number,Amount,DueDate,OccurredAt)
            VALUES(@Id,@TransactionId,@CustomerId,@PartySiteId,@Id,N'PreexistingReceivable',@Number,@Amount,@DueDate,@IssuedAt);
            """ + OpenReceivablesSql,connection,transaction);
        command.Parameters.AddWithValue("@Id",value.ReceivableId);command.Parameters.AddWithValue("@TransactionId",ids.NewId());command.Parameters.AddWithValue("@BusinessId",value.BusinessId);command.Parameters.AddWithValue("@CustomerId",value.CustomerId);command.Parameters.AddWithValue("@PartySiteId",(object?)value.PartySiteId??DBNull.Value);command.Parameters.AddWithValue("@Number",value.DocumentNumber);AddMoney(command,"@Amount",value.Amount);command.Parameters.AddWithValue("@DueDate",value.DueDate);command.Parameters.AddWithValue("@IssuedAt",value.IssuedAt);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplySalesReturnChargeFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        SalesReturnDocumentPayload value, CancellationToken token)
    {
        if (value.Charges is not { Count: > 0 }) return;
        var now = timeProvider.GetUtcNow();
        var rows = value.Charges.Select(charge => new
        {
            charge.AppliedChargeId,
            charge.SupplierId,
            TransactionId = ids.NewId(),
            SupplierCreditId = ids.NewId(), CreditTransactionId = ids.NewId()
        }).ToArray();
        await using var command = new SqlCommand(DeclareSupplierCreditOpeningsSql + """
            DECLARE @Input TABLE(AppliedChargeId uniqueidentifier PRIMARY KEY,SupplierId uniqueidentifier,
              TransactionId uniqueidentifier,SupplierCreditId uniqueidentifier,CreditTransactionId uniqueidentifier);
            INSERT @Input SELECT AppliedChargeId,SupplierId,TransactionId,SupplierCreditId,CreditTransactionId
            FROM OPENJSON(@Rows) WITH(AppliedChargeId uniqueidentifier,SupplierId uniqueidentifier,
              TransactionId uniqueidentifier,SupplierCreditId uniqueidentifier,CreditTransactionId uniqueidentifier);
            DECLARE @ExpenseStatus TABLE(AppliedChargeId uniqueidentifier PRIMARY KEY,Status nvarchar(40));
            INSERT @ExpenseStatus
            SELECT input.AppliedChargeId,expense.Status FROM @Input input
            JOIN dbo.Expenses expense WITH(UPDLOCK,HOLDLOCK)
              ON expense.ExpenseId=input.AppliedChargeId AND expense.BusinessId=@BusinessId
             AND expense.SourceInvoiceId=@OriginalDocumentId AND expense.SupplierId=input.SupplierId;
            IF (SELECT COUNT(*) FROM @ExpenseStatus)<>@Count OR EXISTS(
              SELECT 1 FROM @ExpenseStatus WHERE Status NOT IN(N'Processed',N'Cancelled'))
              THROW 51607,'El estado de uno o más gastos del cargo cambió antes de procesar la devolución.',1;
            DECLARE @Effects TABLE(AppliedChargeId uniqueidentifier PRIMARY KEY,SupplierId uniqueidentifier,
              PayableId uniqueidentifier,OriginalAmount decimal(19,4),OutstandingAmount decimal(19,4),
              AvailableAmount decimal(19,4),
              PayableCredit decimal(19,4),SupplierCredit decimal(19,4),TransactionId uniqueidentifier,SupplierCreditId uniqueidentifier);
            INSERT @Effects
            SELECT input.AppliedChargeId,input.SupplierId,payable.PayableId,payable.OriginalAmount,payable.OutstandingAmount,available.AvailableAmount,
              CASE WHEN available.AvailableAmount<payable.OriginalAmount THEN available.AvailableAmount ELSE payable.OriginalAmount END,
              payable.OriginalAmount-CASE WHEN available.AvailableAmount<payable.OriginalAmount THEN available.AvailableAmount ELSE payable.OriginalAmount END,
              input.TransactionId,input.SupplierCreditId
            FROM @Input input
            JOIN @ExpenseStatus expense ON expense.AppliedChargeId=input.AppliedChargeId
              AND expense.Status=N'Processed'
            JOIN dbo.Payables payable WITH(UPDLOCK,HOLDLOCK)
              ON payable.BusinessId=@BusinessId AND payable.SourceDocumentId=input.AppliedChargeId
             AND payable.SourceDocumentType=N'Expense' AND payable.SupplierId=input.SupplierId
            CROSS APPLY accounting.ObligationAvailability(payable.BusinessId,N'Payable',payable.PayableId,payable.OutstandingAmount,NULL) available;
            IF (SELECT COUNT(*) FROM @Effects)<>(SELECT COUNT(*) FROM @ExpenseStatus WHERE Status=N'Processed')
              THROW 51607,'No fue posible localizar la cuenta por pagar de uno o más cargos.',1;
            IF EXISTS(SELECT 1 FROM @Effects WHERE AvailableAmount<0)
              THROW 51607,'Las obligaciones y sus reservas requieren conciliación antes de devolver los cargos.',1;
            UPDATE payable SET OutstandingAmount=payable.OutstandingAmount-effect.PayableCredit,
              Status=CASE WHEN effect.PayableCredit=effect.OriginalAmount AND effect.OutstandingAmount=effect.OriginalAmount
                THEN N'Cancelled' WHEN payable.OutstandingAmount-effect.PayableCredit=0 THEN N'Paid' ELSE N'PartiallyPaid' END
            FROM dbo.Payables payable JOIN @Effects effect ON effect.PayableId=payable.PayableId;
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT TransactionId,PayableId,N'Credit',PayableCredit,@ReturnId,@At,@Now FROM @Effects WHERE PayableCredit>0;
            INSERT @SupplierCreditOpenings(CreditId,TransactionId,SupplierId,PartySiteId,DocumentId,DocumentType,Nature,Currency,Amount)
            SELECT effect.SupplierCreditId,input.CreditTransactionId,effect.SupplierId,payable.PartySiteId,
              effect.AppliedChargeId,N'SalesReturnCharge',N'Credit',N'COP',effect.SupplierCredit
            FROM @Effects effect JOIN dbo.Payables payable ON payable.PayableId=effect.PayableId
            JOIN @Input input ON input.AppliedChargeId=effect.AppliedChargeId
            WHERE effect.SupplierCredit>0;
            """ + OpenSupplierCreditsSql + """
            INSERT dbo.SalesReturnChargeFinancialEffects(ReturnId,AppliedChargeId,PayableId,PayableCreditAmount,SupplierCreditAmount,CreatedAt)
            SELECT @ReturnId,AppliedChargeId,PayableId,PayableCredit,SupplierCredit,@Now FROM @Effects;
            """, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@OriginalDocumentId", value.OriginalDocumentId);
        command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        command.Parameters.AddWithValue("@CreditParentDocumentId", value.ReturnId);
        command.Parameters.AddWithValue("@CreditParentDocumentType", "SalesReturn");
        command.Parameters.AddWithValue("@CreditAt", value.ReturnedAt);
        command.Parameters.AddWithValue("@At", value.ReturnedAt);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Count", rows.Length);
        await command.ExecuteNonQueryAsync(token);
    }

    private Task ApplyGoodsReceiptFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        GoodsReceiptDocumentPayload value, CancellationToken token) =>
        value.CreatesPayable && value.Withholding.NetAmount -
            (value.FinancialDiscount?.AnticipatedInCost == true
                ? value.FinancialDiscount.FunctionalAmount : 0m) > 0
            ? OpenPayableAsync(connection, transaction, value.BusinessId, value.SupplierId,
                value.DocumentId, "GoodsReceipt", value.DocumentNumber, value.CurrencyCode,
                decimal.Round(value.GrandTotal - value.Withholding.WithholdingTotal / value.ExchangeRate -
                    (value.FinancialDiscount?.AnticipatedInCost == true
                        ? value.FinancialDiscount.Amount : 0m), 4),
                value.DueDate!.Value, value.SupplierInvoiceDate ?? value.ReceivedAt, token,
                value.Withholding.NetAmount -
                    (value.FinancialDiscount?.AnticipatedInCost == true
                        ? value.FinancialDiscount.FunctionalAmount : 0m),
                value.ExchangeRate, value.DocumentId, value.PartySiteId)
            : Task.CompletedTask;

    private Task ApplyGoodsReceiptCostDocumentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        GoodsReceiptCostDocumentAccountingPayload value, CancellationToken token)
    {
        var document = value.Document;
        return document.CreatesPayable && document.Withholding.NetAmount > 0
            ? OpenPayableAsync(connection, transaction, value.BusinessId, document.SupplierId,
                document.CostDocumentId, PurchasingDocumentTypes.GoodsReceiptCostDocument,
                document.DocumentNumber, document.CurrencyCode,
                decimal.Round(document.GrandTotal -
                    document.Withholding.WithholdingTotal / document.ExchangeRate, 4),
                document.DueDate!.Value, document.IssuedAt, token,
                document.Withholding.NetAmount, document.ExchangeRate, value.GoodsReceiptId, document.PartySiteId)
            : Task.CompletedTask;
    }

    private async Task ApplyExpenseFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        ExpenseDocumentPayload value, CancellationToken token)
    {
        if (value.Withholding.NetAmount > 0)
            await OpenPayableAsync(connection, transaction, value.BusinessId, value.SupplierId,
                value.ExpenseId, "Expense", value.DocumentNumber, value.CurrencyCode,
                value.Withholding.NetAmount, value.DueDate, value.IssuedAt, token,
                partySiteId: value.PartySiteId);
        await using var command = new SqlCommand("""
            UPDATE dbo.Expenses SET Status=N'Processed',ProcessedAt=@Now
            WHERE ExpenseId=@Id AND BusinessId=@BusinessId AND Status=N'Accepted';
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", value.ExpenseId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplyExpenseCancellationFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        ExpenseCancellationPayload value, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        if (value.PayableId is Guid payableId)
        {
            await using var payable = new SqlCommand(DeclarePortfolioBalanceChangesSql + """
                INSERT @PortfolioChanges(PositionId,Delta)
                SELECT PayableId,-@Credit FROM dbo.Payables WITH(UPDLOCK,HOLDLOCK)
                WHERE PayableId=@PayableId AND BusinessId=@BusinessId
                  AND SourceDocumentId=@ExpenseId AND SourceDocumentType=N'Expense'
                  AND OutstandingAmount=@Credit;
                IF @@ROWCOUNT<>1 THROW 51801,'The expense payable changed before cancellation.',1;
                """ + PortfolioBalanceChangesSql("Payables", "PayableId", PortfolioBalanceChangeKind.Cancellation) + """
                IF @Credit>0 INSERT dbo.PayableTransactions
                  (PayableTransactionId,PayableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@PayableId,N'Credit',@Credit,@CancellationId,@Now,@Now);
                """, connection, transaction);
            payable.Parameters.AddWithValue("@PayableId", payableId);
            payable.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            payable.Parameters.AddWithValue("@ExpenseId", value.Original.ExpenseId);
            payable.Parameters.AddWithValue("@CancellationId", value.CancellationId);
            payable.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(payable, "@Credit", value.PayableCredit);
            payable.Parameters.AddWithValue("@Now", now);
            await payable.ExecuteNonQueryAsync(token);
        }
        if (value.SupplierCredit > 0)
        {
            await using var credit = new SqlCommand(DeclareSupplierCreditOpeningsSql + """
                INSERT @SupplierCreditOpenings(CreditId,TransactionId,SupplierId,PartySiteId,
                  DocumentId,DocumentType,Nature,Currency,Amount)
                SELECT @Id,@CreditTransactionId,@SupplierId,payable.PartySiteId,@CancellationId,N'ExpenseCancellation',
                  N'Credit',@Currency,@Amount FROM dbo.Payables payable
                WHERE payable.BusinessId=@BusinessId AND payable.SourceDocumentId=@ExpenseId
                  AND payable.SourceDocumentType=N'Expense';
                IF @@ROWCOUNT<>1 THROW 51843,'The expense payable site is unavailable for credit.',1;
                """ + OpenSupplierCreditsSql, connection, transaction);
            credit.Parameters.AddWithValue("@Id", ids.NewId());
            credit.Parameters.AddWithValue("@CreditTransactionId", ids.NewId());
            credit.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            credit.Parameters.AddWithValue("@SupplierId", value.Original.SupplierId);
            credit.Parameters.AddWithValue("@CancellationId", value.CancellationId);
            credit.Parameters.AddWithValue("@ExpenseId", value.Original.ExpenseId);
            credit.Parameters.AddWithValue("@CreditParentDocumentId", value.CancellationId);
            credit.Parameters.AddWithValue("@CreditParentDocumentType", "ExpenseCancellation");
            credit.Parameters.AddWithValue("@CreditAt", value.CancelledAt);
            credit.Parameters.AddWithValue("@Currency", value.Original.CurrencyCode);
            AddMoney(credit, "@Amount", value.SupplierCredit);
            credit.Parameters.AddWithValue("@Now", now);
            await credit.ExecuteNonQueryAsync(token);
        }
        await using var expense = new SqlCommand("""
            UPDATE dbo.Expenses SET Status=N'Cancelled',CancelledAt=@Now
            WHERE ExpenseId=@ExpenseId AND BusinessId=@BusinessId
              AND Status=N'CancellationPending' AND CancellationId=@CancellationId;
            """, connection, transaction);
        expense.Parameters.AddWithValue("@ExpenseId", value.Original.ExpenseId);
        expense.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        expense.Parameters.AddWithValue("@CancellationId", value.CancellationId);
        expense.Parameters.AddWithValue("@Now", now);
        if (await expense.ExecuteNonQueryAsync(token) != 1)
            throw new DBConcurrencyException("The expense cancellation state changed before accounting.");
    }

    private Task OpenPayableAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid supplierId, Guid documentId, string documentType,
        string number, string currency, decimal amount, DateTimeOffset dueDate,
        DateTimeOffset occurredAt, CancellationToken token,
        decimal? functionalAmount = null, decimal exchangeRate = 1,
        Guid? parentGoodsReceiptId = null, Guid? partySiteId = null) =>
        OpenPayablesAsync(connection, transaction, businessId,
            [new(ids.NewId(), ids.NewId(), supplierId, partySiteId, documentId, documentType, null,
                number, currency, amount, functionalAmount ?? amount, exchangeRate, parentGoodsReceiptId, dueDate, occurredAt)], token);

    private sealed record PayableOpening(Guid PayableId, Guid TransactionId, Guid SupplierId, Guid? PartySiteId,
        Guid DocumentId, string DocumentType, Guid? SourceLineId, string Number, string Currency, decimal Amount,
        decimal FunctionalAmount, decimal ExchangeRate, Guid? ParentGoodsReceiptId, DateTimeOffset DueDate, DateTimeOffset OccurredAt);

    private async Task OpenPayablesAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyList<PayableOpening> openings, CancellationToken token)
    {
        if (openings.Count is < 1 or > 500)
            throw new InvalidOperationException("The payable opening batch is outside the source-line limit.");
        await using var command = new SqlCommand("""
            DECLARE @Openings TABLE(PayableId uniqueidentifier PRIMARY KEY,TransactionId uniqueidentifier,
              SupplierId uniqueidentifier,PartySiteId uniqueidentifier,DocumentId uniqueidentifier,DocumentType nvarchar(64),
              SourceLineId uniqueidentifier,Number nvarchar(80),Currency char(3),Amount decimal(19,4),FunctionalAmount decimal(19,4),
              ExchangeRate decimal(19,8),ParentGoodsReceiptId uniqueidentifier,DueDate datetimeoffset(7),OccurredAt datetimeoffset(7));
            INSERT @Openings SELECT * FROM OPENJSON(@Rows) WITH(PayableId uniqueidentifier,TransactionId uniqueidentifier,
              SupplierId uniqueidentifier,PartySiteId uniqueidentifier,DocumentId uniqueidentifier,DocumentType nvarchar(64),
              SourceLineId uniqueidentifier,Number nvarchar(80),Currency char(3),Amount decimal(19,4),FunctionalAmount decimal(19,4),
              ExchangeRate decimal(19,8),ParentGoodsReceiptId uniqueidentifier,DueDate datetimeoffset(7),OccurredAt datetimeoffset(7));
            IF @@ROWCOUNT<>@Count THROW 51842,'The payable opening batch is incomplete.',1;
            INSERT dbo.Payables
              (PayableId,BusinessId,SupplierId,PartySiteId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,FunctionalOriginalAmount,
               ExchangeRate,ParentGoodsReceiptId,DueDate,Status,CreatedAt,SourceLineId)
            SELECT opening.PayableId,@BusinessId,opening.SupplierId,
               site.PartySiteId,opening.DocumentId,opening.DocumentType,
               opening.Number,opening.Currency,opening.Amount,opening.Amount,opening.FunctionalAmount,opening.ExchangeRate,
               opening.ParentGoodsReceiptId,opening.DueDate,N'Open',@Now,opening.SourceLineId
            FROM @Openings opening JOIN dbo.Suppliers supplier ON supplier.SupplierId=opening.SupplierId
            JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
              AND business.TenantId=supplier.TenantId
            CROSS APPLY (SELECT TOP(1) chosen.PartySiteId FROM dbo.PartySites chosen
              WHERE chosen.PartyId=supplier.PartyId AND
                (opening.PartySiteId IS NOT NULL AND chosen.PartySiteId=opening.PartySiteId OR
                 opening.PartySiteId IS NULL AND chosen.IsActive=1)
              ORDER BY chosen.IsPrimary DESC,chosen.PartySiteId) site;
            IF @@ROWCOUNT<>@Count THROW 51842,'Supplier site is missing or invalid.',1;
            INSERT dbo.PayableTransactions
              (PayableTransactionId,PayableId,TransactionType,Amount,
               SourceDocumentId,OccurredAt,CreatedAt)
            SELECT TransactionId,PayableId,N'Opening',Amount,DocumentId,OccurredAt,@Now FROM @Openings;
            IF @@ROWCOUNT<>@Count THROW 51842,'The payable opening transactions are incomplete.',1;
            """, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(openings);
        command.Parameters.AddWithValue("@Count", openings.Count);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task ApplyPurchaseReturnFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        PurchaseReturnDocumentPayload value, CancellationToken token)
    {
        var settlementAmount = PurchaseReturnSupplierSettlement(value);
        Guid? payableId = null;
        Guid? partySiteId = null;
        decimal availableAmount = 0;
        await using (var load = new SqlCommand("""
            SELECT payable.PayableId,
              COALESCE(payable.PartySiteId,receipt.PartySiteId,primarySite.PartySiteId),available.AvailableAmount
            FROM dbo.GoodsReceipts receipt
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=receipt.SupplierId
            OUTER APPLY (SELECT TOP(1) site.PartySiteId FROM dbo.PartySites site
              WHERE site.PartyId=supplier.PartyId AND site.IsActive=1
              ORDER BY site.IsPrimary DESC,site.PartySiteId) primarySite
            LEFT JOIN dbo.Payables payable WITH(UPDLOCK,HOLDLOCK)
              ON payable.BusinessId=receipt.BusinessId AND payable.SourceDocumentId=receipt.GoodsReceiptId
              AND payable.SourceDocumentType=N'GoodsReceipt'
            CROSS APPLY accounting.ObligationAvailability(payable.BusinessId,N'Payable',payable.PayableId,payable.OutstandingAmount,NULL) available
            WHERE receipt.BusinessId=@BusinessId AND receipt.GoodsReceiptId=@OriginalId;
            """, connection, transaction))
        {
            load.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            load.Parameters.AddWithValue("@OriginalId", value.OriginalGoodsReceiptId);
            await using var reader = await load.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                payableId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
                partySiteId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
                availableAmount = reader.IsDBNull(2) && payableId is null ? 0 : reader.GetDecimal(2);
            }
        }
        if (availableAmount < 0)
            throw new InvalidOperationException("La cuenta por pagar y sus reservas requieren conciliación antes de aplicar la devolución.");
        var payableCredit = decimal.Min(settlementAmount, availableAmount);
        var supplierCredit = settlementAmount - payableCredit;
        var now = timeProvider.GetUtcNow();
        if (payableId is not null && payableCredit > 0)
        {
            await using var apply = new SqlCommand(DeclarePortfolioBalanceChangesSql + """
                INSERT @PortfolioChanges VALUES(@PayableId,-@Amount);
                """ + PortfolioBalanceChangesSql("Payables", "PayableId", PortfolioBalanceChangeKind.Settlement) + """
                INSERT dbo.PayableTransactions
                  (PayableTransactionId,PayableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@PayableId,N'Credit',@Amount,@ReturnId,@At,@Now);
                SELECT @@ROWCOUNT;
                """, connection, transaction);
            apply.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            apply.Parameters.AddWithValue("@PayableId", payableId.Value);
            apply.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(apply, "@Amount", payableCredit);
            apply.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            apply.Parameters.AddWithValue("@At", value.ReturnedAt);
            apply.Parameters.AddWithValue("@Now", now);
            if (await apply.ExecuteScalarAsync(token) is not int written || written != 1)
                throw new DBConcurrencyException("The purchase return transaction was not persisted.");
        }
        if (supplierCredit > 0)
        {
            await using var credit = new SqlCommand(DeclareSupplierCreditOpeningsSql + """
                INSERT @SupplierCreditOpenings(CreditId,TransactionId,SupplierId,PartySiteId,PurchaseReturnId,Nature,Currency,Amount)
                VALUES(@Id,@CreditTransactionId,@SupplierId,@PartySiteId,@ReturnId,N'Credit',@Currency,@Amount);
                """ + OpenSupplierCreditsSql, connection, transaction);
            credit.Parameters.AddWithValue("@Id", ids.NewId());
            credit.Parameters.AddWithValue("@CreditTransactionId", ids.NewId());
            credit.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            credit.Parameters.AddWithValue("@SupplierId", value.SupplierId);
            credit.Parameters.AddWithValue("@PartySiteId", (object?)partySiteId ?? DBNull.Value);
            credit.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            credit.Parameters.AddWithValue("@CreditParentDocumentId", value.ReturnId);
            credit.Parameters.AddWithValue("@CreditParentDocumentType", "PurchaseReturn");
            credit.Parameters.AddWithValue("@CreditAt", value.ReturnedAt);
            credit.Parameters.AddWithValue("@Currency", value.CurrencyCode);
            AddMoney(credit, "@Amount", supplierCredit);
            credit.Parameters.AddWithValue("@Now", now);
            await credit.ExecuteNonQueryAsync(token);
        }
        await using var effect = new SqlCommand("""
            INSERT dbo.PurchaseReturnFinancialEffects
              (PurchaseReturnId,PayableId,PayableCreditAmount,SupplierCreditAmount,CreatedAt)
            VALUES(@ReturnId,@PayableId,@PayableCredit,@SupplierCredit,@Now);
            """, connection, transaction);
        effect.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        effect.Parameters.AddWithValue("@PayableId", (object?)payableId ?? DBNull.Value);
        AddMoney(effect, "@PayableCredit", payableCredit);
        AddMoney(effect, "@SupplierCredit", supplierCredit);
        effect.Parameters.AddWithValue("@Now", now);
        await effect.ExecuteNonQueryAsync(token);
    }

    private static decimal PurchaseReturnSupplierSettlement(PurchaseReturnDocumentPayload value)
    {
        if (value.Lines is null || value.Lines.Count == 0 ||
            value.Lines.Any(line => line.FinancialDiscountAmount < 0 ||
                line.FinancialDiscountAmount > line.NetAmount ||
                line.FinancialDiscountAmount + line.FinancialDiscountAdjustment < 0 ||
                line.FinancialDiscountAmount + line.FinancialDiscountAdjustment > line.NetAmount))
            throw new InvalidOperationException("The returned financial discount is invalid.");
        var amount = value.TotalAmount - value.Lines.Sum(line =>
            line.FinancialDiscountAmount + line.FinancialDiscountAdjustment);
        if (amount <= 0)
            throw new InvalidOperationException("The returned purchase has no net supplier settlement.");
        return amount;
    }

    private async Task ApplyPayablePaymentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        SupplierPaymentDocumentPayload payment, CancellationToken token)
    {
        await ApplyPortfolioPaymentsAsync(connection, transaction, payment.BusinessId, AccountingSubledgerKinds.Payable,
            [new(payment.PaymentId, payment.SupplierId, null, payment.WorkSessionId,
                payment.PaidAt, payment.ConfirmedByUserId, payment.CurrencyCode,
                payment.Allocations.Select(item => new PortfolioAllocation(item.LineNumber, item.PayableId, item.Amount)).ToArray(),
                payment.Payments.Select(item => new PortfolioTender(item.LineNumber, item.MethodCode, item.Amount, item.Reference)).ToArray())], token);
        if (payment.Allocations.Any(item => item.FinancialDiscountEarned is not null))
            await PrepareSupplierDiscountPricesAsync(connection, transaction, payment, token);
    }

    private Task ApplyReceivablePaymentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        CustomerPaymentDocumentPayload payment, CancellationToken token) =>
        ApplyPortfolioPaymentsAsync(connection, transaction, payment.BusinessId, AccountingSubledgerKinds.Receivable,
            [new(payment.PaymentId, payment.CustomerId, null, payment.WorkSessionId,
                payment.PaidAt, payment.ConfirmedByUserId, payment.CurrencyCode,
                payment.Allocations.Select(item => new PortfolioAllocation(item.LineNumber, item.ReceivableId, item.Amount)).ToArray(),
                payment.Payments.Select(item => new PortfolioTender(item.LineNumber, item.MethodCode, item.Amount, item.Reference)).ToArray())], token);

    private async Task ApplyCashMovementFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        CashMovementDocumentPayload movement, CancellationToken token)
    {
        var cashIn = movement.Direction == CashMovementDirections.In;
        await InsertSessionMovementAsync(connection, transaction,
            movement.WorkSessionId, movement.DocumentId,
            cashIn ? "CashIn" : "CashOut", "Cash",
            cashIn ? movement.Amount : -movement.Amount,
            movement.Reference ?? movement.DocumentNumber, movement.OccurredAt,
            movement.ConfirmedByUserId, $"cash-movement:{movement.DocumentId:N}", token);
        await using var complete = new SqlCommand("""
            UPDATE dbo.CashMovementDocuments SET Status=N'Processed',ProcessedAt=@Now
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND Status=N'Accepted';
            """, connection, transaction);
        complete.Parameters.AddWithValue("@DocumentId", movement.DocumentId);
        complete.Parameters.AddWithValue("@BusinessId", movement.BusinessId);
        complete.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await complete.ExecuteNonQueryAsync(token) != 1)
            throw new DBConcurrencyException("The cash movement could not be completed.");
    }

    private Task InsertSessionMovementAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid sessionId, Guid? documentId, string movementType, string method,
        decimal amount, string? reference, DateTimeOffset occurredAt, Guid userId,
        string sourceKey, CancellationToken token) =>
        InsertSessionMovementsAsync(connection, transaction,
            [new(ids.NewId(), sessionId, documentId, movementType, method, amount,
                reference, occurredAt, userId, sourceKey)], token);

    private sealed record SessionMovementWrite(Guid Id, Guid SessionId, Guid? DocumentId,
        string MovementType, string Method, decimal Amount, string? Reference,
        DateTimeOffset OccurredAt, Guid UserId, string SourceKey);

    private static async Task InsertSessionMovementsAsync(
        SqlConnection connection, SqlTransaction transaction,
        IReadOnlyList<SessionMovementWrite> movements, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
               MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
            SELECT Id,SessionId,DocumentId,NULL,CONVERT(date,OccurredAt),MovementType,Method,Amount,
              Reference,SourceKey,OccurredAt,UserId
            FROM OPENJSON(@Movements) WITH(Id uniqueidentifier,SessionId uniqueidentifier,DocumentId uniqueidentifier,
              MovementType nvarchar(32),Method nvarchar(32),Amount decimal(19,4),Reference nvarchar(160),
              SourceKey nvarchar(160),OccurredAt datetimeoffset(7),UserId uniqueidentifier);
            """, connection, transaction);
        command.Parameters.Add("@Movements", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(movements);
        if (await command.ExecuteNonQueryAsync(token) != movements.Count)
            throw new DBConcurrencyException("The work-session movements were not recorded atomically.");
    }

}
