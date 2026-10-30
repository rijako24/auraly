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

    public async Task<bool> DeleteVoucherDraftAsync(AccountingUserIdentity user,
        Guid documentId, string rowVersion, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await using var command = new SqlCommand("""
            DECLARE @Version binary(8), @SentAt datetimeoffset(7);
            SELECT @Version=RowVersion,@SentAt=SentAt FROM accounting.VoucherDrafts WITH(UPDLOCK,HOLDLOCK)
            WHERE DocumentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
            IF @Version IS NULL SELECT CAST(0 AS bit);
            ELSE IF @SentAt IS NOT NULL OR EXISTS(
                SELECT 1 FROM dbo.AccountingSourceDocuments
                WHERE SourceDocumentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId)
                THROW 51001,N'El comprobante ya fue aceptado y no se puede descartar.',1;
            ELSE IF @Version<>@ExpectedVersion
                THROW 51002,N'El borrador cambió. Ábrelo nuevamente antes de descartarlo.',1;
            ELSE BEGIN
                DELETE accounting.VoucherDraftLines WHERE DocumentId=@Id;
                DELETE accounting.VoucherDrafts WHERE DocumentId=@Id
                  AND TenantId=@TenantId AND BusinessId=@BusinessId AND SentAt IS NULL
                  AND RowVersion=@ExpectedVersion;
                IF @@ROWCOUNT<>1 THROW 51002,N'El borrador cambió antes de descartarlo.',1;
                SELECT CAST(1 AS bit);
            END;
            """, connection, transaction);
        AddDraftScope(command, user, documentId);
        command.Parameters.Add("@ExpectedVersion", SqlDbType.Binary, 8).Value = Convert.FromBase64String(rowVersion);
        try
        {
            var deleted = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        catch (SqlException error) when (error.Number is 51001 or 51002)
        {
            throw new AccountingConflictException(error.Message);
        }
    }

    public async Task<VoucherDraftView> SaveVoucherDraftAsync(AccountingUserIdentity user,
        SaveVoucherDraftRequest request, CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request with { RowVersion = null })));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var check = new SqlCommand("""
            SELECT TenantId,BusinessId,DocumentType,RowVersion,ContentHash,SentAt,ClonedFromDocumentId,PayloadVersion
            FROM accounting.VoucherDrafts WITH(UPDLOCK,HOLDLOCK) WHERE DocumentId=@Id;
            IF @@ROWCOUNT=0
              SELECT SourceDocumentId FROM dbo.AccountingSourceDocuments WITH(UPDLOCK,HOLDLOCK)
              WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;
            """, connection, transaction))
        {
            check.Parameters.AddWithValue("@Id", request.DocumentId);
            check.Parameters.AddWithValue("@Type", request.DocumentType);
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetGuid(0) != user.TenantId || reader.GetGuid(1) != user.BusinessId ||
                    reader.GetString(2) != request.DocumentType || !reader.IsDBNull(5) || NullableId(reader, 6) != request.ClonedFromDocumentId)
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
                var storedPayloadVersion = reader.IsDBNull(7) ? (int?)null : reader.GetByte(7);
                var upgradingUnsentManualVoucher = request.DocumentType == AccountingManualDocumentTypes.ManualVoucher &&
                    (storedPayloadVersion is null && request.PayloadVersion is 2 or 4 ||
                     storedPayloadVersion == 2 && request.PayloadVersion == 4);
                if (request.PayloadVersion != storedPayloadVersion && !upgradingUnsentManualVoucher)
                    throw new AccountingConflictException("El comprobante requiere conservar la versión e identidades de sus partidas.");
            }
            else if (request.RowVersion is not null)
                throw new AccountingConflictException("El comprobante que intentas editar ya no está disponible.");
            else if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
                throw new AccountingConflictException("Ese comprobante ya fue aceptado. No se puede crear otra captura editable con su identidad.");
        }
        var dimensions = request.Lines.ToList();
        if (request.Adjustment is { } adjustment)
            dimensions.Add(new(adjustment.CounterpartAccountId, null, adjustment.CostCenterId, "", 0, 0));
        await ValidateVoucherDimensionsAsync(connection, transaction, user, dimensions, request.OccurredAt,
            request.ConceptCode, final: false, cancellationToken, mainPartyId: request.MainPartyId);
        await using (var save = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                THROW 51001, N'El documento ya fue aceptado y no admite una captura nueva.', 1;
            IF @SessionId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.WorkSessions
                WHERE WorkSessionId=@SessionId AND TenantId=@TenantId AND BusinessId=@BusinessId AND UserId=@UserId)
                THROW 51002,N'La sesión no pertenece al usuario y sede del comprobante.',1;
            IF @VoucherType IS NOT NULL AND NOT EXISTS(SELECT 1 FROM reference.Options
                WHERE CatalogCode=N'accounting-voucher-type' AND Code=@VoucherType AND IsActive=1)
                THROW 51002,N'El tipo de comprobante no está disponible.',1;
            IF @ClonedFromId IS NOT NULL AND NOT EXISTS(
                SELECT DocumentId FROM accounting.VoucherDrafts WHERE DocumentId=@ClonedFromId AND TenantId=@TenantId AND BusinessId=@BusinessId
                UNION ALL SELECT SourceDocumentId FROM dbo.AccountingEntries WHERE SourceDocumentId=@ClonedFromId AND TenantId=@TenantId AND BusinessId=@BusinessId)
                THROW 51002, N'El comprobante de origen no está disponible en esta sede.', 1;
            IF @SubledgerId IS NOT NULL AND NOT EXISTS(
                SELECT ReceivableId FROM dbo.Receivables WHERE @SubledgerKind=N'Receivable' AND ReceivableId=@SubledgerId AND BusinessId=@BusinessId
                UNION ALL SELECT PayableId FROM dbo.Payables WHERE @SubledgerKind=N'Payable' AND PayableId=@SubledgerId AND BusinessId=@BusinessId)
                THROW 51002, N'La obligación no pertenece a esta sede.', 1;
            IF EXISTS(SELECT 1 FROM accounting.VoucherDrafts WHERE DocumentId=@Id)
                UPDATE accounting.VoucherDrafts SET OccurredAt=@Date,ConceptCode=@Concept,Description=@Description,PayloadVersion=@PayloadVersion,
                  VoucherTypeCode=@VoucherType,WorkSessionId=@SessionId,FiscalCaptureJson=@FiscalCapture,
                  Reference=@Reference,MainPartyId=@MainPartyId,SubledgerKind=@SubledgerKind,SubledgerId=@SubledgerId,Direction=@Direction,
                  AdjustmentAmount=@Amount,CounterpartAccountId=@AccountId,CostCenterId=@CenterId,
                  UpdatedBy=@UserId,UpdatedAt=@Now,ContentHash=@Hash WHERE DocumentId=@Id;
            ELSE
                INSERT accounting.VoucherDrafts(DocumentId,TenantId,BusinessId,DocumentType,OccurredAt,ConceptCode,
                  Description,Reference,MainPartyId,ClonedFromDocumentId,PayloadVersion,VoucherTypeCode,WorkSessionId,FiscalCaptureJson,CurrencyCode,SubledgerKind,SubledgerId,Direction,AdjustmentAmount,
                  CounterpartAccountId,CostCenterId,CreatedBy,CreatedAt,UpdatedBy,UpdatedAt,ContentHash)
                SELECT @Id,@TenantId,@BusinessId,@Type,@Date,@Concept,@Description,@Reference,@MainPartyId,@ClonedFromId,@PayloadVersion,@VoucherType,@SessionId,@FiscalCapture,FunctionalCurrencyCode,
                  @SubledgerKind,@SubledgerId,@Direction,@Amount,@AccountId,@CenterId,@UserId,@Now,@UserId,@Now,@Hash
                FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId;
            IF NOT EXISTS(SELECT 1 FROM accounting.VoucherDrafts WHERE DocumentId=@Id)
                THROW 51002, N'Configura la moneda funcional de contabilidad antes de guardar.', 1;
            DELETE accounting.VoucherDraftLines WHERE DocumentId=@Id;
            INSERT accounting.VoucherDraftLines(DocumentId,LineNumber,LineId,AccountId,PartyId,CostCenterId,Description,Reference,Debit,Credit,BaseAmount,
              TargetSubledgerKind,TargetSubledgerId,FinancialGroupId,DueDate,PartySiteId,CardFranchiseCode,ApprovalNumber,TenderedAmount,PaymentNotes,ReversesTransactionId,FinancialDiscountEarned)
            SELECT @Id,LineNumber,LineId,AccountId,PartyId,CostCenterId,Description,Reference,Debit,Credit,BaseAmount,
              TargetSubledgerKind,TargetSubledgerId,FinancialGroupId,DueDate,PartySiteId,CardFranchiseCode,ApprovalNumber,TenderedAmount,PaymentNotes,ReversesTransactionId,FinancialDiscountEarned
            FROM OPENJSON(@Lines) WITH(LineNumber int,LineId uniqueidentifier,AccountId uniqueidentifier,PartyId uniqueidentifier,
              CostCenterId uniqueidentifier,Description nvarchar(500),Reference nvarchar(100),Debit decimal(19,4),Credit decimal(19,4),BaseAmount decimal(19,4),
              TargetSubledgerKind nvarchar(16),TargetSubledgerId uniqueidentifier,FinancialGroupId uniqueidentifier,DueDate datetimeoffset(7),
              PartySiteId uniqueidentifier,CardFranchiseCode nvarchar(64),ApprovalNumber nvarchar(100),TenderedAmount decimal(19,4),PaymentNotes nvarchar(500),ReversesTransactionId uniqueidentifier,FinancialDiscountEarned bit);
            """, connection, transaction))
        {
            AddDraftScope(save, user, request.DocumentId);
            save.Parameters.AddWithValue("@Type", request.DocumentType);
            save.Parameters.Add("@VoucherType",SqlDbType.NVarChar,64).Value=(object?)request.VoucherTypeCode??DBNull.Value;
            save.Parameters.Add("@SessionId",SqlDbType.UniqueIdentifier).Value=(object?)request.WorkSessionId??DBNull.Value;
            save.Parameters.Add("@FiscalCapture", SqlDbType.NVarChar, -1).Value = request.FiscalCapture is null
                ? DBNull.Value : JsonSerializer.Serialize(request.FiscalCapture);
            save.Parameters.Add("@PayloadVersion", SqlDbType.TinyInt).Value = (object?)request.PayloadVersion ?? DBNull.Value;
            save.Parameters.AddWithValue("@Date", request.OccurredAt);
            save.Parameters.AddWithValue("@Concept", request.ConceptCode);
            save.Parameters.AddWithValue("@Description", request.Description);
            save.Parameters.AddWithValue("@Reference", (object?)request.Reference ?? DBNull.Value);
            save.Parameters.AddWithValue("@MainPartyId", (object?)request.MainPartyId ?? DBNull.Value);
            save.Parameters.AddWithValue("@ClonedFromId", (object?)request.ClonedFromDocumentId ?? DBNull.Value);
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
                new { LineNumber = index + 1, line.LineId, line.AccountId, line.PartyId, line.CostCenterId, line.Description, line.Reference, line.Debit, line.Credit, line.BaseAmount,
                    TargetSubledgerKind=line.ExistingPosition?.SubledgerKind,TargetSubledgerId=line.ExistingPosition?.SubledgerId,
                    line.FinancialGroupId,line.DueDate,line.PartySiteId,CardFranchiseCode=line.PaymentEvidence?.CardFranchiseCode,
                    ApprovalNumber=line.PaymentEvidence?.ApprovalNumber,TenderedAmount=line.PaymentEvidence?.TenderedAmount,PaymentNotes=line.PaymentEvidence?.Notes,
                    ReversesTransactionId=line.ExistingPosition?.ReversesTransactionId,line.FinancialDiscountEarned }));
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
              e.DebitTotal,e.CreditTotal,d.MainPartyId,mainParty.DisplayName,mainParty.Identification,d.ClonedFromDocumentId,d.PayloadVersion,JSON_VALUE(source.PayloadJson,'$.Reference'),d.VoucherTypeCode,d.WorkSessionId,JSON_QUERY(source.PayloadJson,'$.FinancialEffects'),d.FiscalCaptureJson,
              fiscal.FiscalDocumentType,fiscal.FiscalNumber,fiscal.UniqueCodeType,fiscal.UniqueCode,fiscal.FiscalStatus,
              reversal.SourceDocumentId,reversalJob.Status
            FROM accounting.VoucherDrafts d
            LEFT JOIN dbo.Parties mainParty ON mainParty.PartyId=d.MainPartyId AND mainParty.TenantId=d.TenantId
            LEFT JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=d.DocumentId
              AND source.SourceDocumentType=d.DocumentType AND source.BusinessId=d.BusinessId AND source.TenantId=d.TenantId
            LEFT JOIN dbo.AccountingPostingJobs j ON j.SourceDocumentId=d.DocumentId AND j.SourceDocumentType=d.DocumentType AND j.BusinessId=d.BusinessId
            LEFT JOIN dbo.AccountingEntries e ON e.SourceDocumentId=d.DocumentId AND e.SourceDocumentType=d.DocumentType AND e.TenantId=d.TenantId
            LEFT JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=d.DocumentId
              AND fiscal.BusinessId=d.BusinessId AND fiscal.SourceDocumentType=d.DocumentType
            LEFT JOIN dbo.AccountingSourceDocuments reversal ON reversal.ReversesDocumentId=d.DocumentId
              AND reversal.TenantId=d.TenantId AND reversal.BusinessId=d.BusinessId
            LEFT JOIN dbo.AccountingPostingJobs reversalJob ON reversalJob.SourceDocumentId=reversal.SourceDocumentId
              AND reversalJob.SourceDocumentType=reversal.SourceDocumentType
              AND reversalJob.TenantId=d.TenantId AND reversalJob.BusinessId=d.BusinessId
            WHERE d.DocumentId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId;
            SELECT l.AccountId,l.PartyId,l.CostCenterId,l.Description,l.Debit,l.Credit,l.Reference,
              a.Code,a.Name,p.DisplayName,p.Identification,c.Name,
              CASE WHEN l.AccountId IS NOT NULL AND (a.AccountId IS NULL OR a.IsActive=0 OR a.AllowsPosting=0 OR (a.BusinessId IS NOT NULL AND a.BusinessId<>@BusinessId))
                THEN N'La cuenta ya no está disponible en esta sede.'
                WHEN l.PartyId IS NOT NULL AND (p.PartyId IS NULL OR p.IsActive=0) THEN N'El tercero de la partida no está activo.'
                WHEN l.CostCenterId IS NOT NULL AND (c.CostCenterId IS NULL OR c.IsActive=0) THEN N'El centro de costo no está activo.' END,l.LineId,l.BaseAmount,l.TargetSubledgerKind,l.TargetSubledgerId,l.FinancialGroupId,l.DueDate,l.PartySiteId,
              l.CardFranchiseCode,l.ApprovalNumber,l.TenderedAmount,l.PaymentNotes,l.ReversesTransactionId,l.FinancialDiscountEarned
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
        var mainPartyId = NullableId(reader, 23);
        var mainPartyName = NullableText(reader, 24);
        var mainPartyIdentification = NullableText(reader, 25);
        var clonedFromDocumentId = NullableId(reader, 26);
        var payloadVersion = reader.IsDBNull(27) ? (int?)null : reader.GetByte(27);
        var acceptedReference = NullableText(reader, 28);
        var voucherType = NullableText(reader, 29);
        var workSessionId = NullableId(reader, 30);
        var effectsJson = NullableText(reader, 31);
        var financialEffects = effectsJson is null ? null : JsonSerializer.Deserialize<VoucherFinancialEffects>(effectsJson)
            ?? throw new InvalidOperationException("The accepted financial effects are invalid.");
        var fiscalJson = NullableText(reader, 32);
        var fiscalCapture = fiscalJson is null ? null : JsonSerializer.Deserialize<VoucherFiscalCapture>(fiscalJson)
            ?? throw new InvalidOperationException("The fiscal capture is invalid.");
        var reversalDocumentId = NullableId(reader, 38);
        var reversalStatus = NullableText(reader, 39);
        var row = new AccountingDocumentRow(documentId, type, reference, date, status, reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
            NullableText(reader, 16), NullableText(reader, 17), NullableId(reader, 18), NullableText(reader, 19),
            reader.IsDBNull(21) ? null : reader.GetDecimal(21), reader.IsDBNull(22) ? null : reader.GetDecimal(22),
            reader.IsDBNull(20) ? null : reader.GetDateTimeOffset(20), NullableText(reader, 33), NullableText(reader, 34),
            NullableText(reader, 35), NullableText(reader, 36), NullableText(reader, 37), true,
            mainPartyId, mainPartyName, mainPartyIdentification, reversalDocumentId, reversalStatus);
        var lines = new List<VoucherDraftLineView>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
            lines.Add(new(new(NullableId(reader, 0), NullableId(reader, 1), NullableId(reader, 2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetDecimal(5), NullableText(reader, 6), NullableId(reader, 13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                reader.IsDBNull(16)?null:new AccountingOpeningPositionReference(reader.GetString(15),reader.GetGuid(16),NullableId(reader,24)),
                NullableId(reader,17),reader.IsDBNull(18)?null:reader.GetDateTimeOffset(18),NullableId(reader,19),
                reader.IsDBNull(20)&&reader.IsDBNull(21)&&reader.IsDBNull(22)&&reader.IsDBNull(23)?null:
                  new VoucherPaymentEvidence(NullableText(reader,20),NullableText(reader,21),reader.IsDBNull(22)?null:reader.GetDecimal(22),NullableText(reader,23)),
                reader.IsDBNull(25)?null:reader.GetBoolean(25)), NullableText(reader, 7),
                NullableText(reader, 8), NullableText(reader, 9), NullableText(reader, 10), NullableText(reader, 11), NullableText(reader, 12)));
        if (sent is null) row = row with { DebitTotal = adjustment?.Amount ?? lines.Sum(line => line.Value.Debit), CreditTotal = adjustment?.Amount ?? lines.Sum(line => line.Value.Credit) };
        return new(documentId, type, date, concept, description, reference, currency, lines, adjustment, version, sent, status,
            sent is null && user.Permissions.Contains(AccountingPermissionCodes.ManualCreate),
            sent is null && user.Permissions.Contains(AccountingPermissionCodes.ManualSend), row,
            mainPartyId, mainPartyName, mainPartyIdentification, clonedFromDocumentId, payloadVersion, acceptedReference, voucherType, workSessionId, financialEffects, fiscalCapture,
            reversalDocumentId, reversalStatus);
    }

    private static string? NullableText(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? NullableId(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static void AddDraftScope(SqlCommand command, AccountingUserIdentity user, Guid id)
    {
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
    }

    private static async Task<VoucherFinancialContext?> ValidateVoucherDimensionsAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, IReadOnlyList<VoucherDraftLine> lines, DateTimeOffset occurredAt,
        string conceptCode, bool final, CancellationToken token, bool validateDraftReadiness = true, Guid? mainPartyId = null,
        bool financialCapture = false, string? voucherTypeCode = null, Guid? workSessionId = null)
    {
        await using var command = new SqlCommand("""
            DECLARE @RequestedLines TABLE(AccountId uniqueidentifier,PartyId uniqueidentifier,CostCenterId uniqueidentifier,
              PartySiteId uniqueidentifier,TargetKind nvarchar(16),TargetId uniqueidentifier,
              GroupId uniqueidentifier,ReversesTransactionId uniqueidentifier);
            INSERT @RequestedLines
            SELECT AccountId,PartyId,CostCenterId,PartySiteId,TargetKind,TargetId,GroupId,ReversesTransactionId FROM OPENJSON(@Lines)
              WITH(AccountId uniqueidentifier,PartyId uniqueidentifier,CostCenterId uniqueidentifier,PartySiteId uniqueidentifier,
                TargetKind nvarchar(16) '$.ExistingPosition.SubledgerKind',TargetId uniqueidentifier '$.ExistingPosition.SubledgerId',
                GroupId uniqueidentifier '$.FinancialGroupId',
                ReversesTransactionId uniqueidentifier '$.ExistingPosition.ReversesTransactionId');
            DECLARE @ControlAccounts TABLE(AccountId uniqueidentifier,Kind nvarchar(16));
            IF @Final=1
              INSERT @ControlAccounts SELECT effective.AccountId,category.Kind
              FROM (VALUES(N'AccountsReceivable',N'Receivable'),(N'AccountsPayable',N'Payable')) category(Code,Kind)
              CROSS APPLY(SELECT TOP(1) m.AccountId FROM dbo.AccountingAccountMappings m
                WHERE m.TenantId=@TenantId AND m.Category=category.Code AND (m.BusinessId=@BusinessId OR m.BusinessId IS NULL)
                  AND m.EffectiveFrom<=CONVERT(date,@Date) AND (m.EffectiveTo IS NULL OR m.EffectiveTo>=CONVERT(date,@Date))
                ORDER BY CASE WHEN m.BusinessId=@BusinessId THEN 0 ELSE 1 END,m.EffectiveFrom DESC) effective;
            SELECT CASE
              WHEN NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                THEN N'La sede no pertenece a la empresa.'
              WHEN @MainPartyId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Parties WHERE PartyId=@MainPartyId AND TenantId=@TenantId AND (@Final=0 OR IsActive=1))
                THEN N'El tercero principal no está activo o no pertenece a esta empresa.'
              WHEN @Strict=1 AND NOT EXISTS(SELECT 1 FROM reference.Options WHERE CatalogCode=N'accounting-manual-concept' AND Code=@Concept AND IsActive=1)
                THEN N'El concepto contable no está disponible.'
              WHEN @Strict=1 AND @Final=1 AND NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods WHERE TenantId=@TenantId AND Status=N'Open'
                AND CONVERT(date,@Date) BETWEEN StartsOn AND EndsOn)
                THEN N'No hay un período contable abierto para esta fecha.'
              WHEN @FinancialCapture=1 AND NOT EXISTS(SELECT 1 FROM reference.Options
                WHERE CatalogCode=N'accounting-voucher-type' AND Code=@VoucherType AND IsActive=1)
                THEN N'El tipo de comprobante ya no está disponible.'
              WHEN @FinancialCapture=1 AND @Final=1 AND @SessionId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM worksessions.OpenSessionForUser(@TenantId,@BusinessId,@UserId,@SessionId) WHERE DeviceId IS NULL)
                THEN N'Vincula la sesión web abierta del usuario y sede. La sesión de un dispositivo preparado conserva su captura local.'
              WHEN EXISTS(SELECT 1 FROM @RequestedLines l
                LEFT JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId AND (@Final=0 OR a.IsActive=1) AND a.AllowsPosting=1
                  AND (a.BusinessId IS NULL OR a.BusinessId=@BusinessId)
                LEFT JOIN dbo.Parties p ON p.PartyId=l.PartyId AND p.TenantId=@TenantId AND (@Final=0 OR p.IsActive=1)
                LEFT JOIN dbo.AccountingCostCenters c ON c.CostCenterId=l.CostCenterId AND c.BusinessId=@BusinessId AND (@Final=0 OR c.IsActive=1)
                LEFT JOIN dbo.PartySites site ON site.PartySiteId=l.PartySiteId AND site.PartyId=l.PartyId AND (@Final=0 OR site.IsActive=1)
                WHERE (l.AccountId IS NOT NULL AND a.AccountId IS NULL) OR (l.PartyId IS NOT NULL AND p.PartyId IS NULL)
                  OR (l.CostCenterId IS NOT NULL AND c.CostCenterId IS NULL) OR (l.PartySiteId IS NOT NULL AND site.PartySiteId IS NULL)
                  OR (@Final=1 AND (a.AccountId IS NULL OR (a.RequiresParty=1 AND l.PartyId IS NULL))))
                THEN N'Una partida tiene cuenta, tercero, sede del tercero o centro inválido, o le falta el tercero requerido.'
              WHEN @Final=0 AND EXISTS(SELECT 1 FROM @RequestedLines l WHERE l.TargetId IS NOT NULL AND NOT EXISTS(
                SELECT PayableId FROM dbo.Payables WHERE l.TargetKind=N'Payable' AND PayableId=l.TargetId AND BusinessId=@BusinessId
                UNION ALL SELECT ReceivableId FROM dbo.Receivables WHERE l.TargetKind=N'Receivable' AND ReceivableId=l.TargetId AND BusinessId=@BusinessId
                UNION ALL SELECT SupplierCreditId FROM dbo.SupplierCredits WHERE l.TargetKind=N'SupplierCredit' AND SupplierCreditId=l.TargetId AND BusinessId=@BusinessId
                UNION ALL SELECT CustomerCreditId FROM dbo.CustomerCredits WHERE l.TargetKind=N'CustomerCredit' AND CustomerCreditId=l.TargetId AND BusinessId=@BusinessId))
                THEN N'Una posición vinculada no pertenece a la sede del comprobante.'
              WHEN @Final=1 AND @FinancialCapture=0 AND EXISTS(SELECT 1 FROM @RequestedLines l
                JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId
                WHERE a.FinancialFunction IS NOT NULL
                  AND a.FinancialFunction NOT IN(N'General',N'UnallocatedPurchaseCost'))
                THEN N'El comprobante no se contabilizó ni se registró un pago. Las cuentas de cartera, anticipos y tesorería requieren captura financiera y su efecto vinculado.'
              WHEN @Final=1 AND @FinancialCapture=0 AND EXISTS(SELECT 1 FROM @ControlAccounts effective
                JOIN @RequestedLines l ON l.AccountId=effective.AccountId)
                THEN N'Las cuentas de cartera requieren su operación de cuentas por cobrar o pagar.'
              ELSE NULL END OPTION(RECOMPILE);
            IF @FinancialCapture=1
            BEGIN
              -- A posted native cash payment can predate PUC financial semantics.
              -- Its immutable tender is sufficient evidence only to reverse that
              -- same payment, in the same financial group and account.
              DECLARE @LegacyCashReturns TABLE(AccountId uniqueidentifier,GroupId uniqueidentifier,
                PRIMARY KEY(AccountId,GroupId));
              INSERT @LegacyCashReturns
              SELECT DISTINCT tenderLine.AccountId,tenderLine.GroupId
              FROM @RequestedLines tenderLine JOIN @RequestedLines target
                ON target.GroupId=tenderLine.GroupId AND target.ReversesTransactionId IS NOT NULL
              JOIN dbo.ReceivableTransactions movement
                ON movement.ReceivableTransactionId=target.ReversesTransactionId
                  AND movement.ReceivableId=target.TargetId AND target.TargetKind=N'Receivable'
                  AND movement.TransactionType=N'Payment'
              JOIN dbo.CustomerPayments payment ON payment.PaymentId=movement.SourceDocumentId
                AND payment.BusinessId=@BusinessId AND payment.Status=N'Processed'
              JOIN dbo.CustomerPaymentTenders tender ON tender.PaymentId=payment.PaymentId
                AND tender.AccountId=tenderLine.AccountId AND tender.MethodCode=N'Cash'
              JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=payment.PaymentId
                AND job.SourceDocumentType=N'ReceivablePayment' AND job.BusinessId=@BusinessId
                AND job.TenantId=@TenantId AND job.Status=N'Posted'
              JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
                AND entry.SourceDocumentType=job.SourceDocumentType AND entry.BusinessId=@BusinessId
                AND entry.TenantId=@TenantId AND entry.SourcePayloadHash=job.SourcePayloadHash
              JOIN dbo.AccountingEntryLines journal ON journal.EntryId=entry.EntryId
                AND journal.AccountId=tender.AccountId AND journal.Debit>0 AND journal.Credit=0
              WHERE tenderLine.TargetId IS NULL AND tenderLine.GroupId IS NOT NULL
              UNION
              SELECT DISTINCT tenderLine.AccountId,tenderLine.GroupId
              FROM @RequestedLines tenderLine JOIN @RequestedLines target
                ON target.GroupId=tenderLine.GroupId AND target.ReversesTransactionId IS NOT NULL
              JOIN dbo.PayableTransactions movement
                ON movement.PayableTransactionId=target.ReversesTransactionId
                  AND movement.PayableId=target.TargetId AND target.TargetKind=N'Payable'
                  AND movement.TransactionType=N'Payment'
              JOIN dbo.SupplierPayments payment ON payment.PaymentId=movement.SourceDocumentId
                AND payment.BusinessId=@BusinessId AND payment.Status=N'Processed'
              JOIN dbo.SupplierPaymentTenders tender ON tender.PaymentId=payment.PaymentId
                AND tender.AccountId=tenderLine.AccountId AND tender.MethodCode=N'Cash'
              JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=payment.PaymentId
                AND job.SourceDocumentType=N'PayablePayment' AND job.BusinessId=@BusinessId
                AND job.TenantId=@TenantId AND job.Status=N'Posted'
              JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=job.SourceDocumentId
                AND entry.SourceDocumentType=job.SourceDocumentType AND entry.BusinessId=@BusinessId
                AND entry.TenantId=@TenantId AND entry.SourcePayloadHash=job.SourcePayloadHash
              JOIN dbo.AccountingEntryLines journal ON journal.EntryId=entry.EntryId
                AND journal.AccountId=tender.AccountId AND journal.Debit=0 AND journal.Credit>0
              WHERE tenderLine.TargetId IS NULL AND tenderLine.GroupId IS NOT NULL;
              SELECT a.AccountId,CASE WHEN controls.KindCount>1 OR controls.KindCount=1 AND
                  a.FinancialFunction IS NOT NULL AND a.FinancialFunction<>controls.Kind THEN N'Ambiguous'
                ELSE COALESCE(controls.Kind,a.FinancialFunction,
                  CASE WHEN EXISTS(SELECT 1 FROM @LegacyCashReturns legacy WHERE legacy.AccountId=a.AccountId)
                    AND NOT EXISTS(SELECT 1 FROM @RequestedLines requested WHERE requested.AccountId=a.AccountId
                      AND NOT EXISTS(SELECT 1 FROM @LegacyCashReturns legacy
                        WHERE legacy.AccountId=requested.AccountId AND legacy.GroupId=requested.GroupId))
                    THEN N'Cash' END,N'Unclassified') END,method.Code,a.BankAccountId,
                CASE WHEN a.PaymentMethodOptionId IS NOT NULL AND method.OptionId IS NULL THEN 1 ELSE 0 END,a.AccountType,a.Code
              FROM (SELECT DISTINCT AccountId FROM @RequestedLines) input
              JOIN dbo.AccountingAccounts a ON a.AccountId=input.AccountId AND a.TenantId=@TenantId
              LEFT JOIN reference.Options method ON method.OptionId=a.PaymentMethodOptionId
                AND method.CatalogCode=N'payment-method' AND method.IsActive=1
              OUTER APPLY(SELECT COUNT(DISTINCT Kind) KindCount,MAX(Kind) Kind FROM @ControlAccounts WHERE AccountId=a.AccountId) controls;

              -- The capture has at most 500 lines. Resolve its selected keys
              -- before the immutable opening joins; a history scan evaluates
              -- unrelated obligations and needs a large workspace grant.
              SELECT N'Payable',p.PayableId,opening.AccountId,s.PartyId,p.SupplierId,p.PartySiteId,p.CurrencyCode,
                source.OccurredAt,opening.Issue,discount.EligibleAmount,discount.Anticipated,
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications application
                  WHERE application.PayableId=p.PayableId AND application.FinancialDiscountEarned IS NOT NULL) THEN 1 ELSE 0 END),
                CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.PurchaseReturns purchaseReturn
                  WHERE purchaseReturn.OriginalGoodsReceiptId=p.SourceDocumentId AND purchaseReturn.BusinessId=@BusinessId
                    AND purchaseReturn.Status=N'Accepted') THEN 1 ELSE 0 END)
              FROM (SELECT DISTINCT TargetId FROM @RequestedLines WHERE TargetKind=N'Payable') input
              JOIN dbo.Payables p ON p.PayableId=input.TargetId AND p.BusinessId=@BusinessId
              JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId AND s.TenantId=@TenantId
              JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=p.SourceDocumentId
                AND source.SourceDocumentType=p.SourceDocumentType AND source.BusinessId=@BusinessId AND source.TenantId=@TenantId
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'Payable',p.OpeningEntryId,p.OpeningEntryLineNumber,s.PartyId) opening
              OUTER APPLY (SELECT value.EligibleAmount,value.Anticipated
                FROM accounting.GoodsReceiptFinancialDiscount(@BusinessId,p.SourceDocumentId) value
                WHERE p.SourceDocumentType=N'GoodsReceipt') discount
              UNION ALL
              SELECT N'Receivable',p.ReceivableId,opening.AccountId,COALESCE(p.PartyId,c.PartyId),p.CustomerId,p.PartySiteId,p.CurrencyCode,
                source.OccurredAt,opening.Issue,NULL,NULL,CONVERT(bit,0),CONVERT(bit,0)
              FROM (SELECT DISTINCT TargetId FROM @RequestedLines WHERE TargetKind=N'Receivable') input
              JOIN dbo.Receivables p ON p.ReceivableId=input.TargetId AND p.BusinessId=@BusinessId
              LEFT JOIN dbo.Customers c ON c.CustomerId=p.CustomerId AND c.TenantId=@TenantId
              JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=p.SourceDocumentId
                AND source.SourceDocumentType=p.SourceDocumentType AND source.BusinessId=@BusinessId AND source.TenantId=@TenantId
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'Receivable',p.OpeningEntryId,p.OpeningEntryLineNumber,COALESCE(p.PartyId,c.PartyId)) opening
              UNION ALL
              SELECT N'SupplierCredit',credit.SupplierCreditId,opening.AccountId,supplier.PartyId,credit.SupplierId,
                credit.PartySiteId,credit.CurrencyCode,COALESCE(history.OccurredAt,opening.OpeningOccurredAt,credit.CreatedAt),
                CASE WHEN credit.CreditNature IS NULL OR credit.CurrencyCode IS NULL
                  THEN N'El saldo a favor requiere conciliar su naturaleza y moneda antes de cruzarlo.' ELSE opening.Issue END,
                NULL,NULL,CONVERT(bit,0),CONVERT(bit,0)
              FROM (SELECT DISTINCT TargetId FROM @RequestedLines WHERE TargetKind=N'SupplierCredit') input
              JOIN dbo.SupplierCredits credit ON credit.SupplierCreditId=input.TargetId AND credit.BusinessId=@BusinessId
              JOIN dbo.Suppliers supplier ON supplier.SupplierId=credit.SupplierId AND supplier.TenantId=@TenantId
              LEFT JOIN dbo.SupplierCreditTransactions history ON history.SupplierCreditId=credit.SupplierCreditId AND history.TransactionType=N'Opening'
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'SupplierCredit',credit.OpeningEntryId,credit.OpeningEntryLineNumber,supplier.PartyId) opening
              UNION ALL
              SELECT N'CustomerCredit',credit.CustomerCreditId,opening.AccountId,party.PartyId,credit.CustomerId,
                credit.PartySiteId,credit.CurrencyCode,COALESCE(history.OccurredAt,opening.OpeningOccurredAt,credit.CreatedAt),
                CASE WHEN credit.CreditNature IS NULL OR credit.CurrencyCode IS NULL
                  THEN N'El saldo a favor requiere conciliar su naturaleza y moneda antes de cruzarlo.' ELSE opening.Issue END,
                NULL,NULL,CONVERT(bit,0),CONVERT(bit,0)
              FROM (SELECT DISTINCT TargetId FROM @RequestedLines WHERE TargetKind=N'CustomerCredit') input
              JOIN dbo.CustomerCredits credit ON credit.CustomerCreditId=input.TargetId AND credit.BusinessId=@BusinessId
              LEFT JOIN dbo.Customers customer ON customer.CustomerId=credit.CustomerId AND customer.TenantId=@TenantId
              JOIN dbo.Parties party ON party.PartyId=COALESCE(credit.PartyId,customer.PartyId) AND party.TenantId=@TenantId
              LEFT JOIN dbo.CustomerCreditTransactions history ON history.CustomerCreditId=credit.CustomerCreditId AND history.TransactionType=N'Opening'
              CROSS APPLY accounting.ObligationOpeningAccount(@TenantId,@BusinessId,N'CustomerCredit',credit.OpeningEntryId,credit.OpeningEntryLineNumber,party.PartyId) opening
              OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);

              -- Supplier/customer roles are unique by tenant/party in the
              -- schema. Resolve only parties in this capture.
              SELECT party.PartyId,supplier.SupplierId,site.PartySiteId,customer.CustomerId,customer.IsActive
              FROM (SELECT DISTINCT PartyId FROM @RequestedLines WHERE PartyId IS NOT NULL) input
              JOIN dbo.Parties party ON party.PartyId=input.PartyId AND party.TenantId=@TenantId AND party.IsActive=1
              LEFT JOIN dbo.Suppliers supplier ON supplier.PartyId=party.PartyId AND supplier.TenantId=@TenantId AND supplier.IsActive=1
              LEFT JOIN dbo.Customers customer ON customer.PartyId=party.PartyId AND customer.TenantId=@TenantId
              OUTER APPLY(SELECT TOP(1) chosen.PartySiteId FROM dbo.PartySites chosen
                WHERE chosen.PartyId=party.PartyId AND chosen.IsActive=1
                ORDER BY chosen.IsPrimary DESC,chosen.PartySiteId) site;
              SELECT category.Code,account.AccountId
              FROM (VALUES(N'SupplierFinancialDiscountEarned'),(N'SupplierFinancialDiscountLost')) category(Code)
              OUTER APPLY(SELECT TOP(1) selected.AccountId FROM dbo.AccountingAccountMappings selected
                WHERE selected.TenantId=@TenantId AND selected.Category=category.Code
                  AND (selected.BusinessId=@BusinessId OR selected.BusinessId IS NULL)
                  AND selected.EffectiveFrom<=CONVERT(date,@Date)
                  AND (selected.EffectiveTo IS NULL OR selected.EffectiveTo>=CONVERT(date,@Date))
                ORDER BY CASE WHEN selected.BusinessId=@BusinessId THEN 0 ELSE 1 END,
                  selected.EffectiveFrom DESC) mapping
              OUTER APPLY accounting.PostingAccountReadiness(@TenantId,@BusinessId,category.Code,mapping.AccountId) ready
              LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=ready.AccountId
                AND account.TenantId=@TenantId AND account.IsActive=1 AND account.AllowsPosting=1
                AND account.FinancialFunction=N'General'
                AND (account.BusinessId IS NULL OR account.BusinessId=@BusinessId);
            END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@MainPartyId", (object?)mainPartyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Date", occurredAt);
        command.Parameters.AddWithValue("@Concept", conceptCode);
        command.Parameters.AddWithValue("@Final", final);
        command.Parameters.AddWithValue("@Strict", validateDraftReadiness);
        command.Parameters.AddWithValue("@FinancialCapture", financialCapture);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = (object?)workSessionId ?? DBNull.Value;
        command.Parameters.Add("@VoucherType", SqlDbType.NVarChar, 64).Value = (object?)voucherTypeCode ?? DBNull.Value;
        command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new DBConcurrencyException("Voucher validation returned no result.");
        if (!reader.IsDBNull(0)) throw new AccountingValidationException(reader.GetString(0));
        if (!financialCapture) return null;
        var accounts = new List<VoucherFinancialAccount>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetInt32(4) != 0 || reader.GetString(1) == "Ambiguous")
                throw new AccountingValidationException("El auxiliar requiere revisar su función o medio de pago configurado.");
            accounts.Add(new(reader.GetGuid(0), reader.GetString(1), NullableText(reader, 2), NullableId(reader, 3),
                reader.GetString(5), reader.GetString(6)));
        }
        var positions = new List<VoucherFinancialPosition>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
            positions.Add(new(reader.GetString(0), reader.GetGuid(1), NullableId(reader, 2), reader.GetGuid(3),
                NullableId(reader, 4), NullableId(reader, 5), NullableText(reader, 6), reader.GetDateTimeOffset(7), NullableText(reader, 8),
                reader.IsDBNull(9)?null:reader.GetDecimal(9),reader.IsDBNull(10)?null:reader.GetBoolean(10),
                reader.GetBoolean(11),reader.GetBoolean(12)));
        var parties = new List<VoucherFinancialParty>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
            parties.Add(new(reader.GetGuid(0), NullableId(reader, 1), NullableId(reader, 2), NullableId(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetBoolean(4)));
        Guid? earnedAccountId = null;
        Guid? lostAccountId = null;
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetString(0) == AccountingCategories.SupplierFinancialDiscountEarned)
                earnedAccountId = NullableId(reader, 1);
            else if (reader.GetString(0) == AccountingCategories.SupplierFinancialDiscountLost)
                lostAccountId = NullableId(reader, 1);
        }
        return new(accounts, positions, parties, earnedAccountId, lostAccountId);
    }
}
