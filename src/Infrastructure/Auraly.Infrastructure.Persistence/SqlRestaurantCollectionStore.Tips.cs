using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantCollectionStore
{
    public async Task<IReadOnlyList<RestaurantTipSettlement>> RecentTipSettlementsAsync(
        RestaurantAccountActor actor, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT recent.SettlementId,recent.TotalAmount,recent.DistributionBasis,
              recent.AgreementReference,recent.PaymentMethodCode,
              recent.PaymentReference,recent.CreatedAt,recipient.PartyId,
              recipient.Amount
            FROM (SELECT TOP(30) SettlementId,TotalAmount,DistributionBasis,
                   AgreementReference,PaymentMethodCode,PaymentReference,CreatedAt
                  FROM sales.RestaurantTipSettlements
                  WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  ORDER BY CreatedAt DESC,SettlementId DESC) recent
            JOIN sales.RestaurantTipSettlementRecipients recipient
              ON recipient.SettlementId=recent.SettlementId
            ORDER BY recent.CreatedAt DESC,recent.SettlementId DESC,
                     recipient.PartyId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<RestaurantTipSettlement>();
        var recipients = new List<RestaurantTipRecipientRequest>();
        RestaurantTipSettlement? current = null;
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (current?.SettlementId != id)
            {
                if (current is not null) rows.Add(current);
                recipients = [];
                current = new(id, actor.BusinessId, reader.GetDecimal(1),
                    reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6), recipients);
            }
            recipients.Add(new(reader.GetGuid(7), reader.GetDecimal(8)));
        }
        if (current is not null) rows.Add(current);
        return rows;
    }

    public async Task<RestaurantTipPendingPage> PendingTipsAsync(
        RestaurantAccountActor actor, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT COUNT(*),COALESCE(SUM(collection.TipAmount),0)
            FROM sales.RestaurantAccountCollections collection
            WHERE collection.TenantId=@TenantId AND collection.BusinessId=@BusinessId
              AND collection.Status=N'Captured' AND collection.TipAmount>0
              AND NOT EXISTS(SELECT 1 FROM sales.RestaurantTipSettlementCollections settled
                  WHERE settled.CollectionId=collection.CollectionId);
            SELECT TOP(100) collection.CollectionId,collection.AccountId,
              collection.TipAmount,collection.CapturedAt
            FROM sales.RestaurantAccountCollections collection
            WHERE collection.TenantId=@TenantId AND collection.BusinessId=@BusinessId
              AND collection.Status=N'Captured' AND collection.TipAmount>0
              AND NOT EXISTS(SELECT 1 FROM sales.RestaurantTipSettlementCollections settled
                  WHERE settled.CollectionId=collection.CollectionId)
            ORDER BY collection.CapturedAt,collection.CollectionId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        var total = reader.GetDecimal(1);
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantTipPendingCollection>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetDecimal(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return new(items, count, total);
    }

    public async Task<RestaurantTipSettlement> SettleTipsAsync(
        RestaurantAccountActor actor, SettleRestaurantTipsRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(request)));
        var collectionIds = JsonSerializer.Serialize(request.CollectionIds);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var replay = new SqlCommand("""
                SELECT RequestHash FROM sales.RestaurantTipSettlements WITH(UPDLOCK,HOLDLOCK)
                WHERE SettlementId=@SettlementId AND TenantId=@TenantId
                  AND BusinessId=@BusinessId;
                """, connection, transaction))
            {
                replay.Parameters.AddWithValue("@SettlementId", request.SettlementId);
                replay.Parameters.AddWithValue("@TenantId", actor.TenantId);
                replay.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
                if (await replay.ExecuteScalarAsync(ct) is byte[] existing)
                {
                    if (!existing.AsSpan().SequenceEqual(requestHash))
                        throw new RestaurantAccountConflictException(
                            "La liquidación ya existe con datos diferentes.");
                    var saved = await LoadTipSettlementAsync(connection,
                        transaction, actor, request.SettlementId, ct);
                    await transaction.CommitAsync(ct);
                    return saved;
                }
            }

            decimal total;
            await using (var selected = new SqlCommand("""
                SELECT COUNT(*),COALESCE(SUM(collection.TipAmount),0)
                FROM OPENJSON(@CollectionIds) WITH(CollectionId uniqueidentifier '$') input
                JOIN sales.RestaurantAccountCollections collection WITH(UPDLOCK,HOLDLOCK)
                  ON collection.CollectionId=input.CollectionId
                  AND collection.TenantId=@TenantId AND collection.BusinessId=@BusinessId
                  AND collection.Status=N'Captured' AND collection.TipAmount>0
                LEFT JOIN sales.RestaurantTipSettlementCollections settled WITH(UPDLOCK,HOLDLOCK)
                  ON settled.CollectionId=collection.CollectionId
                WHERE settled.CollectionId IS NULL;
                """, connection, transaction))
            {
                selected.Parameters.Add("@CollectionIds", SqlDbType.NVarChar, -1).Value = collectionIds;
                selected.Parameters.AddWithValue("@TenantId", actor.TenantId);
                selected.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
                await using var reader = await selected.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                if (reader.GetInt32(0) != request.CollectionIds.Count)
                    throw new RestaurantAccountConflictException(
                        "Alguna propina ya fue anulada o liquidada. Actualiza la lista.");
                total = reader.GetDecimal(1);
            }

            var shares = request.DistributionBasis == "EqualByLaw"
                ? EqualTipShares(total, request.Recipients)
                : request.Recipients;
            if (shares.Any(line => line.Amount <= 0) ||
                shares.Sum(line => line.Amount) != total)
                throw new RestaurantAccountValidationException(
                    "La distribución del acuerdo no coincide con las propinas seleccionadas.");
            var recipientsJson = JsonSerializer.Serialize(shares);
            if (request.PaymentMethodCode == "Transfer")
            {
                await using var bank = new SqlCommand("""
                    SELECT COUNT(*) FROM accounting.BankAccounts
                    WHERE BankAccountId=@BankAccountId AND TenantId=@TenantId
                      AND IsActive=1;
                    """, connection, transaction);
                bank.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                bank.Parameters.AddWithValue("@TenantId", actor.TenantId);
                if ((int)(await bank.ExecuteScalarAsync(ct) ?? 0) != 1)
                    throw new RestaurantAccountValidationException(
                        "Selecciona una cuenta bancaria activa de esta empresa.");
            }
            await using (var validate = new SqlCommand("""
                SELECT COUNT(*) FROM OPENJSON(@Recipients)
                  WITH(PartyId uniqueidentifier '$.PartyId') input
                JOIN dbo.Parties party ON party.PartyId=input.PartyId
                  AND party.TenantId=@TenantId AND party.IsActive=1
                WHERE EXISTS(SELECT 1 FROM parties.Waiters waiter
                    WHERE waiter.PartyId=party.PartyId AND waiter.TenantId=@TenantId
                      AND waiter.IsActive=1)
                  OR EXISTS(SELECT 1 FROM dbo.Employees employee
                    WHERE employee.PartyId=party.PartyId AND employee.TenantId=@TenantId
                      AND employee.IsActive=1);
                """, connection, transaction))
            {
                validate.Parameters.Add("@Recipients", SqlDbType.NVarChar, -1).Value = recipientsJson;
                validate.Parameters.AddWithValue("@TenantId", actor.TenantId);
                if ((int)(await validate.ExecuteScalarAsync(ct) ?? 0) != shares.Count)
                    throw new RestaurantAccountValidationException(
                        "Selecciona trabajadores activos de esta empresa para repartir la propina.");
            }

            await using (var insert = new SqlCommand("""
                INSERT sales.RestaurantTipSettlements
                  (SettlementId,TenantId,BusinessId,CreatedByUserId,
                   DistributionBasis,AgreementReference,PaymentMethodCode,
                   BankAccountId,PaymentReference,TotalAmount,RequestHash,CreatedAt)
                VALUES(@SettlementId,@TenantId,@BusinessId,@UserId,
                       @Basis,@Agreement,@Method,@BankAccountId,@Reference,
                       @Total,@Hash,@Now);
                INSERT sales.RestaurantTipSettlementCollections
                  (CollectionId,SettlementId,TipAmount)
                SELECT collection.CollectionId,@SettlementId,collection.TipAmount
                FROM OPENJSON(@CollectionIds) WITH(CollectionId uniqueidentifier '$') input
                JOIN sales.RestaurantAccountCollections collection
                  ON collection.CollectionId=input.CollectionId
                  AND collection.TenantId=@TenantId AND collection.BusinessId=@BusinessId;
                INSERT sales.RestaurantTipSettlementRecipients
                  (SettlementId,PartyId,Amount)
                SELECT @SettlementId,PartyId,Amount FROM OPENJSON(@Recipients)
                  WITH(PartyId uniqueidentifier '$.PartyId',Amount decimal(19,4) '$.Amount');
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@SettlementId", request.SettlementId);
                insert.Parameters.AddWithValue("@TenantId", actor.TenantId);
                insert.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
                insert.Parameters.AddWithValue("@UserId", actor.UserId);
                insert.Parameters.AddWithValue("@Basis", request.DistributionBasis);
                insert.Parameters.AddWithValue("@Agreement", (object?)request.AgreementReference ?? DBNull.Value);
                insert.Parameters.AddWithValue("@Method", request.PaymentMethodCode);
                insert.Parameters.AddWithValue("@BankAccountId", (object?)request.BankAccountId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@Reference", (object?)request.PaymentReference ?? DBNull.Value);
                insert.Parameters.AddWithValue("@Total", total);
                insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = requestHash;
                insert.Parameters.AddWithValue("@Now", now);
                insert.Parameters.Add("@CollectionIds", SqlDbType.NVarChar, -1).Value = collectionIds;
                insert.Parameters.Add("@Recipients", SqlDbType.NVarChar, -1).Value = recipientsJson;
                await insert.ExecuteNonQueryAsync(ct);
            }
            var payload = JsonSerializer.Serialize(new RestaurantTipSettlementAccountingPayload(
                request.SettlementId, actor.TenantId, actor.BusinessId,
                request.PaymentMethodCode, request.BankAccountId, total));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            await using (var accounting = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings settings
                          WHERE settings.TenantId=@TenantId AND settings.Status=N'Ready'
                            AND settings.EffectiveFrom<=CONVERT(date,@Now))
                BEGIN
                    INSERT dbo.AccountingSourceDocuments
                      (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                       PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                    VALUES(@SettlementId,N'RestaurantTipSettlement',@TenantId,@BusinessId,
                           @Payload,@Hash,@Now,@Now,1);
                    INSERT dbo.AccountingPostingJobs
                      (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                       SourceDocumentType,SourcePayloadHash,OccurredAt,Status,
                       AttemptCount,CreatedAt)
                    VALUES(NEWID(),@TenantId,@BusinessId,@SettlementId,
                           N'RestaurantTipSettlement',@Hash,@Now,N'Pending',0,@Now);
                END;
                """, connection, transaction))
            {
                accounting.Parameters.AddWithValue("@TenantId", actor.TenantId);
                accounting.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
                accounting.Parameters.AddWithValue("@SettlementId", request.SettlementId);
                accounting.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
                accounting.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
                accounting.Parameters.AddWithValue("@Now", now);
                await accounting.ExecuteNonQueryAsync(ct);
            }
            var result = new RestaurantTipSettlement(request.SettlementId,
                actor.BusinessId, total, request.DistributionBasis,
                request.AgreementReference, request.PaymentMethodCode,
                request.PaymentReference, now, shares);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new RestaurantAccountConflictException(
                "Otra liquidación tomó alguna propina seleccionada. Actualiza la lista.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static IReadOnlyList<RestaurantTipRecipientRequest> EqualTipShares(
        decimal total, IReadOnlyList<RestaurantTipRecipientRequest> recipients)
    {
        var ordered = recipients.OrderBy(item => item.PartyId).ToArray();
        var baseShare = decimal.Floor(total / ordered.Length);
        var remainder = (int)(total - baseShare * ordered.Length);
        return ordered.Select((item, index) => item with
        {
            Amount = baseShare + (index < remainder ? 1 : 0)
        }).ToArray();
    }

    private static async Task<RestaurantTipSettlement> LoadTipSettlementAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid settlementId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT TotalAmount,DistributionBasis,AgreementReference,
              PaymentMethodCode,PaymentReference,CreatedAt
            FROM sales.RestaurantTipSettlements
            WHERE SettlementId=@SettlementId AND BusinessId=@BusinessId
              AND TenantId=@TenantId;
            SELECT PartyId,Amount FROM sales.RestaurantTipSettlementRecipients
            WHERE SettlementId=@SettlementId ORDER BY PartyId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@SettlementId", settlementId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new RestaurantAccountConflictException("La liquidación no existe en esta sede.");
        var total = reader.GetDecimal(0);
        var basis = reader.GetString(1);
        var agreement = reader.IsDBNull(2) ? null : reader.GetString(2);
        var method = reader.GetString(3);
        var reference = reader.IsDBNull(4) ? null : reader.GetString(4);
        var created = reader.GetFieldValue<DateTimeOffset>(5);
        await reader.NextResultAsync(ct);
        var recipients = new List<RestaurantTipRecipientRequest>();
        while (await reader.ReadAsync(ct))
            recipients.Add(new(reader.GetGuid(0), reader.GetDecimal(1)));
        return new(settlementId, actor.BusinessId, total, basis, agreement,
            method, reference, created, recipients);
    }
}
