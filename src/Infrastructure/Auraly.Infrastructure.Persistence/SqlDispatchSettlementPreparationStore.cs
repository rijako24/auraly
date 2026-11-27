using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Dispatching;
using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlDispatchSettlementPreparationStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider) : IDispatchSettlementPreparationStore
{
    public async Task<(DispatchSettlementWork? Work,TimeSpan? RetryAfter)> ClaimLegacyAsync(CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var select = Procedure("dbo.DispatchSettlementOperationClaimGet", connection);
        await using var reader = await select.ExecuteReaderAsync(token);
        if(await reader.ReadAsync(token)) return (new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetGuid(3),reader.GetGuid(4),
            reader.GetDateTimeOffset(5),reader.GetInt32(6),reader.GetGuid(7),reader.GetString(8),reader.GetGuid(9),reader.GetString(10)),null);
        await reader.NextResultAsync(token);
        if(!await reader.ReadAsync(token)||reader.IsDBNull(0))return(null,null);
        // SQL supplies both instants so clock skew cannot delay an expired lease.
        var delay=reader.GetDateTimeOffset(0)-reader.GetDateTimeOffset(1);
        return(null,delay>TimeSpan.Zero?delay:TimeSpan.Zero);
    }

    private async Task<IReadOnlyList<DispatchReturnWork>> ReadReturnsAsync(DispatchSettlementWork operation, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementReturnsGet", connection);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        var rows = new List<(Guid Source, Guid Warehouse, bool NotDelivered, string ReasonCode, bool ReverseUncollected, DispatchReturnLine Line)>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            rows.Add((reader.GetGuid(0), reader.GetGuid(6), reader.GetString(1) == "NotDelivered",
                reader.IsDBNull(5) ? throw new InvalidOperationException(
                    "No hay un motivo de devolución de venta activo para esta sede.") : reader.GetString(5),
                reader.GetString(7)=="Dispatch" && reader.GetDecimal(8)<reader.GetDecimal(9),
                new(reader.GetInt32(2), reader.GetDecimal(3), reader.GetString(4))));
        var charges = new Dictionary<Guid,List<Guid>>();
        await reader.NextResultAsync(token);
        while(await reader.ReadAsync(token))
        {
            if(!charges.TryGetValue(reader.GetGuid(0),out var items)) charges.Add(reader.GetGuid(0),items=[]);
            items.Add(reader.GetGuid(1));
        }
        return rows.GroupBy(row => new { row.Source, row.Warehouse, row.NotDelivered, row.ReasonCode, row.ReverseUncollected })
            .Select(group => new DispatchReturnWork(DeterministicGuid($"dispatch:{operation.DispatchId:N}:return:{group.Key.Source:N}"),
                group.Key.Source, group.Key.Warehouse, group.Key.NotDelivered, group.Key.ReasonCode,
                group.Select(row => row.Line).ToArray(),group.Key.ReverseUncollected,
                charges.GetValueOrDefault(group.Key.Source,[]))).ToArray();
    }

    private async Task<IReadOnlyList<DispatchPaymentWork>> ReadPaymentsAsync(DispatchSettlementWork operation, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementPaymentsGet", connection);
        command.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        var values = new List<(Guid Source,Guid Customer,Guid Receivable,string KeyMethod,CustomerPaymentTenderRequest Tender)>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var source = reader.GetGuid(0);
            var method = reader.GetString(3);
            values.Add((source,reader.GetGuid(1),reader.GetGuid(2),reader.GetString(11),
                new CustomerPaymentTenderRequest(method,reader.GetDecimal(4),
                    BankAccountId:reader.IsDBNull(6)?null:reader.GetGuid(6),
                    Reference:reader.IsDBNull(5)?null:reader.GetString(5),
                    PaymentMethodId:reader.IsDBNull(7)?null:reader.GetGuid(7),
                    PaymentMethodVersion:reader.IsDBNull(8)?null:reader.GetInt32(8),
                    CardFranchiseCode:reader.IsDBNull(9)?null:reader.GetString(9),
                    ApprovalNumber:reader.IsDBNull(10)?null:reader.GetString(10))));
        }
        return values.GroupBy(value=>new{value.Source,value.Customer,value.Receivable,value.KeyMethod})
            .Select(group=>new DispatchPaymentWork(
                DeterministicGuid($"dispatch:{operation.DispatchId:N}:payment:{group.Key.Source:N}:{group.Key.KeyMethod}"),
                group.Key.Source,group.Key.Customer,group.Key.Receivable,group.Key.KeyMethod,
                group.Select(value=>value.Tender).ToArray())).ToArray();
    }

    public async Task RescheduleLegacyAsync(DispatchSettlementWork operation, string? error, bool attention, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using var command = Procedure("dbo.DispatchSettlementOperationReschedule", connection, transaction);
        command.Parameters.AddWithValue("@OperationStatus", attention ? "NeedsAttention" : "Pending");
        command.Parameters.AddWithValue("@DispatchStatus", attention ? "SettlementAttention" : "SettlementProcessing");
        command.Parameters.AddWithValue("@SettlementStatus", attention ? "Attention" : "Processing");
        command.Parameters.AddWithValue("@Error", (object?)error?[..Math.Min(error.Length, 2000)] ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", operation.Id);
        command.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
        try
        {
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<DispatchFinancialWork> EnsureCashDifferenceDocumentAsync(
        DispatchSettlementWork operation,
        CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, token);
        try
        {
            decimal expected;
            decimal received;
            DateTimeOffset occurredAt;
            string? notes;
            Guid? cashDifferenceForm;
            var collections = new List<DispatchCollectedPayment>();
            await using (var select = Procedure("dbo.DispatchSettlementCashDifferenceGet", connection, transaction))
            {
                select.Parameters.AddWithValue("@SettlementId", operation.SettlementId);
                select.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
                select.Parameters.AddWithValue("@DispatchId", operation.DispatchId);
                await using var reader = await select.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token) || reader.IsDBNull(1) || reader.IsDBNull(2))
                    throw new InvalidOperationException(
                        "La liquidación del despacho aún no está lista para registrar su diferencia de efectivo.");
                expected = reader.GetDecimal(0);
                received = reader.GetDecimal(1);
                occurredAt = TimeZoneInfo.ConvertTime(reader.GetDateTimeOffset(2),TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(5)));
                notes = reader.IsDBNull(3) ? null : reader.GetString(3);
                cashDifferenceForm=reader.IsDBNull(4)?null:reader.GetGuid(4);
                await reader.NextResultAsync(token);
                while (await reader.ReadAsync(token))
                    collections.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetDecimal(4)));
            }

            var difference = decimal.Round(received - expected, 4);
            var movementId = DeterministicGuid(
                $"dispatch:{operation.DispatchId:N}:cash-difference:movement");
            await using (var replay = Procedure("dbo.DocumentProcessingJobByDocumentGet", connection, transaction))
            {
                replay.Parameters.AddWithValue("@DocumentId", operation.SettlementId);
                replay.Parameters.AddWithValue("@DocumentType", DispatchAccountingDocumentTypes.CashDifference);
                if (await replay.ExecuteScalarAsync(token) is Guid existing)
                {
                    await transaction.CommitAsync(token);
                    return new(new DocumentProcessingSignal(existing, operation.BusinessId,
                        operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference), true);
                }
            }

            var payload = new DispatchCashDifferencePayload(
                operation.SettlementId, operation.TenantId, operation.BusinessId,
                operation.DispatchId, operation.DispatchNumber, operation.TransporterName,
                expected, received, difference, occurredAt, notes, collections, cashDifferenceForm);
            var json = System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            var now = timeProvider.GetUtcNow();
            var sequence = await SqlOperationalDocumentAllocator.AllocateSequenceAsync(
                connection, transaction, operation.BusinessId, now, token);
            await using (var insert = Procedure("dbo.DispatchCashDifferenceDocumentCreate", connection, transaction))
            {
                insert.Parameters.AddWithValue("@JobId", movementId);
                insert.Parameters.AddWithValue("@BusinessId", operation.BusinessId);
                insert.Parameters.AddWithValue("@Sequence", sequence);
                insert.Parameters.AddWithValue("@DocumentId", operation.SettlementId);
                insert.Parameters.AddWithValue("@DocumentType", DispatchAccountingDocumentTypes.CashDifference);
                insert.Parameters.AddWithValue("@Now", now);
                insert.Parameters.AddWithValue("@Payload", json);
                insert.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = hash;
                await insert.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token);
            return new(new DocumentProcessingSignal(movementId, operation.BusinessId,
                operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference), false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static SqlCommand Procedure(
        string name,
        SqlConnection connection,
        SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

}
