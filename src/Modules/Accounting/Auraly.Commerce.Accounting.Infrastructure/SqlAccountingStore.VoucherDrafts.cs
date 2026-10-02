using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<VoucherDraftView?> GetVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadVoucherDraftAsync(connection, null, user, documentId, cancellationToken);
    }

    public async Task<VoucherDraftView> SaveVoucherDraftAsync(AccountingUserIdentity user,
        SaveVoucherDraftRequest request, CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request with { RowVersion = null })));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var check = new SqlCommand("""
            SELECT TenantId,BusinessId,DocumentType,RowVersion,ContentHash,SentAt
            FROM accounting.VoucherDrafts WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@Id;
            """, connection, transaction))
        {
            check.Parameters.AddWithValue("@Id", request.DocumentId);
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetGuid(0) != user.TenantId || reader.GetGuid(1) != user.BusinessId ||
                    reader.GetString(2) != request.DocumentType || !reader.IsDBNull(5))
                    throw new AccountingConflictException("El comprobante no está disponible para edición.");
                if (((byte[])reader[4]).AsSpan().SequenceEqual(hash))
                {
                    await reader.DisposeAsync();
                    var replay = await ReadVoucherDraftAsync(connection, transaction, user, request.DocumentId, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return replay!;
                }
                if (request.RowVersion != Convert.ToBase64String((byte[])reader[3]))
                    throw new AccountingConflictException("Otra persona modificó el comprobante. Ábrelo nuevamente.");
            }
            else if (request.RowVersion is not null)
                throw new AccountingConflictException("El comprobante que intentas editar ya no está disponible.");
        }
        var dimensions = request.Lines.ToList();
        if (request.Adjustment is { } adjustment)
            dimensions.Add(new(adjustment.CounterpartAccountId, null, adjustment.CostCenterId, "", 0, 0));
        await ValidateVoucherDimensionsAsync(connection, transaction, user, dimensions, request.OccurredAt,
            request.ConceptCode, final: false, cancellationToken);
        await using (var save = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                THROW 51001, N'El documento ya fue aceptado y no admite una captura nueva.', 1;
            IF @SubledgerId IS NOT NULL AND NOT EXISTS(
                SELECT ReceivableId FROM dbo.Receivables WHERE @SubledgerKind=N'Receivable' AND ReceivableId=@SubledgerId AND BusinessId=@BusinessId
                UNION ALL SELECT PayableId FROM dbo.Payables WHERE @SubledgerKind=N'Payable' AND PayableId=@SubledgerId AND BusinessId=@BusinessId)
                THROW 51002, N'La obligación no pertenece a esta sede.', 1;
            IF EXISTS(SELECT 1 FROM accounting.VoucherDrafts WHERE DocumentId=@Id)
                UPDATE accounting.VoucherDrafts SET OccurredAt=@Date,ConceptCode=@Concept,Description=@Description,
                  Reference=@Reference,SubledgerKind=@SubledgerKind,SubledgerId=@SubledgerId,Direction=@Direction,
                  AdjustmentAmount=@Amount,CounterpartAccountId=@AccountId,CostCenterId=@CenterId,
                  UpdatedBy=@UserId,UpdatedAt=@Now,ContentHash=@Hash WHERE DocumentId=@Id;
            ELSE
                INSERT accounting.VoucherDrafts(DocumentId,TenantId,BusinessId,DocumentType,OccurredAt,ConceptCode,
                  Description,Reference,CurrencyCode,SubledgerKind,SubledgerId,Direction,AdjustmentAmount,
                  CounterpartAccountId,CostCenterId,CreatedBy,CreatedAt,UpdatedBy,UpdatedAt,ContentHash)
                SELECT @Id,@TenantId,@BusinessId,@Type,@Date,@Concept,@Description,@Reference,FunctionalCurrencyCode,
                  @SubledgerKind,@SubledgerId,@Direction,@Amount,@AccountId,@CenterId,@UserId,@Now,@UserId,@Now,@Hash
                FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId;
            IF NOT EXISTS(SELECT 1 FROM accounting.VoucherDrafts WHERE DocumentId=@Id)
                THROW 51002, N'Configura la moneda funcional de contabilidad antes de guardar.', 1;
            DELETE accounting.VoucherDraftLines WHERE DocumentId=@Id;
            INSERT accounting.VoucherDraftLines(DocumentId,LineNumber,AccountId,PartyId,CostCenterId,Description,Reference,Debit,Credit)
            SELECT @Id,LineNumber,AccountId,PartyId,CostCenterId,Description,Reference,Debit,Credit
            FROM OPENJSON(@Lines) WITH(LineNumber int,AccountId uniqueidentifier,PartyId uniqueidentifier,
              CostCenterId uniqueidentifier,Description nvarchar(500),Reference nvarchar(100),Debit decimal(19,4),Credit decimal(19,4));
            """, connection, transaction))
        {
            AddDraftScope(save, user, request.DocumentId);
            save.Parameters.AddWithValue("@Type", request.DocumentType);
            save.Parameters.AddWithValue("@Date", request.OccurredAt);
            save.Parameters.AddWithValue("@Concept", request.ConceptCode);
            save.Parameters.AddWithValue("@Description", request.Description);
            save.Parameters.AddWithValue("@Reference", (object?)request.Reference ?? DBNull.Value);
            save.Parameters.AddWithValue("@SubledgerKind", (object?)request.Adjustment?.SubledgerKind ?? DBNull.Value);
            save.Parameters.AddWithValue("@SubledgerId", (object?)request.Adjustment?.SubledgerId ?? DBNull.Value);
            save.Parameters.AddWithValue("@Direction", (object?)request.Adjustment?.Direction ?? DBNull.Value);
            save.Parameters.AddWithValue("@Amount", (object?)request.Adjustment?.Amount ?? DBNull.Value);
            save.Parameters.AddWithValue("@AccountId", (object?)request.Adjustment?.CounterpartAccountId ?? DBNull.Value);
            save.Parameters.AddWithValue("@CenterId", (object?)request.Adjustment?.CostCenterId ?? DBNull.Value);
            save.Parameters.AddWithValue("@UserId", user.UserId);
            save.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            save.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
            save.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(request.Lines.Select((line, index) =>
                new { LineNumber = index + 1, line.AccountId, line.PartyId, line.CostCenterId, line.Description, line.Reference, line.Debit, line.Credit }));
            try { await save.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqlException error) when (error.Number == 51001) { throw new AccountingConflictException(error.Message); }
            catch (SqlException error) when (error.Number == 51002) { throw new AccountingValidationException(error.Message); }
        }
        var result = await ReadVoucherDraftAsync(connection, transaction, user, request.DocumentId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result!;
    }

    private static async Task<VoucherDraftView?> ReadVoucherDraftAsync(SqlConnection connection, SqlTransaction? transaction,
        AccountingUserIdentity user, Guid documentId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT d.DocumentType,d.OccurredAt,d.ConceptCode,d.Description,d.Reference,d.CurrencyCode,d.RowVersion,d.SentAt,
              d.SubledgerKind,d.SubledgerId,d.Direction,d.AdjustmentAmount,d.CounterpartAccountId,d.CostCenterId,
              COALESCE(j.Status,N'Created'),j.AttemptCount,j.LastErrorCode,j.LastErrorMessage,e.EntryId,e.EntryNumber,e.PostedAt,
              e.DebitTotal,e.CreditTotal
            FROM accounting.VoucherDrafts d
            LEFT JOIN dbo.AccountingPostingJobs j ON j.SourceDocumentId=d.DocumentId AND j.SourceDocumentType=d.DocumentType AND j.BusinessId=d.BusinessId
            LEFT JOIN dbo.AccountingEntries e ON e.SourceDocumentId=d.DocumentId AND e.SourceDocumentType=d.DocumentType AND e.TenantId=d.TenantId
            WHERE d.DocumentId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId;
            SELECT l.AccountId,l.PartyId,l.CostCenterId,l.Description,l.Debit,l.Credit,l.Reference,
              a.Code,a.Name,p.DisplayName,p.Identification,c.Name
            FROM accounting.VoucherDraftLines l
            JOIN accounting.VoucherDrafts d ON d.DocumentId=l.DocumentId
            LEFT JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=d.TenantId
            LEFT JOIN dbo.Parties p ON p.PartyId=l.PartyId AND p.TenantId=d.TenantId
            LEFT JOIN dbo.AccountingCostCenters c ON c.CostCenterId=l.CostCenterId AND c.BusinessId=d.BusinessId
            WHERE d.DocumentId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId ORDER BY l.LineNumber;
            """, connection, transaction);
        AddDraftScope(command, user, documentId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var type = reader.GetString(0);
        var date = reader.GetDateTimeOffset(1);
        var concept = reader.GetString(2);
        var description = reader.GetString(3);
        var reference = NullableText(reader, 4);
        var currency = reader.GetString(5);
        var version = Convert.ToBase64String((byte[])reader[6]);
        var sent = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(7);
        var adjustment = reader.IsDBNull(9) ? null : new VoucherDraftAdjustment(reader.GetString(8), reader.GetGuid(9),
            reader.GetString(10), reader.GetDecimal(11), NullableId(reader, 12), NullableId(reader, 13));
        var status = reader.GetString(14);
        var row = new AccountingDocumentRow(documentId, type, reference, date, status, reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
            NullableText(reader, 16), NullableText(reader, 17), NullableId(reader, 18), NullableText(reader, 19),
            reader.IsDBNull(21) ? null : reader.GetDecimal(21), reader.IsDBNull(22) ? null : reader.GetDecimal(22),
            reader.IsDBNull(20) ? null : reader.GetDateTimeOffset(20), null, null, null, null, null, true);
        var lines = new List<VoucherDraftLineView>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
            lines.Add(new(new(NullableId(reader, 0), NullableId(reader, 1), NullableId(reader, 2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetDecimal(5), NullableText(reader, 6)), NullableText(reader, 7),
                NullableText(reader, 8), NullableText(reader, 9), NullableText(reader, 10), NullableText(reader, 11)));
        if (sent is null) row = row with { DebitTotal = adjustment?.Amount ?? lines.Sum(line => line.Value.Debit), CreditTotal = adjustment?.Amount ?? lines.Sum(line => line.Value.Credit) };
        return new(documentId, type, date, concept, description, reference, currency, lines, adjustment, version, sent, status,
            sent is null && user.Permissions.Contains(AccountingPermissionCodes.ManualCreate),
            sent is null && user.Permissions.Contains(AccountingPermissionCodes.ManualSend), row);
    }

    private static string? NullableText(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? NullableId(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static void AddDraftScope(SqlCommand command, AccountingUserIdentity user, Guid id)
    {
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
    }

    private static async Task ValidateVoucherDimensionsAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, IReadOnlyList<VoucherDraftLine> lines, DateTimeOffset occurredAt,
        string conceptCode, bool final, CancellationToken token, bool validateDraftReadiness = true)
    {
        await using var command = new SqlCommand("""
            SELECT CASE
              WHEN NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                THEN N'La sede no pertenece a la empresa.'
              WHEN @Strict=1 AND NOT EXISTS(SELECT 1 FROM reference.Options WHERE CatalogCode=N'accounting-manual-concept' AND Code=@Concept AND IsActive=1)
                THEN N'El concepto contable no está disponible.'
              WHEN @Strict=1 AND @Final=1 AND NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods WHERE TenantId=@TenantId AND Status=N'Open'
                AND CONVERT(date,@Date) BETWEEN StartsOn AND EndsOn)
                THEN N'No hay un período contable abierto para esta fecha.'
              WHEN EXISTS(SELECT 1 FROM OPENJSON(@Lines) WITH(AccountId uniqueidentifier,PartyId uniqueidentifier,CostCenterId uniqueidentifier) l
                LEFT JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId AND a.IsActive=1 AND a.AllowsPosting=1
                LEFT JOIN dbo.Parties p ON p.PartyId=l.PartyId AND p.TenantId=@TenantId AND p.IsActive=1
                LEFT JOIN dbo.AccountingCostCenters c ON c.CostCenterId=l.CostCenterId AND c.BusinessId=@BusinessId AND c.IsActive=1
                WHERE (l.AccountId IS NOT NULL AND a.AccountId IS NULL) OR (l.PartyId IS NOT NULL AND p.PartyId IS NULL)
                  OR (l.CostCenterId IS NOT NULL AND c.CostCenterId IS NULL)
                  OR (@Final=1 AND (a.AccountId IS NULL OR (a.RequiresParty=1 AND l.PartyId IS NULL))))
                THEN N'Una partida tiene cuenta, tercero o centro inválido, o le falta el tercero requerido.'
              WHEN @Final=1 AND EXISTS(SELECT 1 FROM (VALUES(N'AccountsReceivable'),(N'AccountsPayable')) category(Code)
                CROSS APPLY(SELECT TOP(1) m.AccountId FROM dbo.AccountingAccountMappings m
                  WHERE m.TenantId=@TenantId AND m.Category=category.Code AND (m.BusinessId=@BusinessId OR m.BusinessId IS NULL)
                    AND m.EffectiveFrom<=CONVERT(date,@Date) AND (m.EffectiveTo IS NULL OR m.EffectiveTo>=CONVERT(date,@Date))
                  ORDER BY CASE WHEN m.BusinessId=@BusinessId THEN 0 ELSE 1 END,m.EffectiveFrom DESC) effective
                JOIN OPENJSON(@Lines) WITH(AccountId uniqueidentifier) l ON l.AccountId=effective.AccountId)
                THEN N'Las cuentas de cartera requieren su operación de cuentas por cobrar o pagar.'
              ELSE NULL END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Date", occurredAt);
        command.Parameters.AddWithValue("@Concept", conceptCode);
        command.Parameters.AddWithValue("@Final", final);
        command.Parameters.AddWithValue("@Strict", validateDraftReadiness);
        command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
        var error = await command.ExecuteScalarAsync(token);
        if (error is string message) throw new AccountingValidationException(message);
    }
}
