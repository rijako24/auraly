using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Returns;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesReturnDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => SalesReturnDocumentTypes.SalesReturn;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var value = SalesReturnContractSerializer.Deserialize(document.Payload);
        if (value.ReturnId != document.DocumentId.Value ||
            value.BusinessId != document.BusinessId.Value ||
            value.TenantId != document.TenantId.Value)
            throw new InvalidOperationException(
                "The sales return envelope does not match its payload.");
        var session = sessions.Current;
        foreach (var line in value.Lines.OrderBy(line => line.LineNumber))
            await ApplyInventoryAsync(session, value, line, cancellationToken);
        await InsertTaxSummariesAsync(session, value, cancellationToken);
        await ApplyEconomicResolutionAsync(session, value, cancellationToken);
        await InsertOutboxAsync(session, value, document.Payload, cancellationToken);
        await MarkProcessedAsync(session, value, cancellationToken);
    }

    private async Task ApplyInventoryAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        SalesReturnLineSnapshot line,
        CancellationToken cancellationToken)
    {
        if (line.InventoryDisposition != ReturnInventoryDispositions.Sellable) return;
        const string sql = """
            DECLARE @ManageStock BIT;
            DECLARE @QuantityBefore DECIMAL(19,6);
            DECLARE @AverageCost DECIMAL(19,6);
            DECLARE @ValueBefore DECIMAL(19,4);
            SELECT @ManageStock=p.ManageStock
            FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Warehouses w WITH (UPDLOCK,HOLDLOCK)
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId
            WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId;
            IF @ManageStock IS NULL
              THROW 51210,'The return product or warehouse is outside the business.',1;
            IF @ManageStock=0 RETURN;
            IF NOT EXISTS
              (SELECT 1 FROM dbo.InventoryBalances WITH (UPDLOCK,HOLDLOCK)
               WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId)
              INSERT dbo.InventoryBalances
                (BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                 InventoryValue,LastProcessingSequence,UpdatedAt)
              VALUES(@BusinessId,@WarehouseId,@ProductId,0,0,0,@Sequence,@Now);
            SELECT @QuantityBefore=QuantityOnHand,@AverageCost=AverageUnitCost,
                   @ValueBefore=InventoryValue
            FROM dbo.InventoryBalances WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            DECLARE @QuantityAfter DECIMAL(19,6)=@QuantityBefore+@Quantity;
            DECLARE @ValueChange DECIMAL(19,4)=CAST(@Quantity*@RecognizedUnitCost AS DECIMAL(19,4));
            DECLARE @ValueAfter DECIMAL(19,4)=@ValueBefore+@ValueChange;
            DECLARE @AverageCostAfter DECIMAL(19,6)=CASE WHEN @QuantityAfter=0 THEN 0
              ELSE CAST(@ValueAfter/@QuantityAfter AS DECIMAL(19,6)) END;
            UPDATE dbo.InventoryBalances
            SET QuantityOnHand=@QuantityAfter,InventoryValue=@ValueAfter,AverageUnitCost=@AverageCostAfter,
                LastProcessingSequence=@Sequence,UpdatedAt=@Now
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            INSERT dbo.InventoryMovements
              (InventoryMovementId,BusinessId,WarehouseId,DocumentId,DocumentType,
               LineNumber,ProductId,MovementType,QuantityChange,ProcessingSequence,
               QuantityBefore,QuantityAfter,AverageUnitCostBefore,AverageUnitCostAfter,
               RecognizedUnitCost,ValueChange,OccurredAt,PostedAt,CreatedAt)
            VALUES(@MovementId,@BusinessId,@WarehouseId,@DocumentId,N'SalesReturn',
               @LineNumber,@ProductId,N'SalesReturn',@Quantity,@Sequence,@QuantityBefore,
               @QuantityAfter,@AverageCost,@AverageCostAfter,@RecognizedUnitCost,@ValueChange,
               @OccurredAt,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@MovementId", ids.NewId());
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", value.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", line.ProductId);
        command.Parameters.AddWithValue("@DocumentId", value.ReturnId);
        command.Parameters.AddWithValue("@LineNumber", line.LineNumber);
        AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
        AddDecimal(command, "@RecognizedUnitCost", line.RecognizedUnitCost, 19, 6);
        command.Parameters.AddWithValue("@Sequence", session.ProcessingSequence);
        command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    private async Task InsertTaxSummariesAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.SalesReturnTaxSummaries
              (ReturnId,TaxCode,TaxRate,TaxableAmount,TaxAmount,TotalAmount,CreatedAt)
            VALUES(@ReturnId,@TaxCode,@TaxRate,@Taxable,@Tax,@Total,@Now);
            """;
        foreach (var summary in value.Lines
            .GroupBy(line => new { line.TaxCode, line.TaxRate })
            .OrderBy(group => group.Key.TaxCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TaxRate))
        {
            await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            command.Parameters.AddWithValue("@TaxCode", summary.Key.TaxCode);
            AddDecimal(command, "@TaxRate", summary.Key.TaxRate, 9, 6);
            AddDecimal(command, "@Taxable", summary.Sum(line => line.UntaxedAmount), 19, 4);
            AddDecimal(command, "@Tax", summary.Sum(line => line.TaxAmount), 19, 4);
            AddDecimal(command, "@Total", summary.Sum(line => line.LineTotal), 19, 4);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
    private async Task ApplyEconomicResolutionAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string settlement = """
            INSERT dbo.SalesReturnSettlements
              (ReturnId,SettlementNumber,SettlementType,MethodCode,Amount,Reference,OccurredAt)
            VALUES(@ReturnId,1,@Type,@Method,@Amount,@Reference,@OccurredAt);
            """;
        await using (var command = new SqlCommand(
            settlement, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
            command.Parameters.AddWithValue("@Type", value.EconomicResolution);
            command.Parameters.AddWithValue("@Method", (object?)value.RefundMethodCode ?? DBNull.Value);
            AddDecimal(command, "@Amount", value.TotalAmount, 19, 4);
            command.Parameters.AddWithValue("@Reference", value.DocumentNumber);
            command.Parameters.AddWithValue("@OccurredAt", value.ReturnedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (value.EconomicResolution != ReturnEconomicResolutions.CustomerCredit) return;
        if (value.CustomerId is null)
            throw new InvalidOperationException(
                "An identified customer is required to open customer credit.");
        const string credit = """
            INSERT dbo.CustomerCredits
              (CustomerCreditId,BusinessId,CustomerId,SourceReturnId,OriginalAmount,
               AvailableAmount,Status,CreatedAt)
            VALUES(@Id,@BusinessId,@CustomerId,@ReturnId,@Amount,@Amount,N'Open',@Now);
            """;
        await using var creditCommand = new SqlCommand(
            credit, session.Connection, session.Transaction);
        creditCommand.Parameters.AddWithValue("@Id", ids.NewId());
        creditCommand.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        creditCommand.Parameters.AddWithValue("@CustomerId", value.CustomerId.Value);
        creditCommand.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        AddDecimal(creditCommand, "@Amount", value.TotalAmount, 19, 4);
        creditCommand.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await creditCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        string payload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@Id,@DocumentId,N'SalesReturn',N'sales.return.processed',@Payload,@Now);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", value.ReturnId);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SalesReturnDocumentPayload value,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.SalesReturns SET Status=N'Processed',ProcessedAt=@Now
            WHERE ReturnId=@ReturnId AND BusinessId=@BusinessId AND Status=N'Accepted';
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@ReturnId", value.ReturnId);
        command.Parameters.AddWithValue("@BusinessId", value.BusinessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException("The sales return could not be marked as processed.");
    }

    private static void AddDecimal(
        SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }
}
