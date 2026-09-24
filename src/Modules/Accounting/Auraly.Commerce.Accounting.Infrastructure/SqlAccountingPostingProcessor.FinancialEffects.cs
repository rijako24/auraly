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
    private async Task ApplyFinancialEffectsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken)
    {
        Guid? affectedCustomerId = null;
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
        }

        if (affectedCustomerId is Guid customerId)
            await SqlAccountingPosSynchronizationOutbox.InsertCustomerAsync(
                connection, transaction, source.BusinessId, customerId,
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

        await using var receivable = new SqlCommand("""
            INSERT dbo.Receivables
              (ReceivableId,BusinessId,CustomerId,PartySiteId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES(@ReceivableId,@BusinessId,@CustomerId,@PartySiteId,@DocumentId,N'ServiceInvoice',
               @Number,N'COP',@Amount,@Amount,@DueDate,N'Open',@Now);
            INSERT dbo.ReceivableTransactions
              (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
               SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@ReceivableId,N'Opening',@Amount,
               @DocumentId,@OccurredAt,@Now);
            """, connection, transaction);
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
        if (await receivable.ExecuteNonQueryAsync(token) != 2)
            throw new DBConcurrencyException(
                "The service-invoice receivable was not opened atomically.");
    }

    private async Task ApplySalesDebitNoteFinancialEffectsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SalesDebitNoteDocumentPayload value,
        CancellationToken token)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.Receivables
              (ReceivableId,BusinessId,CustomerId,PartySiteId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            SELECT @ReceivableId,@BusinessId,@CustomerId,document.CustomerPartySiteId,@DocumentId,N'SalesDebitNote',
               @Number,N'COP',@Amount,@Amount,@DueDate,N'Open',@Now
            FROM dbo.SalesDocuments document
            WHERE document.DocumentId=@OriginalDocumentId AND document.BusinessId=@BusinessId;
            INSERT dbo.ReceivableTransactions
              (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
               SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@ReceivableId,N'Opening',@Amount,@DocumentId,@OccurredAt,@Now);
            """, connection, transaction);
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
        if (await command.ExecuteNonQueryAsync(token) != 2)
            throw new DBConcurrencyException("The debit-note receivable was not opened atomically.");
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
        var sql = isReceivable
            ? """
              UPDATE dbo.Receivables
              SET OutstandingAmount=OutstandingAmount+@Delta,
                  Status=CASE WHEN OutstandingAmount+@Delta=0 THEN N'Paid'
                              WHEN OutstandingAmount+@Delta>=OriginalAmount THEN N'Open'
                              ELSE N'PartiallyPaid' END
              WHERE ReceivableId=@SubledgerId AND BusinessId=@BusinessId
                AND Status<>N'Cancelled' AND OutstandingAmount+@Delta>=0;
              IF @@ROWCOUNT<>1 THROW 51000,'The receivable adjustment is no longer valid.',1;
              INSERT dbo.ReceivableTransactions
                (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                 SourceDocumentId,OccurredAt,CreatedAt)
              VALUES(@TransactionId,@SubledgerId,N'Adjustment',@Delta,
                     @DocumentId,@OccurredAt,@Now);
              """
            : """
              UPDATE dbo.Payables
              SET OutstandingAmount=OutstandingAmount+@Delta,
                  Status=CASE WHEN OutstandingAmount+@Delta=0 THEN N'Paid'
                              WHEN OutstandingAmount+@Delta>=OriginalAmount THEN N'Open'
                              ELSE N'PartiallyPaid' END
              WHERE PayableId=@SubledgerId AND BusinessId=@BusinessId
                AND Status<>N'Cancelled' AND OutstandingAmount+@Delta>=0;
              IF @@ROWCOUNT<>1 THROW 51000,'The payable adjustment is no longer valid.',1;
              INSERT dbo.PayableTransactions
                (PayableTransactionId,PayableId,TransactionType,Amount,
                 SourceDocumentId,OccurredAt,CreatedAt)
              VALUES(@TransactionId,@SubledgerId,N'Adjustment',@Delta,
                     @DocumentId,@OccurredAt,@Now);
              """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SubledgerId", request.SubledgerId);
        command.Parameters.AddWithValue("@BusinessId", source.BusinessId);
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", source.DocumentId);
        command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
        command.Parameters.AddWithValue("@Now", now);
        AddMoney(command, "@Delta", delta);
        if (await command.ExecuteNonQueryAsync(token) != 2)
            throw new DBConcurrencyException(
                "The account adjustment was not applied atomically.");
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
            await using var receivable = new SqlCommand("""
                INSERT dbo.Receivables
                  (ReceivableId,BusinessId,CustomerId,PartySiteId,SourceDocumentId,SourceDocumentType,
                   DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                VALUES(@ReceivableId,@BusinessId,@CustomerId,@PartySiteId,@DocumentId,@DocumentType,
                   @Number,N'COP',@Amount,@Amount,@DueDate,N'Open',@Now);
                INSERT dbo.ReceivableTransactions
                  (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@ReceivableId,N'Opening',@Amount,
                   @DocumentId,@OccurredAt,@Now);
                """, connection, transaction);
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
            if (await receivable.ExecuteNonQueryAsync(token) != 2)
                throw new DBConcurrencyException("The receivable was not opened atomically.");
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
        decimal outstanding = 0;
        await using (var load = new SqlCommand("""
            SELECT TOP(1) receivable.ReceivableId,receivable.OutstandingAmount
            FROM dbo.Receivables receivable WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.SalesDocuments sale
              ON sale.DocumentId=receivable.SourceDocumentId
             AND sale.BusinessId=receivable.BusinessId
             AND sale.DocumentType=receivable.SourceDocumentType
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
                outstanding = reader.GetDecimal(1);
            }
        }
        var applied = decimal.Min(value.TotalAmount, outstanding);
        var credit = value.TotalAmount - applied;
        var now = timeProvider.GetUtcNow();
        if (receivableId is not null && applied > 0)
        {
            var after = outstanding - applied;
            await using var apply = new SqlCommand("""
                UPDATE dbo.Receivables SET OutstandingAmount=@After,
                  Status=CASE WHEN @After=0 THEN N'Paid' ELSE N'PartiallyPaid' END
                WHERE ReceivableId=@ReceivableId;
                INSERT dbo.ReceivableTransactions
                  (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@ReceivableId,N'Reversal',@Amount,@ReturnId,@At,@Now);
                INSERT dbo.SalesReturnReceivableApplications
                  (ReturnId,ReceivableId,Amount,AppliedAt)
                VALUES(@ReturnId,@ReceivableId,@Amount,@Now);
                """, connection, transaction);
            apply.Parameters.AddWithValue("@After", after);
            apply.Parameters.AddWithValue("@ReceivableId", receivableId.Value);
            apply.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(apply, "@Amount", applied);
            apply.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            apply.Parameters.AddWithValue("@At", value.ReturnedAt);
            apply.Parameters.AddWithValue("@Now", now);
            if (await apply.ExecuteNonQueryAsync(token) != 3)
                throw new DBConcurrencyException("The return was not applied to its receivable.");
        }
        if (credit > 0)
        {
            await using var createCredit = new SqlCommand("""
                INSERT dbo.CustomerCredits
                  (CustomerCreditId,BusinessId,CustomerId,SourceReturnId,
                   OriginalAmount,AvailableAmount,Status,CreatedAt)
                VALUES(@Id,@BusinessId,@CustomerId,@ReturnId,@Amount,@Amount,N'Open',@Now);
                """, connection, transaction);
            createCredit.Parameters.AddWithValue("@Id", ids.NewId());
            createCredit.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            createCredit.Parameters.AddWithValue("@CustomerId", value.CustomerId.Value);
            createCredit.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            AddMoney(createCredit, "@Amount", credit);
            createCredit.Parameters.AddWithValue("@Now", now);
            await createCredit.ExecuteNonQueryAsync(token);
        }
    }

    private async Task ApplyPreexistingReceivableAsync(SqlConnection connection,SqlTransaction transaction,
        PreexistingReceivablePayload value,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            INSERT dbo.Receivables(ReceivableId,BusinessId,CustomerId,PartySiteId,SourceDocumentId,
              SourceDocumentType,DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES(@Id,@BusinessId,@CustomerId,@PartySiteId,@Id,N'PreexistingReceivable',@Number,N'COP',
              @Amount,@Amount,@DueDate,N'Open',@Now);
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,TransactionType,Amount,
              SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@Id,N'Opening',@Amount,@Id,@IssuedAt,@Now);
            """,connection,transaction);
        command.Parameters.AddWithValue("@Id",value.ReceivableId);command.Parameters.AddWithValue("@TransactionId",ids.NewId());command.Parameters.AddWithValue("@BusinessId",value.BusinessId);command.Parameters.AddWithValue("@CustomerId",value.CustomerId);command.Parameters.AddWithValue("@PartySiteId",(object?)value.PartySiteId??DBNull.Value);command.Parameters.AddWithValue("@Number",value.DocumentNumber);AddMoney(command,"@Amount",value.Amount);command.Parameters.AddWithValue("@DueDate",value.DueDate);command.Parameters.AddWithValue("@IssuedAt",value.IssuedAt);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        if(await command.ExecuteNonQueryAsync(token)!=2)throw new DBConcurrencyException("The preexisting receivable was not opened atomically.");
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
            SupplierCreditId = ids.NewId()
        }).ToArray();
        await using var command = new SqlCommand("""
            DECLARE @Input TABLE(AppliedChargeId uniqueidentifier PRIMARY KEY,SupplierId uniqueidentifier,
              TransactionId uniqueidentifier,SupplierCreditId uniqueidentifier);
            INSERT @Input SELECT AppliedChargeId,SupplierId,TransactionId,SupplierCreditId
            FROM OPENJSON(@Rows) WITH(AppliedChargeId uniqueidentifier,SupplierId uniqueidentifier,
              TransactionId uniqueidentifier,SupplierCreditId uniqueidentifier);
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
              PayableCredit decimal(19,4),SupplierCredit decimal(19,4),TransactionId uniqueidentifier,SupplierCreditId uniqueidentifier);
            INSERT @Effects
            SELECT input.AppliedChargeId,input.SupplierId,payable.PayableId,payable.OriginalAmount,payable.OutstandingAmount,
              CASE WHEN payable.OutstandingAmount<payable.OriginalAmount THEN payable.OutstandingAmount ELSE payable.OriginalAmount END,
              payable.OriginalAmount-CASE WHEN payable.OutstandingAmount<payable.OriginalAmount THEN payable.OutstandingAmount ELSE payable.OriginalAmount END,
              input.TransactionId,input.SupplierCreditId
            FROM @Input input
            JOIN @ExpenseStatus expense ON expense.AppliedChargeId=input.AppliedChargeId
              AND expense.Status=N'Processed'
            JOIN dbo.Payables payable WITH(UPDLOCK,HOLDLOCK)
              ON payable.BusinessId=@BusinessId AND payable.SourceDocumentId=input.AppliedChargeId
             AND payable.SourceDocumentType=N'Expense' AND payable.SupplierId=input.SupplierId;
            IF (SELECT COUNT(*) FROM @Effects)<>(SELECT COUNT(*) FROM @ExpenseStatus WHERE Status=N'Processed')
              THROW 51607,'No fue posible localizar la cuenta por pagar de uno o más cargos.',1;
            UPDATE payable SET OutstandingAmount=payable.OutstandingAmount-effect.PayableCredit,
              Status=CASE WHEN effect.PayableCredit=effect.OriginalAmount AND effect.OutstandingAmount=effect.OriginalAmount
                THEN N'Cancelled' WHEN payable.OutstandingAmount-effect.PayableCredit=0 THEN N'Paid' ELSE N'PartiallyPaid' END
            FROM dbo.Payables payable JOIN @Effects effect ON effect.PayableId=payable.PayableId;
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT TransactionId,PayableId,N'Credit',PayableCredit,@ReturnId,@At,@Now FROM @Effects WHERE PayableCredit>0;
            INSERT dbo.SupplierCredits(SupplierCreditId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
              OriginalAmount,AvailableAmount,Status,CreatedAt)
            SELECT SupplierCreditId,@BusinessId,SupplierId,AppliedChargeId,N'SalesReturnCharge',
              SupplierCredit,SupplierCredit,N'Open',@Now FROM @Effects WHERE SupplierCredit>0;
            INSERT dbo.SalesReturnChargeFinancialEffects(ReturnId,AppliedChargeId,PayableId,PayableCreditAmount,SupplierCreditAmount,CreatedAt)
            SELECT @ReturnId,AppliedChargeId,PayableId,PayableCredit,SupplierCredit,@Now FROM @Effects;
            """, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@OriginalDocumentId", value.OriginalDocumentId);
        command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        command.Parameters.AddWithValue("@At", value.ReturnedAt);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Count", rows.Length);
        await command.ExecuteNonQueryAsync(token);
    }

    private Task ApplyGoodsReceiptFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        GoodsReceiptDocumentPayload value, CancellationToken token) =>
        value.CreatesPayable && value.Withholding.NetAmount > 0
            ? OpenPayableAsync(connection, transaction, value.BusinessId, value.SupplierId,
                value.DocumentId, "GoodsReceipt", value.DocumentNumber, value.CurrencyCode,
                decimal.Round(value.GrandTotal - value.Withholding.WithholdingTotal / value.ExchangeRate, 4),
                value.DueDate!.Value, value.SupplierInvoiceDate ?? value.ReceivedAt, token,
                value.Withholding.NetAmount, value.ExchangeRate, value.DocumentId)
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
                document.Withholding.NetAmount, document.ExchangeRate, value.GoodsReceiptId)
            : Task.CompletedTask;
    }

    private async Task ApplyExpenseFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        ExpenseDocumentPayload value, CancellationToken token)
    {
        if (value.Withholding.NetAmount > 0)
            await OpenPayableAsync(connection, transaction, value.BusinessId, value.SupplierId,
                value.ExpenseId, "Expense", value.DocumentNumber, value.CurrencyCode,
                value.Withholding.NetAmount, value.DueDate, value.IssuedAt, token);
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
            await using var payable = new SqlCommand("""
                UPDATE dbo.Payables SET OutstandingAmount=0,
                  FunctionalOutstandingAmount=0,
                  Status=CASE WHEN @Credit=OriginalAmount THEN N'Cancelled' ELSE N'Paid' END
                WHERE PayableId=@PayableId AND BusinessId=@BusinessId
                  AND SourceDocumentId=@ExpenseId AND SourceDocumentType=N'Expense'
                  AND OutstandingAmount=@Credit;
                IF @@ROWCOUNT<>1 THROW 51801,'The expense payable changed before cancellation.',1;
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
            await using var credit = new SqlCommand("""
                INSERT dbo.SupplierCredits(SupplierCreditId,BusinessId,SupplierId,
                  SourceDocumentId,SourceDocumentType,OriginalAmount,AvailableAmount,Status,CreatedAt)
                VALUES(@Id,@BusinessId,@SupplierId,@CancellationId,N'ExpenseCancellation',
                  @Amount,@Amount,N'Open',@Now);
                """, connection, transaction);
            credit.Parameters.AddWithValue("@Id", ids.NewId());
            credit.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            credit.Parameters.AddWithValue("@SupplierId", value.Original.SupplierId);
            credit.Parameters.AddWithValue("@CancellationId", value.CancellationId);
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

    private async Task OpenPayableAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid supplierId, Guid documentId, string documentType,
        string number, string currency, decimal amount, DateTimeOffset dueDate,
        DateTimeOffset occurredAt, CancellationToken token,
        decimal? functionalAmount = null, decimal exchangeRate = 1,
        Guid? parentGoodsReceiptId = null)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.Payables
              (PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,FunctionalOriginalAmount,
               FunctionalOutstandingAmount,ExchangeRate,ParentGoodsReceiptId,DueDate,Status,CreatedAt)
            VALUES(@PayableId,@BusinessId,@SupplierId,@DocumentId,@DocumentType,
               @Number,@Currency,@Amount,@Amount,@FunctionalAmount,@FunctionalAmount,@ExchangeRate,
               @ParentGoodsReceiptId,@DueDate,N'Open',@Now);
            INSERT dbo.PayableTransactions
              (PayableTransactionId,PayableId,TransactionType,Amount,
               SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@PayableId,N'Opening',@Amount,@DocumentId,@At,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@PayableId", ids.NewId());
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SupplierId", supplierId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Number", number);
        command.Parameters.AddWithValue("@Currency", currency);
        AddMoney(command, "@Amount", amount);
        AddMoney(command, "@FunctionalAmount", functionalAmount ?? amount);
        var rate = command.Parameters.Add("@ExchangeRate", SqlDbType.Decimal);
        rate.Precision = 19;
        rate.Scale = 8;
        rate.Value = exchangeRate;
        command.Parameters.AddWithValue("@ParentGoodsReceiptId", (object?)parentGoodsReceiptId ?? DBNull.Value);
        command.Parameters.AddWithValue("@DueDate", dueDate);
        command.Parameters.AddWithValue("@At", occurredAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(token) != 2)
            throw new DBConcurrencyException("The payable was not opened atomically.");
    }

    private async Task ApplyPurchaseReturnFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        PurchaseReturnDocumentPayload value, CancellationToken token)
    {
        Guid? payableId = null;
        decimal outstanding = 0;
        await using (var load = new SqlCommand("""
            SELECT PayableId,OutstandingAmount FROM dbo.Payables WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND SourceDocumentId=@OriginalId
              AND SourceDocumentType=N'GoodsReceipt';
            """, connection, transaction))
        {
            load.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            load.Parameters.AddWithValue("@OriginalId", value.OriginalGoodsReceiptId);
            await using var reader = await load.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                payableId = reader.GetGuid(0);
                outstanding = reader.GetDecimal(1);
            }
        }
        var payableCredit = decimal.Min(value.TotalAmount, outstanding);
        var supplierCredit = value.TotalAmount - payableCredit;
        var now = timeProvider.GetUtcNow();
        if (payableId is not null && payableCredit > 0)
        {
            await using var apply = new SqlCommand("""
                UPDATE dbo.Payables SET OutstandingAmount=@After,
                  Status=CASE WHEN @After=0 THEN N'Paid' ELSE N'PartiallyPaid' END
                WHERE PayableId=@PayableId;
                INSERT dbo.PayableTransactions
                  (PayableTransactionId,PayableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@PayableId,N'Credit',@Amount,@ReturnId,@At,@Now);
                """, connection, transaction);
            apply.Parameters.AddWithValue("@After", outstanding - payableCredit);
            apply.Parameters.AddWithValue("@PayableId", payableId.Value);
            apply.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(apply, "@Amount", payableCredit);
            apply.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            apply.Parameters.AddWithValue("@At", value.ReturnedAt);
            apply.Parameters.AddWithValue("@Now", now);
            await apply.ExecuteNonQueryAsync(token);
        }
        if (supplierCredit > 0)
        {
            await using var credit = new SqlCommand("""
                INSERT dbo.SupplierCredits
                  (SupplierCreditId,BusinessId,SupplierId,SourcePurchaseReturnId,
                   OriginalAmount,AvailableAmount,Status,CreatedAt)
                VALUES(@Id,@BusinessId,@SupplierId,@ReturnId,@Amount,@Amount,N'Open',@Now);
                """, connection, transaction);
            credit.Parameters.AddWithValue("@Id", ids.NewId());
            credit.Parameters.AddWithValue("@BusinessId", value.BusinessId);
            credit.Parameters.AddWithValue("@SupplierId", value.SupplierId);
            credit.Parameters.AddWithValue("@ReturnId", value.ReturnId);
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

    private async Task ApplyPayablePaymentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        SupplierPaymentDocumentPayload payment, CancellationToken token)
    {
        await using (var apply = new SqlCommand("""
            DECLARE @Allocations TABLE(LineNumber int UNIQUE,PayableId uniqueidentifier PRIMARY KEY,
              Amount decimal(19,4),TransactionId uniqueidentifier);
            INSERT @Allocations SELECT LineNumber,PayableId,Amount,TransactionId FROM OPENJSON(@AllocationsJson)
              WITH(LineNumber int,PayableId uniqueidentifier,Amount decimal(19,4),TransactionId uniqueidentifier);
            DECLARE @Ready TABLE(LineNumber int,PayableId uniqueidentifier PRIMARY KEY,
              Amount decimal(19,4),BalanceAfter decimal(19,4),TransactionId uniqueidentifier);
            INSERT @Ready SELECT input.LineNumber,input.PayableId,input.Amount,
              balance.OutstandingAmount-input.Amount,input.TransactionId
            FROM @Allocations input JOIN dbo.Payables balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.PayableId=input.PayableId AND balance.BusinessId=@BusinessId
              AND balance.SupplierId=@SupplierId AND balance.CurrencyCode=@Currency
              AND balance.Status IN(N'Open',N'PartiallyPaid') AND balance.OutstandingAmount>=input.Amount
            JOIN dbo.SupplierPaymentApplications application WITH(UPDLOCK,HOLDLOCK)
              ON application.PayableId=input.PayableId AND application.PaymentId=@PaymentId
              AND application.LineNumber=input.LineNumber AND application.Amount=input.Amount AND application.AppliedAt IS NULL;
            IF @@ROWCOUNT<>@AllocationCount THROW 51607,'The payment allocations are no longer valid.',1;
            UPDATE balance SET OutstandingAmount=ready.BalanceAfter,
              Status=CASE WHEN ready.BalanceAfter=0 THEN N'Paid' ELSE N'PartiallyPaid' END
            FROM dbo.Payables balance JOIN @Ready ready ON ready.PayableId=balance.PayableId;
            IF @@ROWCOUNT<>@AllocationCount THROW 51607,'The payment balances were not updated atomically.',1;
            UPDATE application SET AppliedAt=@Now FROM dbo.SupplierPaymentApplications application
              JOIN @Ready ready ON ready.LineNumber=application.LineNumber AND ready.PayableId=application.PayableId
              WHERE application.PaymentId=@PaymentId AND application.AppliedAt IS NULL;
            IF @@ROWCOUNT<>@AllocationCount THROW 51607,'The payment applications were not updated atomically.',1;
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,
              SourceDocumentId,OccurredAt,CreatedAt)
            SELECT TransactionId,PayableId,N'Payment',Amount,@PaymentId,@At,@Now FROM @Ready;
            """, connection, transaction))
        {
            apply.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
            apply.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
            apply.Parameters.AddWithValue("@SupplierId", payment.SupplierId);
            apply.Parameters.AddWithValue("@Currency", payment.CurrencyCode);
            apply.Parameters.AddWithValue("@AllocationCount", payment.Allocations.Count);
            apply.Parameters.AddWithValue("@At", payment.PaidAt);
            apply.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            apply.Parameters.Add("@AllocationsJson", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(
                payment.Allocations.Select(allocation => new { allocation.LineNumber,allocation.PayableId,allocation.Amount,
                    TransactionId = ids.NewId() }));
            await apply.ExecuteNonQueryAsync(token);
        }
        await CompleteSupplierPaymentAsync(connection, transaction, payment, token);
    }

    private async Task CompleteSupplierPaymentAsync(
        SqlConnection connection, SqlTransaction transaction,
        SupplierPaymentDocumentPayload payment, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        await using var complete = new SqlCommand("""
            UPDATE dbo.SupplierPayments SET Status=N'Processed',ProcessedAt=@Now
            WHERE PaymentId=@PaymentId AND BusinessId=@BusinessId AND Status=N'Accepted'
              AND NOT EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications
                             WHERE PaymentId=@PaymentId AND AppliedAt IS NULL);
            """, connection, transaction);
        complete.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
        complete.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
        complete.Parameters.AddWithValue("@Now", now);
        if (await complete.ExecuteNonQueryAsync(token) != 1)
            throw new DBConcurrencyException("The supplier payment could not be completed.");
        if (payment.WorkSessionId is null) return;
        await InsertPaymentSessionMovementsAsync(connection, transaction,
            payment.WorkSessionId.Value, payment.PaymentId, "PayablePayment",
            payment.Payments.Select(item => (item.LineNumber, item.MethodCode, -item.Amount, item.Reference)),
            payment.PaidAt, payment.ConfirmedByUserId, "payable-payment", token);
    }

    private async Task ApplyReceivablePaymentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        CustomerPaymentDocumentPayload payment, CancellationToken token)
    {
        await using (var apply = new SqlCommand("""
            DECLARE @Allocations TABLE(LineNumber int UNIQUE,ReceivableId uniqueidentifier PRIMARY KEY,
              Amount decimal(19,4),TransactionId uniqueidentifier);
            INSERT @Allocations SELECT LineNumber,ReceivableId,Amount,TransactionId FROM OPENJSON(@AllocationsJson)
              WITH(LineNumber int,ReceivableId uniqueidentifier,Amount decimal(19,4),TransactionId uniqueidentifier);
            DECLARE @Ready TABLE(LineNumber int,ReceivableId uniqueidentifier PRIMARY KEY,
              Amount decimal(19,4),BalanceAfter decimal(19,4),TransactionId uniqueidentifier);
            INSERT @Ready SELECT input.LineNumber,input.ReceivableId,input.Amount,
              balance.OutstandingAmount-input.Amount,input.TransactionId
            FROM @Allocations input JOIN dbo.Receivables balance WITH(UPDLOCK,HOLDLOCK)
              ON balance.ReceivableId=input.ReceivableId AND balance.BusinessId=@BusinessId
              AND balance.CustomerId=@CustomerId AND balance.CurrencyCode=@Currency
              AND balance.Status IN(N'Open',N'PartiallyPaid') AND balance.OutstandingAmount>=input.Amount
            JOIN dbo.CustomerPaymentApplications application WITH(UPDLOCK,HOLDLOCK)
              ON application.ReceivableId=input.ReceivableId AND application.PaymentId=@PaymentId
              AND application.LineNumber=input.LineNumber AND application.Amount=input.Amount AND application.AppliedAt IS NULL;
            IF @@ROWCOUNT<>@AllocationCount THROW 51607,'The payment allocations are no longer valid.',1;
            UPDATE balance SET OutstandingAmount=ready.BalanceAfter,
              Status=CASE WHEN ready.BalanceAfter=0 THEN N'Paid' ELSE N'PartiallyPaid' END
            FROM dbo.Receivables balance JOIN @Ready ready ON ready.ReceivableId=balance.ReceivableId;
            IF @@ROWCOUNT<>@AllocationCount THROW 51607,'The payment balances were not updated atomically.',1;
            UPDATE application SET AppliedAt=@Now FROM dbo.CustomerPaymentApplications application
              JOIN @Ready ready ON ready.LineNumber=application.LineNumber AND ready.ReceivableId=application.ReceivableId
              WHERE application.PaymentId=@PaymentId AND application.AppliedAt IS NULL;
            IF @@ROWCOUNT<>@AllocationCount THROW 51607,'The payment applications were not updated atomically.',1;
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,TransactionType,Amount,
              SourceDocumentId,OccurredAt,CreatedAt)
            SELECT TransactionId,ReceivableId,N'Payment',Amount,@PaymentId,@At,@Now FROM @Ready;
            """, connection, transaction))
        {
            apply.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
            apply.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
            apply.Parameters.AddWithValue("@CustomerId", payment.CustomerId);
            apply.Parameters.AddWithValue("@Currency", payment.CurrencyCode);
            apply.Parameters.AddWithValue("@AllocationCount", payment.Allocations.Count);
            apply.Parameters.AddWithValue("@At", payment.PaidAt);
            apply.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            apply.Parameters.Add("@AllocationsJson", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(
                payment.Allocations.Select(allocation => new { allocation.LineNumber,allocation.ReceivableId,allocation.Amount,
                    TransactionId = ids.NewId() }));
            await apply.ExecuteNonQueryAsync(token);
        }
        var completedAt = timeProvider.GetUtcNow();
        await using var complete = new SqlCommand("""
            UPDATE dbo.CustomerPayments SET Status=N'Processed',ProcessedAt=@Now
            WHERE PaymentId=@PaymentId AND BusinessId=@BusinessId AND Status=N'Accepted'
              AND NOT EXISTS(SELECT 1 FROM dbo.CustomerPaymentApplications
                             WHERE PaymentId=@PaymentId AND AppliedAt IS NULL);
            """, connection, transaction);
        complete.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
        complete.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
        complete.Parameters.AddWithValue("@Now", completedAt);
        if (await complete.ExecuteNonQueryAsync(token) != 1)
            throw new DBConcurrencyException("The customer payment could not be completed.");
        if (payment.WorkSessionId is Guid sessionId)
            await InsertPaymentSessionMovementsAsync(connection, transaction,
                sessionId, payment.PaymentId, "ReceivablePayment",
                payment.Payments.Select(item => (item.LineNumber, item.MethodCode, item.Amount, item.Reference)),
                payment.PaidAt, payment.ConfirmedByUserId, "receivable-payment", token);
    }

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

    private async Task InsertSessionMovementAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid sessionId, Guid? documentId, string movementType, string method,
        decimal amount, string? reference, DateTimeOffset occurredAt, Guid userId,
        string sourceKey, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
               MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
            VALUES(@Id,@SessionId,@DocumentId,NULL,@Date,@Type,@Method,@Amount,
               @Reference,@SourceKey,@At,@UserId);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@DocumentId", (object?)documentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Date", occurredAt.Date);
        command.Parameters.AddWithValue("@Type", movementType);
        command.Parameters.AddWithValue("@Method", method);
        AddMoney(command, "@Amount", amount);
        command.Parameters.AddWithValue("@Reference", (object?)reference ?? DBNull.Value);
        command.Parameters.AddWithValue("@SourceKey", sourceKey);
        command.Parameters.AddWithValue("@At", occurredAt);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task InsertPaymentSessionMovementsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid sessionId, Guid paymentId,
        string movementType, IEnumerable<(int LineNumber, string MethodCode, decimal Amount, string? Reference)> values,
        DateTimeOffset occurredAt, Guid userId, string sourcePrefix, CancellationToken token)
    {
        var rows = values.Select(value => new
        {
            MovementId = ids.NewId(), value.LineNumber, value.MethodCode, value.Amount, value.Reference,
            SourceKey = $"{sourcePrefix}:{paymentId:N}:{value.LineNumber}"
        }).ToArray();
        await using var command = new SqlCommand("""
            INSERT dbo.WorkSessionMovements
              (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
               MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
            SELECT input.MovementId,@SessionId,@PaymentId,NULL,CONVERT(date,@At),@Type,
                   input.MethodCode,input.Amount,input.Reference,input.SourceKey,@At,@UserId
            FROM OPENJSON(@Rows) WITH(
              MovementId uniqueidentifier,LineNumber int,MethodCode nvarchar(32),Amount decimal(19,4),
              Reference nvarchar(160),SourceKey nvarchar(160)) input;
            IF @@ROWCOUNT<>@Count THROW 51608,'The work-session payment breakdown was not recorded atomically.',1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@At", occurredAt);
        command.Parameters.AddWithValue("@Type", movementType);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Count", rows.Length);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        await command.ExecuteNonQueryAsync(token);
    }
}
