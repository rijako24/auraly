using System.Data;
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
    private Task ApplyFinancialEffectsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SourceEnvelope source,
        CancellationToken cancellationToken) => source.DocumentType switch
    {
        "SalesInvoice" or "SalesReceipt" => ApplySaleFinancialEffectsAsync(
            connection, transaction,
            PosSaleContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "SalesReturn" => ApplySalesReturnFinancialEffectsAsync(
            connection, transaction,
            SalesReturnContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "SalesDebitNote" => ApplySalesDebitNoteFinancialEffectsAsync(
            connection, transaction,
            SalesDebitNoteContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "GoodsReceipt" => ApplyGoodsReceiptFinancialEffectsAsync(
            connection, transaction,
            GoodsReceiptContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "Expense" => ApplyExpenseFinancialEffectsAsync(
            connection, transaction,
            ExpenseContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "PurchaseReturn" => ApplyPurchaseReturnFinancialEffectsAsync(
            connection, transaction,
            PurchaseReturnContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "PayablePayment" => ApplyPayablePaymentFinancialEffectsAsync(
            connection, transaction,
            SupplierPaymentContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "ReceivablePayment" => ApplyReceivablePaymentFinancialEffectsAsync(
            connection, transaction,
            CustomerPaymentContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        "CashReceipt" or "CashDisbursement" => ApplyCashMovementFinancialEffectsAsync(
            connection, transaction,
            CashMovementContractSerializer.Deserialize(source.PayloadJson), cancellationToken),
        AccountingManualDocumentTypes.AccountAdjustment => ApplyAccountAdjustmentFinancialEffectsAsync(
            connection, transaction, source, cancellationToken),
        AccountingManualDocumentTypes.ManualVoucher => Task.CompletedTask,
        _ => Task.CompletedTask
    };

    private async Task ApplySalesDebitNoteFinancialEffectsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SalesDebitNoteDocumentPayload value,
        CancellationToken token)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.Receivables
              (ReceivableId,BusinessId,CustomerId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES(@ReceivableId,@BusinessId,@CustomerId,@DocumentId,N'SalesDebitNote',
               @Number,N'COP',@Amount,@Amount,@DueDate,N'Open',@Now);
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
        PosSaleUploadRequest sale, CancellationToken token)
    {
        if (sale.Credit is not null)
        {
            await using var receivable = new SqlCommand("""
                INSERT dbo.Receivables
                  (ReceivableId,BusinessId,CustomerId,SourceDocumentId,SourceDocumentType,
                   DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                VALUES(@ReceivableId,@BusinessId,@CustomerId,@DocumentId,N'SalesInvoice',
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
            receivable.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
            receivable.Parameters.AddWithValue("@Number", sale.DocumentNumber.FullNumber);
            AddMoney(receivable, "@Amount", sale.Credit.Amount);
            receivable.Parameters.AddWithValue("@DueDate", sale.Credit.DueDate);
            receivable.Parameters.AddWithValue("@OccurredAt", sale.CommercialSnapshot.IssuedAt);
            receivable.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            if (await receivable.ExecuteNonQueryAsync(token) != 2)
                throw new DBConcurrencyException("The receivable was not opened atomically.");
        }

        foreach (var payment in sale.Payments.OrderBy(x => x.PaymentNumber))
        {
            await using var movement = new SqlCommand("""
                INSERT dbo.WorkSessionMovements
                  (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,
                   BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,SourceKey,
                   OccurredAt,RecordedByUserId)
                VALUES(@Id,@SessionId,@DocumentId,@Number,@Date,N'SalePayment',@Method,
                   @Amount,@Reference,@SourceKey,@OccurredAt,@UserId);
                """, connection, transaction);
            movement.Parameters.AddWithValue("@Id", ids.NewId());
            movement.Parameters.AddWithValue("@SessionId", sale.WorkSessionId);
            movement.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
            movement.Parameters.AddWithValue("@Number", payment.PaymentNumber);
            movement.Parameters.AddWithValue("@Date", sale.CommercialSnapshot.IssuedAt.Date);
            movement.Parameters.AddWithValue("@Method", payment.MethodCode);
            AddMoney(movement, "@Amount", payment.Amount);
            movement.Parameters.AddWithValue("@Reference", (object?)payment.Reference ?? DBNull.Value);
            movement.Parameters.AddWithValue("@SourceKey", $"sale:{sale.DocumentId:D}:{payment.PaymentNumber}");
            movement.Parameters.AddWithValue("@OccurredAt", sale.CommercialSnapshot.IssuedAt);
            movement.Parameters.AddWithValue("@UserId", sale.SoldByUserId);
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
               OriginalPaymentNumber,Amount,Reference,OccurredAt)
            VALUES(@ReturnId,1,@Type,@Method,@OriginalId,@PaymentNumber,@Amount,@Reference,@At);
            """, connection, transaction))
        {
            settlement.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            settlement.Parameters.AddWithValue("@Type", value.EconomicResolution);
            settlement.Parameters.AddWithValue("@Method", (object?)value.RefundMethodCode ?? DBNull.Value);
            settlement.Parameters.AddWithValue("@OriginalId", value.OriginalDocumentId);
            settlement.Parameters.AddWithValue("@PaymentNumber", (object?)value.OriginalPaymentNumber ?? DBNull.Value);
            AddMoney(settlement, "@Amount", value.TotalAmount);
            settlement.Parameters.AddWithValue("@Reference", value.DocumentNumber);
            settlement.Parameters.AddWithValue("@At", value.ReturnedAt);
            await settlement.ExecuteNonQueryAsync(token);
        }

        if (value.EconomicResolution == ReturnEconomicResolutions.Refund)
        {
            if (value.WorkSessionId is null || value.OriginalPaymentNumber is null)
                throw new InvalidOperationException(
                    "A cash refund requires its work session and original payment.");
            await using var refund = new SqlCommand("""
                INSERT dbo.WorkSessionMovements
                  (WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,
                   MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
                VALUES(@Id,@SessionId,@OriginalId,@PaymentNumber,@Date,N'Refund',N'Cash',
                   @Amount,@Reference,@SourceKey,@At,@UserId);
                """, connection, transaction);
            refund.Parameters.AddWithValue("@Id", ids.NewId());
            refund.Parameters.AddWithValue("@SessionId", value.WorkSessionId.Value);
            refund.Parameters.AddWithValue("@OriginalId", value.OriginalDocumentId);
            refund.Parameters.AddWithValue("@PaymentNumber", value.OriginalPaymentNumber.Value);
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
            SELECT TOP(1) ReceivableId,OutstandingAmount
            FROM dbo.Receivables WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND CustomerId=@CustomerId
              AND SourceDocumentId=@OriginalId AND SourceDocumentType=N'SalesInvoice'
              AND Status IN(N'Open',N'PartiallyPaid') ORDER BY CreatedAt;
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

    private Task ApplyGoodsReceiptFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        GoodsReceiptDocumentPayload value, CancellationToken token) =>
        value.CreatesPayable && value.Withholding.NetAmount > 0
            ? OpenPayableAsync(connection, transaction, value.BusinessId, value.SupplierId,
                value.DocumentId, "GoodsReceipt", value.DocumentNumber, value.CurrencyCode,
                value.Withholding.NetAmount, value.DueDate!.Value, value.ReceivedAt, token)
            : Task.CompletedTask;

    private Task ApplyExpenseFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        ExpenseDocumentPayload value, CancellationToken token) =>
        value.Withholding.NetAmount > 0
            ? OpenPayableAsync(connection, transaction, value.BusinessId, value.SupplierId,
                value.ExpenseId, "Expense", value.DocumentNumber, value.CurrencyCode,
                value.Withholding.NetAmount, value.DueDate, value.IssuedAt, token)
            : Task.CompletedTask;

    private async Task OpenPayableAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid supplierId, Guid documentId, string documentType,
        string number, string currency, decimal amount, DateTimeOffset dueDate,
        DateTimeOffset occurredAt, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.Payables
              (PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES(@PayableId,@BusinessId,@SupplierId,@DocumentId,@DocumentType,
               @Number,@Currency,@Amount,@Amount,@DueDate,N'Open',@Now);
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
        foreach (var allocation in payment.Allocations.OrderBy(x => x.LineNumber))
        {
            decimal balance;
            await using (var read = new SqlCommand("""
                SELECT p.OutstandingAmount FROM dbo.Payables p WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.SupplierPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                  ON a.PayableId=p.PayableId AND a.PaymentId=@PaymentId
                 AND a.LineNumber=@Line AND a.Amount=@Amount AND a.AppliedAt IS NULL
                WHERE p.PayableId=@PayableId AND p.BusinessId=@BusinessId
                  AND p.SupplierId=@SupplierId AND p.CurrencyCode=@Currency
                  AND p.Status IN(N'Open',N'PartiallyPaid');
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
                read.Parameters.AddWithValue("@Line", allocation.LineNumber);
                AddMoney(read, "@Amount", allocation.Amount);
                read.Parameters.AddWithValue("@PayableId", allocation.PayableId);
                read.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
                read.Parameters.AddWithValue("@SupplierId", payment.SupplierId);
                read.Parameters.AddWithValue("@Currency", payment.CurrencyCode);
                var result = await read.ExecuteScalarAsync(token);
                if (result is null || allocation.Amount > (balance = Convert.ToDecimal(result)))
                    throw new InvalidOperationException("The supplier payment allocation is no longer valid.");
            }
            var now = timeProvider.GetUtcNow();
            await using var apply = new SqlCommand("""
                UPDATE dbo.Payables SET OutstandingAmount=@After,
                  Status=CASE WHEN @After=0 THEN N'Paid' ELSE N'PartiallyPaid' END
                WHERE PayableId=@PayableId;
                UPDATE dbo.SupplierPaymentApplications SET AppliedAt=@Now
                WHERE PaymentId=@PaymentId AND LineNumber=@Line AND AppliedAt IS NULL;
                INSERT dbo.PayableTransactions
                  (PayableTransactionId,PayableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@PayableId,N'Payment',@Amount,@PaymentId,@At,@Now);
                """, connection, transaction);
            AddMoney(apply, "@After", balance - allocation.Amount);
            apply.Parameters.AddWithValue("@PayableId", allocation.PayableId);
            apply.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
            apply.Parameters.AddWithValue("@Line", allocation.LineNumber);
            apply.Parameters.AddWithValue("@Now", now);
            apply.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(apply, "@Amount", allocation.Amount);
            apply.Parameters.AddWithValue("@At", payment.PaidAt);
            if (await apply.ExecuteNonQueryAsync(token) != 3)
                throw new DBConcurrencyException("The supplier payment was not applied atomically.");
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
        if (payment.PaymentMethod != "Cash" || payment.WorkSessionId is null) return;
        await InsertSessionMovementAsync(connection, transaction, payment.WorkSessionId.Value,
            payment.PaymentId, "PayablePayment", "Cash", -payment.TotalAmount,
            payment.Reference, payment.PaidAt, payment.ConfirmedByUserId,
            $"payable-payment:{payment.PaymentId:N}", token);
    }

    private async Task ApplyReceivablePaymentFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        CustomerPaymentDocumentPayload payment, CancellationToken token)
    {
        foreach (var allocation in payment.Allocations.OrderBy(x => x.LineNumber))
        {
            decimal balance;
            await using (var read = new SqlCommand("""
                SELECT r.OutstandingAmount FROM dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.CustomerPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                  ON a.ReceivableId=r.ReceivableId AND a.PaymentId=@PaymentId
                 AND a.LineNumber=@Line AND a.Amount=@Amount AND a.AppliedAt IS NULL
                WHERE r.ReceivableId=@ReceivableId AND r.BusinessId=@BusinessId
                  AND r.CustomerId=@CustomerId AND r.CurrencyCode=@Currency
                  AND r.Status IN(N'Open',N'PartiallyPaid');
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
                read.Parameters.AddWithValue("@Line", allocation.LineNumber);
                AddMoney(read, "@Amount", allocation.Amount);
                read.Parameters.AddWithValue("@ReceivableId", allocation.ReceivableId);
                read.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
                read.Parameters.AddWithValue("@CustomerId", payment.CustomerId);
                read.Parameters.AddWithValue("@Currency", payment.CurrencyCode);
                var result = await read.ExecuteScalarAsync(token);
                if (result is null || allocation.Amount > (balance = Convert.ToDecimal(result)))
                    throw new InvalidOperationException("The customer payment allocation is no longer valid.");
            }
            var now = timeProvider.GetUtcNow();
            await using var apply = new SqlCommand("""
                UPDATE dbo.Receivables SET OutstandingAmount=@After,
                  Status=CASE WHEN @After=0 THEN N'Paid' ELSE N'PartiallyPaid' END
                WHERE ReceivableId=@ReceivableId;
                UPDATE dbo.CustomerPaymentApplications SET AppliedAt=@Now
                WHERE PaymentId=@PaymentId AND LineNumber=@Line AND AppliedAt IS NULL;
                INSERT dbo.ReceivableTransactions
                  (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@ReceivableId,N'Payment',@Amount,@PaymentId,@At,@Now);
                """, connection, transaction);
            AddMoney(apply, "@After", balance - allocation.Amount);
            apply.Parameters.AddWithValue("@ReceivableId", allocation.ReceivableId);
            apply.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
            apply.Parameters.AddWithValue("@Line", allocation.LineNumber);
            apply.Parameters.AddWithValue("@Now", now);
            apply.Parameters.AddWithValue("@TransactionId", ids.NewId());
            AddMoney(apply, "@Amount", allocation.Amount);
            apply.Parameters.AddWithValue("@At", payment.PaidAt);
            if (await apply.ExecuteNonQueryAsync(token) != 3)
                throw new DBConcurrencyException("The customer payment was not applied atomically.");
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
            await InsertSessionMovementAsync(connection, transaction, sessionId, null,
                "ReceivablePayment", payment.PaymentMethod, payment.TotalAmount,
                payment.Reference, payment.PaidAt, payment.ConfirmedByUserId,
                $"receivable-payment:{payment.PaymentId:D}", token);
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
}
