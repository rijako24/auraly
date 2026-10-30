using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore(
    AccountingSqlConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    SqlAccountingPostingProcessor postingProcessor,
    IManualVoucherFiscalParticipant fiscalParticipant) : IAccountingStore
{
    public Task<AccountingManualDocumentAcceptance> ConfirmAccountAdjustmentAsync(
        AccountingUserIdentity user, ConfirmAccountAdjustmentRequest request,
        CancellationToken cancellationToken, string? draftVersion = null) => AcceptManualDocumentAsync(
            user, request.AdjustmentId, AccountingManualDocumentTypes.AccountAdjustment,
            request.OccurredAt, request, async (connection, transaction, accepted, token) =>
            {
                var receivable = request.SubledgerKind == AccountingSubledgerKinds.Receivable;
                var table = receivable ? "Receivables" : "Payables";
                var idColumn = receivable ? "ReceivableId" : "PayableId";
                var partyTable = receivable ? "Customers" : "Suppliers";
                var partyColumn = receivable ? "CustomerId" : "SupplierId";
                var positionParty = receivable ? "COALESCE(balance.PartyId,party.PartyId)" : "party.PartyId";
                await using var target = new SqlCommand($"""
                    SELECT pending.AvailableAmount,
                      balance.Status,{positionParty},source.OccurredAt,balance.CurrencyCode,settings.FunctionalCurrencyCode,
                      pending.PendingCancellationCount,pending.SourceCancelled
                    FROM dbo.[{table}] balance WITH(UPDLOCK,HOLDLOCK)
                    JOIN dbo.AccountingTenantSettings settings ON settings.TenantId=@TenantId
                    LEFT JOIN dbo.[{partyTable}] party ON party.[{partyColumn}]=balance.[{partyColumn}] AND party.TenantId=@TenantId
                    JOIN dbo.Parties identityParty ON identityParty.PartyId={positionParty} AND identityParty.TenantId=@TenantId
                    JOIN dbo.AccountingPostingJobs source ON source.SourceDocumentId=balance.SourceDocumentId
                      AND source.SourceDocumentType=balance.SourceDocumentType AND source.BusinessId=balance.BusinessId
                      AND source.TenantId=@TenantId
                    CROSS APPLY accounting.ObligationAvailability(@BusinessId,@Kind,@Id,balance.OutstandingAmount,@AdjustmentId) pending
                    WHERE balance.[{idColumn}]=@Id AND balance.BusinessId=@BusinessId;
                    """, connection, transaction);
                target.Parameters.AddWithValue("@Id", request.SubledgerId);
                target.Parameters.AddWithValue("@AdjustmentId", request.AdjustmentId);
                target.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                target.Parameters.AddWithValue("@Kind", request.SubledgerKind);
                target.Parameters.AddWithValue("@TenantId", user.TenantId);
                await using var reader = await target.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token))
                    throw new AccountingValidationException("La obligación no existe en esta sede.");
                var available = reader.GetDecimal(0);
                var status = reader.GetString(1);
                var partyId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
                if (reader.GetBoolean(7))
                    throw new AccountingConflictException("No se registró el ajuste: el documento de origen fue anulado.");
                if (reader.GetInt32(6) > 0)
                    throw new AccountingConflictException("La obligación tiene una anulación aceptada pendiente. No se registró el ajuste.");
                if (status == "Cancelled" || request.Direction == AccountingAdjustmentDirections.Decrease && request.Amount > available)
                    throw new AccountingConflictException("El ajuste supera el saldo disponible, incluidos los movimientos pendientes.");
                if (draftVersion is not null && request.OccurredAt.Date < reader.GetDateTimeOffset(3).Date)
                    throw new AccountingValidationException("La fecha del ajuste no puede anteceder la obligación.");
                if (reader.GetString(4) != reader.GetString(5))
                    throw new AccountingValidationException("La moneda de la obligación debe coincidir con la moneda funcional.");
                await reader.DisposeAsync();
                await ValidateVoucherDimensionsAsync(connection, transaction, user,
                    [new(request.CounterpartAccountId, partyId, request.CostCenterId, request.Description, request.Amount, 0)],
                    request.OccurredAt, request.ConceptCode, final: true, token, validateDraftReadiness: draftVersion is not null);
            }, cancellationToken, draftVersion);

    public async Task<AccountingManualDocumentAcceptance> ConfirmManualVoucherAsync(
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        CancellationToken cancellationToken, string? draftVersion = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            PreparedManualVoucherFiscal? preparedFiscal = null;
            try
            {
                return await AcceptManualDocumentAsync(
            user, request.VoucherId, AccountingManualDocumentTypes.ManualVoucher,
            request.OccurredAt, request, async (connection, transaction, accepted, token) =>
            {
                if (accepted.PayloadVersion == 4)
                {
                    await AcceptVoucherPaymentGroupsAsync(connection, transaction, user, accepted, cancellationToken);
                    if (preparedFiscal is not null)
                        await fiscalParticipant.PersistAsync(connection, transaction, user, accepted, preparedFiscal, token);
                    return;
                }
                if (request.ReversesDocumentId is not null)
                    await ValidateJournalCancellationAsync(connection, transaction, user, request, token);
                await ValidateVoucherDimensionsAsync(connection, transaction, user,
                    request.Lines.Select(line => new VoucherDraftLine(line.AccountId, line.PartyId,
                        line.CostCenterId, line.Description, line.Debit, line.Credit)).ToArray(),
                    request.OccurredAt, request.ConceptCode, final: true, token, validateDraftReadiness: draftVersion is not null,
                    mainPartyId: request.MainPartyId);
            }, cancellationToken, draftVersion, request.PayloadVersion == 4
                ? async (connection, transaction, raw, token) =>
                {
                    if (raw.MainPartyId is null)
                        throw new AccountingValidationException("Selecciona el tercero principal antes de contabilizar el comprobante.");
                    var hasFiscalEffect = raw.VoucherTypeCode is "SupportDocument" or "SupportDocumentCancellation";
                    var fiscalCalculation = hasFiscalEffect
                        ? await fiscalParticipant.ReviewAsync(connection, transaction, user, raw, token) : null;
                    var context = await ValidateVoucherDimensionsAsync(connection, transaction, user,
                        raw.Lines.Select(line => new VoucherDraftLine(line.AccountId, line.PartyId, line.CostCenterId,
                            line.Description, line.Debit, line.Credit, line.Reference, line.LineId, line.BaseAmount,
                            line.ExistingPosition, line.FinancialGroupId, line.DueDate, line.PartySiteId, line.PaymentEvidence,
                            line.FinancialDiscountEarned)).ToArray(),
                        raw.OccurredAt, raw.ConceptCode, final: true, token, mainPartyId: raw.MainPartyId, financialCapture: true,
                        voucherTypeCode: raw.VoucherTypeCode, workSessionId: raw.WorkSessionId);
                    VoucherFiscalPreview? fiscalProof = null;
                    if (fiscalCalculation is not null)
                    {
                        fiscalProof = AccountingVoucherFinancialValidation.ValidateFiscal(raw, fiscalCalculation, context!);
                        preparedFiscal = await fiscalParticipant.PrepareAsync(connection, transaction, user, raw,
                            fiscalCalculation, fiscalProof, token);
                        raw = raw with { Reference = raw.Reference ?? preparedFiscal.FiscalNumber };
                    }
                    return raw with { FinancialEffects = AccountingVoucherFinancialValidation.Normalize(user, raw, context!, ids, fiscalProof) };
                } : null);
            }
            catch (SqlException error) when (error.Number == 1205 && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
            catch (SqlException error) when (request.ReversesDocumentId is not null && (error.Number is 2601 or 2627) &&
                error.Message.Contains("UX_AccountingSourceDocuments_ManualReversal", StringComparison.Ordinal))
            {
                throw new AccountingConflictException("Este comprobante ya tiene una anulación aceptada. Consulta su asiento inverso.");
            }
        }
    }

    private async Task<AccountingManualDocumentAcceptance> AcceptManualDocumentAsync<T>(
        AccountingUserIdentity user, Guid documentId, string documentType,
        DateTimeOffset occurredAt, T payload,
        Func<SqlConnection, SqlTransaction, T, CancellationToken, Task> validate,
        CancellationToken cancellationToken, string? draftVersion = null,
        Func<SqlConnection, SqlTransaction, T, CancellationToken, Task<T>>? prepare = null)
    {
        var json = JsonSerializer.Serialize(payload);
        var now = timeProvider.GetUtcNow();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var draftWasSent = false;
            var draftExists = false;
            string? frozenPayload = null;
            string? capturedReference = null;
            // Lock the position before publishing its reservation key, in the same
            // order as native payments. Otherwise concurrent adjustments can hold
            // each other's uncommitted source while waiting for the position.
            // Lock the editable aggregate before accepting its immutable source.
            // Legacy direct acceptance cannot bypass a saved draft's version or permission.
            await using (var draft = new SqlCommand("""
                DECLARE @LockedPosition uniqueidentifier;
                IF @SubledgerKind=N'Payable'
                    SELECT @LockedPosition=PayableId FROM dbo.Payables WITH(UPDLOCK,HOLDLOCK)
                    WHERE PayableId=@SubledgerId AND BusinessId=@BusinessId;
                IF @SubledgerKind=N'Receivable'
                    SELECT @LockedPosition=ReceivableId FROM dbo.Receivables WITH(UPDLOCK,HOLDLOCK)
                    WHERE ReceivableId=@SubledgerId AND BusinessId=@BusinessId;
                DECLARE @Positions TABLE(Kind nvarchar(16),PositionId uniqueidentifier,PRIMARY KEY(Kind,PositionId));
                INSERT @Positions SELECT DISTINCT Kind,PositionId FROM OPENJSON(@FinancialLines)
                  WITH(Kind nvarchar(16) '$.ExistingPosition.SubledgerKind',PositionId uniqueidentifier '$.ExistingPosition.SubledgerId')
                  WHERE PositionId IS NOT NULL AND Kind IN(N'Payable',N'Receivable',N'SupplierCredit',N'CustomerCredit');
                SELECT @LockedPosition=p.PayableId FROM @Positions input JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK)
                  ON input.Kind=N'Payable' AND p.PayableId=input.PositionId AND p.BusinessId=@BusinessId
                  ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
                SELECT @LockedPosition=p.ReceivableId FROM @Positions input JOIN dbo.Receivables p WITH(UPDLOCK,HOLDLOCK)
                  ON input.Kind=N'Receivable' AND p.ReceivableId=input.PositionId AND p.BusinessId=@BusinessId
                  ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
                SELECT @LockedPosition=p.SupplierCreditId FROM @Positions input JOIN dbo.SupplierCredits p WITH(UPDLOCK,HOLDLOCK)
                  ON input.Kind=N'SupplierCredit' AND p.SupplierCreditId=input.PositionId AND p.BusinessId=@BusinessId
                  ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
                SELECT @LockedPosition=p.CustomerCreditId FROM @Positions input JOIN dbo.CustomerCredits p WITH(UPDLOCK,HOLDLOCK)
                  ON input.Kind=N'CustomerCredit' AND p.CustomerCreditId=input.PositionId AND p.BusinessId=@BusinessId
                  ORDER BY input.PositionId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
                DECLARE @Draft TABLE(TenantId uniqueidentifier,BusinessId uniqueidentifier,DocumentType nvarchar(64),
                  [RowVersion] binary(8),SentAt datetimeoffset(7),FrozenPayload nvarchar(max),CapturedReference nvarchar(100));
                INSERT @Draft(TenantId,BusinessId,DocumentType,[RowVersion],SentAt,CapturedReference)
                SELECT d.TenantId,d.BusinessId,d.DocumentType,d.RowVersion,d.SentAt,d.Reference
                FROM accounting.VoucherDrafts d WITH(UPDLOCK,HOLDLOCK) WHERE d.DocumentId=@Id;
                -- An unsent draft has no immutable payload to read. A conditional
                -- LEFT JOIN can still probe its missing source range under
                -- SERIALIZABLE and block a concurrent fiscal acceptance's insert.
                IF EXISTS(SELECT 1 FROM @Draft WHERE SentAt IS NOT NULL)
                  UPDATE captured SET FrozenPayload=source.PayloadJson FROM @Draft captured
                  JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=@Id
                    AND source.SourceDocumentType=captured.DocumentType AND source.TenantId=captured.TenantId
                    AND source.BusinessId=captured.BusinessId;
                SELECT TenantId,BusinessId,DocumentType,[RowVersion],SentAt,FrozenPayload,CapturedReference FROM @Draft;
                """, connection, transaction))
            {
                draft.Parameters.AddWithValue("@Id", documentId);
                draft.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                var adjustment = payload as ConfirmAccountAdjustmentRequest;
                draft.Parameters.AddWithValue("@SubledgerKind", (object?)adjustment?.SubledgerKind ?? DBNull.Value);
                draft.Parameters.AddWithValue("@SubledgerId", (object?)adjustment?.SubledgerId ?? DBNull.Value);
                draft.Parameters.Add("@FinancialLines", SqlDbType.NVarChar, -1).Value =
                    payload is ConfirmManualAccountingVoucherRequest { PayloadVersion: 4 } financial ? JsonSerializer.Serialize(financial.Lines) : "[]";
                await using var reader = await draft.ExecuteReaderAsync(cancellationToken);
                draftExists = await reader.ReadAsync(cancellationToken);
                draftWasSent = draftExists && !reader.IsDBNull(4);
                if (draftWasSent && !reader.IsDBNull(5)) frozenPayload = reader.GetString(5);
                if (draftExists && !reader.IsDBNull(6)) capturedReference = reader.GetString(6);
                if (draftExists && (draftVersion is null || reader.GetGuid(0) != user.TenantId ||
                    reader.GetGuid(1) != user.BusinessId || reader.GetString(2) != documentType ||
                    (reader.IsDBNull(4) && Convert.ToBase64String((byte[])reader[3]) != draftVersion)))
                    throw new AccountingConflictException("El comprobante cambió o debe contabilizarse desde su captura guardada.");
                if (!draftExists && draftVersion is not null)
                    throw new AccountingConflictException("El comprobante no existe en esta sede.");
            }

            if (prepare is not null)
            {
                if (!draftExists || draftVersion is null)
                    throw new AccountingConflictException("La versión financiera requiere su captura guardada.");
                if (draftWasSent && frozenPayload is not null)
                {
                    var accepted = JsonSerializer.Deserialize<T>(frozenPayload)
                        ?? throw new AccountingConflictException("La fuente financiera aceptada requiere revisión.");
                    if (accepted is not ConfirmManualAccountingVoucherRequest stored || stored.FinancialEffects is null)
                        throw new AccountingConflictException("La captura no coincide con su fuente financiera aceptada.");
                    var comparable = stored with { FinancialEffects = null };
                    // A concurrent send may have read the unsent capture before
                    // the first send assigned its fiscal number. Only that
                    // server-owned default differs; captured references and all
                    // remaining input must still match the immutable source.
                    if (capturedReference is null && payload is ConfirmManualAccountingVoucherRequest { Reference: null } &&
                        stored.VoucherTypeCode is "SupportDocument" or "SupportDocumentCancellation")
                        comparable = comparable with { Reference = null };
                    if (JsonSerializer.Serialize(comparable) != json)
                        throw new AccountingConflictException("La captura no coincide con su fuente financiera aceptada.");
                    payload = accepted;
                    json = frozenPayload;
                }
                else if (!draftWasSent)
                {
                    payload = await prepare(connection, transaction, payload, cancellationToken);
                    json = JsonSerializer.Serialize(payload);
                }
            }

            var acceptance = await SqlAccountingPostingJobWriter.AcceptSourcesAsync(connection, transaction,
                [new(user.TenantId, user.BusinessId, documentId, documentType, json, occurredAt, ids.NewId())],
                now, AccountingJobRequirement.ManualBookEntry, cancellationToken);
            if (acceptance.QueuedCount == 0)
                throw new AccountingConflictException("Accounting must be active for the manual document date.");
            if (draftWasSent && acceptance.NewSourceCount != 0)
                throw new AccountingConflictException("El comprobante enviado perdió su fuente aceptada y requiere revisión; no se vuelve a aceptar.");
            if (draftExists && !draftWasSent && acceptance.NewSourceCount == 0)
                throw new AccountingConflictException("La fuente ya fue aceptada, pero su captura no está congelada. Revisa la integridad del comprobante.");
            var acceptedStatus = acceptance.SingleSourceStatus
                ?? throw new DBConcurrencyException("The manual accounting acceptance has no authoritative status.");
            if (acceptance.NewSourceCount == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(documentId, documentType, acceptedStatus, true);
            }

            await validate(connection, transaction, payload, cancellationToken);
            await using var insert = new SqlCommand("""
                UPDATE accounting.VoucherDrafts SET SentAt=@Now,SentBy=@UserId,UpdatedAt=@Now,UpdatedBy=@UserId
                WHERE DocumentId=@DocumentId AND TenantId=@TenantId AND BusinessId=@BusinessId AND SentAt IS NULL;
                """, connection, transaction);
            insert.Parameters.AddWithValue("@UserId", user.UserId);
            insert.Parameters.AddWithValue("@TenantId", user.TenantId);
            insert.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            insert.Parameters.AddWithValue("@DocumentId", documentId);
            insert.Parameters.AddWithValue("@Now", now);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != (draftVersion is null ? 0 : 1))
                throw new DBConcurrencyException(
                    "The manual accounting document was not accepted atomically.");
            await transaction.CommitAsync(cancellationToken);
            return new(documentId, documentType, acceptedStatus, false);
        }
        catch (SqlException error) when (error.Number == 51731)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingValidationException("El comprobante contiene datos que no pertenecen a esta empresa o no son válidos para su aceptación.");
        }
        catch (SqlException error) when (error.Number == 51848)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Message);
        }
        catch (SqlException error) when (error.Number is 51849 or 51851)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingValidationException(error.Number == 51851 ? error.Message
                : "No se aceptó el cruce. Revisa la sede, moneda y conciliación de las posiciones seleccionadas.");
        }
        catch (SqlException error) when (error.Number is 51732 or 51733)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Number == 51732
                ? "El identificador del comprobante ya fue aceptado con otros datos."
                : "El comprobante ya fue aceptado y requiere revisar su trabajo contable.");
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task AcceptVoucherPaymentGroupsAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken token)
    {
        var effects = request.FinancialEffects ?? throw new InvalidOperationException("The financial source has no normalized effects.");
        var sourceHash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        await AcceptDirectionAsync(AccountingSubledgerKinds.Payable);
        await AcceptDirectionAsync(AccountingSubledgerKinds.Receivable);

        async Task AcceptDirectionAsync(string kind)
        {
            var groups = effects.Payments.Where(group => group.SubledgerKind == kind).ToArray();
            if (groups.Length == 0) return;
            var payable = kind == AccountingSubledgerKinds.Payable;
            var payload = JsonSerializer.Serialize(new
            {
                groups = groups.Select(group => new
                {
                    tenantId = user.TenantId, businessId = user.BusinessId, paymentId = group.PaymentId,
                    supplierId = payable ? group.CounterpartyRoleId : null, customerId = payable ? null : group.CounterpartyRoleId,
                    partyId = group.PartyId, partySiteId = group.PartySiteId,
                    confirmedByUserId = effects.AcceptedByUserId, workSessionId = request.WorkSessionId,
                    paidAt = request.OccurredAt, currencyCode = request.CurrencyCode, notes = request.Description,
                    totalAmount = group.Allocations.Sum(item => item.Amount) + group.AdvanceAmount,
                    advanceAmount = group.AdvanceAmount, advancePositionId = group.AdvancePositionId,
                    parentSourceDocumentId = request.VoucherId,
                    parentSourceDocumentType = AccountingManualDocumentTypes.ManualVoucher, sourceGroupId = group.GroupId,
                    allocations = group.Allocations.Select((item, index) => new
                    {
                        lineNumber = index + 1, payableId = payable ? (Guid?)item.SubledgerId : null,
                        receivableId = payable ? null : (Guid?)item.SubledgerId, amount = item.Amount, sourceLineId = item.SourceLineId,
                        financialDiscountEarned = item.FinancialDiscountEarned,
                        financialDiscountAdjustment = item.FinancialDiscountAdjustment
                    }),
                    payments = group.Tenders.Select((item, index) => new
                    {
                        lineNumber = index + 1, methodCode = item.MethodCode, amount = item.Amount, tenderedAmount = item.TenderedAmount,
                        bankAccountId = item.BankAccountId, reference = item.Reference, notes = item.Notes,
                        cardFranchiseCode = item.CardFranchiseCode, approvalNumber = item.ApprovalNumber,
                        accountId = item.AccountId, sourceLineId = item.SourceLineId
                    })
                })
            });
            await using var command = new SqlCommand("accounting.PortfolioPaymentAccept", connection, transaction)
                { CommandType = CommandType.StoredProcedure };
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@SubledgerKind", kind);
            command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = payload;
            command.Parameters.AddWithValue("@IdempotencyKey", request.VoucherId.ToString("N"));
            command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = sourceHash;
            command.Parameters.AddWithValue("@AcceptedAt", timeProvider.GetUtcNow());
            try { await command.ExecuteNonQueryAsync(token); }
            catch (SqlException error) when (error.Number == 51402)
            { throw new AccountingConflictException(error.Message); }
            catch (SqlException error) when (error.Number is 51400 or 51401)
            { throw new AccountingValidationException($"El pago no se realizó. {error.Message}", error); }
        }
    }

    public async Task<AccountingAccountOptionPage> AccountOptionsAsync(
        AccountingUserIdentity user, AccountingAccountOptionQuery query, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string filters = """
            WHERE TenantId=@TenantId AND (@IncludeInactive=1 OR IsActive=1)
              AND (@IncludeStructural=1 OR AllowsPosting=1)
              AND (@GroupingOnly=0 OR AllowsPosting=0)
              AND (@IncludeStructural=1 OR BusinessId IS NULL OR BusinessId=@BusinessId)
              AND (@ExpenseOnly=0 OR AccountType=N'Expense')
              AND (@LiabilityOnly=0 OR AccountType=N'Liability')
              AND (@AccountId IS NULL OR AccountId=@AccountId)
              AND (@Search IS NULL OR Code LIKE N'%'+@Search+N'%' OR Name LIKE N'%'+@Search+N'%')
            """;
        await using var command = new SqlCommand($"""
            SELECT AccountId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,RowVersion,
              ParentAccountId,FinancialFunction,BalanceSide,BusinessId,BankAccountId,PaymentMethodOptionId,ConfigurationLockedAt
            FROM dbo.AccountingAccounts {filters} ORDER BY Code,AccountId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT COUNT(*) FROM dbo.AccountingAccounts {filters};
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@ExpenseOnly", query.ExpenseOnly);
        command.Parameters.AddWithValue("@LiabilityOnly", query.LiabilityOnly);
        command.Parameters.AddWithValue("@IncludeInactive", query.IncludeInactive);
        command.Parameters.AddWithValue("@IncludeStructural", query.IncludeStructural);
        command.Parameters.AddWithValue("@GroupingOnly", query.GroupingOnly);
        command.Parameters.AddWithValue("@AccountId", (object?)query.AccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        var items = new List<AccountingAccountView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(ReadAccount(reader));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(items, query.Page, query.PageSize, reader.GetInt32(0));
    }

    public async Task<IReadOnlyList<AccountingAccountView>> ListAccountsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT AccountId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,RowVersion,
              ParentAccountId,FinancialFunction,BalanceSide,BusinessId,BankAccountId,PaymentMethodOptionId,ConfigurationLockedAt
            FROM dbo.AccountingAccounts WHERE TenantId=@TenantId ORDER BY Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingAccountView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(ReadAccount(reader));
        return values;
    }

    public async Task<IReadOnlyList<AccountingCostCenterView>> ListCostCentersAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT c.CostCenterId,c.BusinessId,c.Code,c.Name,c.ParentCostCenterId,c.IsDefault,c.IsActive,c.RowVersion
            FROM dbo.AccountingCostCenters c
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            WHERE c.BusinessId=@BusinessId AND b.TenantId=@TenantId ORDER BY c.Code;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingCostCenterView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetBoolean(5), reader.GetBoolean(6),
                Convert.ToBase64String((byte[])reader[7])));
        return values;
    }

    public async Task<IReadOnlyList<AccountingPeriodView>> ListPeriodsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT PeriodId,StartsOn,EndsOn,Name,Status FROM dbo.AccountingPeriods
            WHERE TenantId=@TenantId ORDER BY StartsOn DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingPeriodView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), DateOnly.FromDateTime(reader.GetDateTime(1)),
                DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3), reader.GetString(4)));
        return values;
    }

    public async Task<IReadOnlyList<AccountingMappingView>> ListMappingsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo
            FROM dbo.AccountingAccountMappings
            WHERE TenantId=@TenantId AND (BusinessId IS NULL OR BusinessId=@BusinessId)
            ORDER BY Category,EffectiveFrom DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        var values = new List<AccountingMappingView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetGuid(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6))));
        return values;
    }

    public async Task<IReadOnlyList<AccountingCategoryDefinition>> ListCategoryDefinitionsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT d.Category,d.DisplayName,d.AccountType,d.IsRequired,d.DisplayOrder
            FROM dbo.AccountingConfigurationProfiles p
            INNER JOIN dbo.AccountingConfigurationProfileAccounts d ON d.ProfileCode=p.ProfileCode
            WHERE p.IsDefault=1 AND p.IsActive=1
            ORDER BY d.DisplayOrder;
            """, connection);
        var values = new List<AccountingCategoryDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetBoolean(3), reader.GetInt32(4)));
        return values;
    }

    public async Task<AccountingDefaultsResult> EnsureDefaultsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
                  THROW 51403,'The accounting business is outside the legal entity.',1;

                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId)
                  INSERT dbo.AccountingTenantSettings
                    (TenantId,Status,FunctionalCurrencyCode,UpdatedAt)
                  VALUES(@TenantId,N'Configuring',N'COP',@Now);

                DECLARE @ProfileCode nvarchar(32)=(
                  SELECT ProfileCode FROM dbo.AccountingConfigurationProfiles WITH(UPDLOCK,HOLDLOCK)
                  WHERE IsDefault=1 AND IsActive=1);
                IF @ProfileCode IS NULL
                  THROW 51403,'No active default accounting profile is configured.',1;

                ;WITH DefaultAccounts AS(
                  SELECT d.AccountCode,d.AccountName,d.AccountType,d.AllowsPosting,d.RequiresParty,
                    ROW_NUMBER() OVER(PARTITION BY d.AccountCode
                      ORDER BY CASE WHEN d.Category=N'OtherExpense' THEN 0 ELSE 1 END,d.Category) AccountRank
                  FROM accounting.DefaultAccountDefinitions(@ProfileCode) d
                )
                INSERT dbo.AccountingAccounts(
                  AccountId,TenantId,Code,Name,AccountType,AllowsPosting,
                  RequiresParty,IsActive,CreatedAt)
                SELECT NEWID(),@TenantId,d.AccountCode,d.AccountName,d.AccountType,d.AllowsPosting,
                       d.RequiresParty,1,@Now
                FROM DefaultAccounts d
                WHERE d.AccountRank=1 AND
                  NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts a WITH(UPDLOCK,HOLDLOCK)
                  WHERE a.TenantId=@TenantId AND a.Code=d.AccountCode);

                INSERT dbo.AccountingAccountMappings(
                  MappingId,TenantId,BusinessId,Category,AccountId,
                  EffectiveFrom,EffectiveTo,CreatedAt)
                SELECT NEWID(),@TenantId,NULL,d.Category,a.AccountId,
                       CONVERT(date,'20000101'),NULL,@Now
                FROM accounting.DefaultAccountDefinitions(@ProfileCode) d
                INNER JOIN dbo.AccountingAccounts a
                  ON a.TenantId=@TenantId AND a.Code=d.AccountCode
                    AND (a.Name=d.AccountName OR d.Category=N'SupplierFinancialDiscountLost')
                    AND a.AccountType=d.AccountType AND a.IsActive=1 AND a.AllowsPosting=1
                WHERE d.ProfileCode=@ProfileCode AND
                  NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings m WITH(UPDLOCK,HOLDLOCK)
                  WHERE m.TenantId=@TenantId AND m.BusinessId IS NULL AND m.Category=d.Category);

                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1)
                  INSERT dbo.AccountingCostCenters(
                    CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
                  VALUES(NEWID(),@BusinessId,N'GENERAL',N'Operación general',NULL,1,1,@Now);

                DECLARE @YearStart date=DATEFROMPARTS(YEAR(@Now),1,1);
                DECLARE @YearEnd date=DATEFROMPARTS(YEAR(@Now),12,31);
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND StartsOn<=@YearEnd AND EndsOn>=@YearStart)
                  INSERT dbo.AccountingPeriods(
                    PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                  VALUES(NEWID(),@TenantId,CONVERT(nvarchar(4),YEAR(@Now)),
                         @YearStart,@YearEnd,N'Open',@Now);

                DECLARE @RequiredMappingCount int=(SELECT COUNT(*)
                  FROM dbo.AccountingConfigurationProfileAccounts
                  WHERE ProfileCode=@ProfileCode AND IsRequired=1);
                SELECT
                  (SELECT COUNT(*) FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND IsActive=1),
                  (SELECT COUNT(DISTINCT Category) FROM dbo.AccountingAccountMappings
                    WHERE TenantId=@TenantId AND (BusinessId IS NULL OR BusinessId=@BusinessId)
                      AND EffectiveFrom<=CAST(@Now AS date)
                      AND (EffectiveTo IS NULL OR EffectiveTo>=CAST(@Now AS date))),
                  CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingCostCenters
                    WHERE BusinessId=@BusinessId AND IsDefault=1 AND IsActive=1) THEN 1 ELSE 0 END),
                  CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingPeriods
                    WHERE TenantId=@TenantId AND Status=N'Open'
                      AND CAST(@Now AS date) BETWEEN StartsOn AND EndsOn) THEN 1 ELSE 0 END),
                  @RequiredMappingCount;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Accounting defaults did not return their status.");
            var accountCount = reader.GetInt32(0);
            var mappingCount = reader.GetInt32(1);
            var hasCenter = reader.GetBoolean(2);
            var hasPeriod = reader.GetBoolean(3);
            var requiredMappingCount = reader.GetInt32(4);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return new(accountCount, mappingCount, hasCenter, hasPeriod,
                accountCount >= requiredMappingCount && mappingCount >= requiredMappingCount && hasCenter && hasPeriod);
        }
        catch (SqlException exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AccountingAccountView> CreateAccountAsync(
        AccountingUserIdentity user, CreateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var existing = await ReadAccountForEditAsync(connection, transaction, user.TenantId, request.AccountId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Code != request.Code || existing.Name != request.Name || existing.AccountType != request.AccountType ||
                    existing.AllowsPosting != request.AllowsPosting || existing.RequiresParty != request.RequiresParty || existing.Semantics != request.Semantics)
                    throw new AccountingConflictException("El identificador ya pertenece a otra configuración de cuenta.");
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
            await ValidateAccountConfigurationAsync(connection, transaction, user.TenantId, request.AccountId,
                request.Code, request.AccountType, request.AllowsPosting, request.RequiresParty, request.Semantics, cancellationToken);
            await using var command = new SqlCommand("""
                INSERT dbo.AccountingAccounts
                  (AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt,
                   ParentAccountId,FinancialFunction,BalanceSide,BusinessId,BankAccountId,PaymentMethodOptionId)
                OUTPUT inserted.AccountId,inserted.Code,inserted.Name,inserted.AccountType,
                  inserted.AllowsPosting,inserted.RequiresParty,inserted.IsActive,inserted.RowVersion,
                  inserted.ParentAccountId,inserted.FinancialFunction,inserted.BalanceSide,inserted.BusinessId,
                  inserted.BankAccountId,inserted.PaymentMethodOptionId,inserted.ConfigurationLockedAt
                VALUES(@AccountId,@TenantId,@Code,@Name,@AccountType,@AllowsPosting,@RequiresParty,1,@Now,
                  @Parent,@Function,@Balance,@BusinessId,@BankId,@PaymentId);
                """, connection, transaction);
            command.Parameters.AddWithValue("@AccountId", request.AccountId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@AccountType", request.AccountType);
            command.Parameters.AddWithValue("@AllowsPosting", request.AllowsPosting);
            command.Parameters.AddWithValue("@RequiresParty", request.RequiresParty);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            AddAccountConfigurationParameters(command, request.Semantics);
            AccountingAccountView result;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                result = ReadAccount(reader);
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException exception) when (IsConflict(exception))
        { throw new AccountingConflictException("Ya existe una cuenta con ese identificador o código."); }
        catch (SqlException exception) when (exception.Number == 51411)
        { throw new AccountingValidationException(exception.Message); }
    }

    public async Task<AccountingAccountView> UpdateAccountAsync(
        AccountingUserIdentity user, Guid accountId, UpdateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var result = await UpdateAccountInTransactionAsync(connection, transaction, user, accountId, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<AccountingAccountView> UpdateAccountInTransactionAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        Guid accountId, UpdateAccountingAccountRequest request, CancellationToken cancellationToken,
        bool preserveLegacyParent = false)
    {
        var version = RequiredRecordVersion(request.RowVersion);
        try
        {
            var configurationChanged = false;
            if (request.Semantics is not null)
            {
                var existing = await ReadAccountForEditAsync(connection, transaction, user.TenantId, accountId, cancellationToken)
                    ?? throw new AccountingConflictException("La cuenta no está disponible en esta empresa.");
                configurationChanged = existing.Semantics != request.Semantics;
                if (configurationChanged)
                {
                    if (existing.RowVersion != request.RowVersion || existing.ConfigurationLocked)
                        throw new AccountingConflictException("La cuenta cambió o ya fue utilizada. Su configuración financiera se conserva.");
                    await ValidateAccountConfigurationAsync(connection, transaction, user.TenantId, accountId,
                        existing.Code, existing.AccountType, existing.AllowsPosting, request.RequiresParty, request.Semantics, cancellationToken,
                        preserveLegacyParent && existing.Semantics is null);
                }
            }

            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK)
                  WHERE AccountId=@AccountId AND TenantId=@TenantId
                    AND (RowVersion=@Version OR (@Configure=0 AND Name COLLATE Latin1_General_100_BIN2=@Name AND RequiresParty=@RequiresParty)))
                  THROW 51415,N'La cuenta cambió o no está disponible. Actualiza la lista antes de guardar.',1;
                IF @RequiresParty=1 AND @Configure=0 AND EXISTS(SELECT 1 FROM accounting.BankAccounts bank
                  JOIN dbo.AccountingAccounts account ON account.BankAccountId=bank.BankAccountId AND account.TenantId=bank.TenantId
                  WHERE bank.TenantId=@TenantId AND account.AccountId=@AccountId AND bank.IsActive=1
                    AND account.FinancialFunction=N'Bank')
                  THROW 51411,N'Una cuenta vinculada a un banco activo no puede exigir tercero.',1;
                IF @Configure=1 AND EXISTS(SELECT 1 FROM dbo.AccountingAccounts
                  WHERE TenantId=@TenantId AND AccountId=@AccountId AND ConfigurationLockedAt IS NOT NULL)
                  THROW 51415,N'La configuración financiera ya fue utilizada y no puede reasignarse.',1;
                UPDATE dbo.AccountingAccounts SET Name=@Name,RequiresParty=@RequiresParty,
                  ParentAccountId=CASE WHEN @Configure=1 THEN @Parent ELSE ParentAccountId END,
                  FinancialFunction=CASE WHEN @Configure=1 THEN @Function ELSE FinancialFunction END,
                  BalanceSide=CASE WHEN @Configure=1 THEN @Balance ELSE BalanceSide END,
                  BusinessId=CASE WHEN @Configure=1 THEN @BusinessId ELSE BusinessId END,
                  BankAccountId=CASE WHEN @Configure=1 THEN @BankId ELSE BankAccountId END,
                  PaymentMethodOptionId=CASE WHEN @Configure=1 THEN @PaymentId ELSE PaymentMethodOptionId END
                WHERE AccountId=@AccountId AND TenantId=@TenantId AND RowVersion=@Version
                  AND (Name COLLATE Latin1_General_100_BIN2<>@Name OR RequiresParty<>@RequiresParty OR @Configure=1);
                SELECT AccountId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,RowVersion,
                  ParentAccountId,FinancialFunction,BalanceSide,BusinessId,BankAccountId,PaymentMethodOptionId,ConfigurationLockedAt
                FROM dbo.AccountingAccounts WHERE AccountId=@AccountId AND TenantId=@TenantId;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@AccountId", accountId);
            command.Parameters.Add("@Version", SqlDbType.Binary, 8).Value = version;
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@RequiresParty", request.RequiresParty);
            command.Parameters.AddWithValue("@Configure", configurationChanged);
            AddAccountConfigurationParameters(command, request.Semantics);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new AccountingConflictException("La cuenta cambió. Actualiza la lista antes de guardar.");
            var result = ReadAccount(reader);
            await reader.DisposeAsync();
            return result;
        }
        catch (SqlException exception) when (exception.Number == 51415)
        {
            throw new AccountingConflictException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number == 51411)
        {
            throw new AccountingValidationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            throw new AccountingValidationException("Las propiedades no cumplen las reglas contables de la cuenta.");
        }
    }

    private static AccountingAccountView ReadAccount(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6),
        PucLevel(reader.GetString(1)), Convert.ToBase64String((byte[])reader[7]),
        reader.IsDBNull(9) ? null : new(
            reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.GetString(9), reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11), reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13)), !reader.IsDBNull(14));

    public async Task<AccountingCostCenterView> CreateCostCenterAsync(
        AccountingUserIdentity user, CreateCostCenterRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51400,'The cost center business is outside the legal entity.',1;
            IF @ParentCostCenterId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM dbo.AccountingCostCenters
              WHERE CostCenterId=@ParentCostCenterId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51400,'The parent must be active in this business.',1;
            INSERT dbo.AccountingCostCenters
            (CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
            VALUES(@CostCenterId,@BusinessId,@Code,@Name,@ParentCostCenterId,@IsDefault,1,@Now);
            """;
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CostCenterId", request.CostCenterId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Code", request.Code); command.Parameters.AddWithValue("@Name", request.Name); command.Parameters.AddWithValue("@ParentCostCenterId", (object?)request.ParentCostCenterId ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsDefault", request.IsDefault); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await ExecuteMutationAsync(command, cancellationToken, "The cost center conflicts with an existing code, parent or default center.");
        return (await ListCostCentersAsync(user, cancellationToken)).Single(row => row.CostCenterId == request.CostCenterId);
    }

    public async Task<AccountingCostCenterView> SetCostCenterStatusAsync(
        AccountingUserIdentity user, Guid costCenterId, SetAccountingCostCenterStatusRequest request,
        CancellationToken cancellationToken)
    {
        var current = (await ListCostCentersAsync(user, cancellationToken))
            .SingleOrDefault(row => row.CostCenterId == costCenterId)
            ?? throw new AccountingValidationException("El centro no pertenece a esta sede.");
        return await UpdateCostCenterAsync(user, costCenterId, new(
            current.Code, current.Name, current.ParentCostCenterId, current.IsDefault,
            request.IsActive, request.RowVersion ?? ""), cancellationToken);
    }

    public async Task<AccountingCostCenterView> UpdateCostCenterAsync(
        AccountingUserIdentity user, Guid costCenterId, UpdateCostCenterRequest request,
        CancellationToken cancellationToken)
    {
        var version = RequiredRecordVersion(request.RowVersion);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51411,'El centro no pertenece a esta empresa.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND RowVersion=@Version)
                  THROW 51415,'El centro cambió. Actualiza la lista antes de guardar.',1;
                IF @ParentCostCenterId IS NOT NULL AND NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@ParentCostCenterId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51411,'El centro superior debe estar activo en esta sede.',1;
                DECLARE @Ancestor uniqueidentifier=@ParentCostCenterId;
                DECLARE @Visited TABLE(Id uniqueidentifier PRIMARY KEY);
                WHILE @Ancestor IS NOT NULL
                BEGIN
                  IF @Ancestor=@CostCenterId OR EXISTS(SELECT 1 FROM @Visited WHERE Id=@Ancestor)
                    THROW 51411,'El centro superior genera un ciclo.',1;
                  INSERT @Visited VALUES(@Ancestor);
                  SELECT @Ancestor=ParentCostCenterId FROM dbo.AccountingCostCenters
                    WHERE CostCenterId=@Ancestor AND BusinessId=@BusinessId;
                END;
                IF @IsDefault=0 AND EXISTS(SELECT 1 FROM dbo.AccountingCostCenters
                  WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND IsDefault=1)
                  THROW 51412,'Selecciona otro centro como predeterminado antes de sustituir este.',1;
                IF @IsActive=0
                BEGIN
                  IF @IsDefault=1
                    THROW 51412,'El centro predeterminado debe permanecer activo.',1;
                  DECLARE @Dependencies nvarchar(2048);
                  SELECT @Dependencies=STRING_AGG(CONVERT(nvarchar(max),Label),N'; ')
                  FROM (
                    SELECT N'Centro hijo: '+Code+N' · '+Name Label FROM dbo.AccountingCostCenters
                    WHERE BusinessId=@BusinessId AND ParentCostCenterId=@CostCenterId AND IsActive=1
                    UNION ALL
                    SELECT N'Regla: '+OperationKind+COALESCE(N' / '+CONVERT(nvarchar(36),WarehouseId),N' / general')
                    FROM dbo.AccountingCostCenterAssignments
                    WHERE BusinessId=@BusinessId AND CostCenterId=@CostCenterId AND IsActive=1
                    UNION ALL
                    SELECT N'Concepto de gasto: '+Code+N' · '+Name FROM dbo.ExpenseConcepts
                    WHERE BusinessId=@BusinessId AND DefaultCostCenterId=@CostCenterId AND IsActive=1
                    UNION ALL
                    SELECT N'Motivo: '+Code+N' · '+Name FROM dbo.BusinessReasons
                    WHERE BusinessId=@BusinessId AND DefaultCostCenterId=@CostCenterId AND IsActive=1
                  ) dependencies;
                  IF @Dependencies IS NOT NULL
                    THROW 51413,@Dependencies,1;
                END;
                IF @IsDefault=1
                  UPDATE dbo.AccountingCostCenters SET IsDefault=0
                  WHERE BusinessId=@BusinessId AND IsDefault=1 AND CostCenterId<>@CostCenterId;
                UPDATE dbo.AccountingCostCenters SET Code=@Code,Name=@Name,
                  ParentCostCenterId=@ParentCostCenterId,IsDefault=@IsDefault,IsActive=@IsActive
                WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId;
                SELECT CostCenterId,BusinessId,Code,Name,ParentCostCenterId,IsDefault,IsActive,RowVersion
                FROM dbo.AccountingCostCenters WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@CostCenterId", costCenterId);
            command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@ParentCostCenterId", (object?)request.ParentCostCenterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsDefault", request.IsDefault);
            command.Parameters.AddWithValue("@IsActive", request.IsActive);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var result = new AccountingCostCenterView(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetBoolean(5), reader.GetBoolean(6), Convert.ToBase64String((byte[])reader[7]));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException error) when (error.Number == 51415 || error.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Number == 51415 ? error.Message : "El código del centro ya existe en esta sede.");
        }
        catch (SqlException error) when (error.Number is 51411 or 51412 or 51413)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingValidationException(error.Message);
        }
    }

    private static byte[] RequiredRecordVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AccountingConflictException("Actualiza la lista para obtener la versión vigente.");
        try
        {
            var version = Convert.FromBase64String(value);
            if (version.Length == 8) return version;
        }
        catch (FormatException)
        {
            throw new AccountingValidationException("La versión del registro no es válida.");
        }
        throw new AccountingValidationException("La versión del registro no es válida.");
    }

    public async Task<AccountingPeriodView> CreatePeriodAsync(
        AccountingUserIdentity user, CreateAccountingPeriodRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var overlap = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM dbo.AccountingPeriods WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND StartsOn<=@EndsOn AND EndsOn>=@StartsOn)
                  THROW 51401,'The accounting period overlaps an existing period.',1;
                """, connection, transaction))
            {
                AddPeriod(overlap, user.TenantId, request.StartsOn, request.EndsOn);
                await overlap.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var insert = new SqlCommand("""
                INSERT dbo.AccountingPeriods(PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                VALUES(@PeriodId,@TenantId,@Name,@StartsOn,@EndsOn,N'Open',@Now);
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@PeriodId", request.PeriodId); insert.Parameters.AddWithValue("@Name", request.Name); insert.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                AddPeriod(insert, user.TenantId, request.StartsOn, request.EndsOn);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (IsConflict(exception))
        { await transaction.RollbackAsync(CancellationToken.None); throw new AccountingConflictException(exception.Message); }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
        return new(request.PeriodId, request.StartsOn, request.EndsOn, request.Name, "Open");
    }

    public async Task SetMappingAsync(
        AccountingUserIdentity user, SetAccountMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var validate = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM accounting.PostingAccountReadiness(@TenantId,@BusinessId,@Category,@AccountId))
                  THROW 51402,N'El auxiliar no está disponible o no corresponde al tipo contable y configuración de esta categoría.',1;
                IF @BusinessId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Businesses
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51403,'The mapping business is outside the legal entity.',1;
                IF EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND Category=@Category
                    AND ((BusinessId=@BusinessId) OR (BusinessId IS NULL AND @BusinessId IS NULL))
                    AND EffectiveFrom<>@EffectiveFrom
                    AND EffectiveFrom<=COALESCE(@EffectiveTo,'9999-12-31')
                    AND COALESCE(EffectiveTo,'9999-12-31')>=@EffectiveFrom)
                  THROW 51404,'The accounting mapping overlaps another validity range.',1;
                """, connection, transaction))
            { AddMapping(validate, request); await validate.ExecuteNonQueryAsync(cancellationToken); }
            await using (var upsert = new SqlCommand("""
                DELETE dbo.AccountingAccountMappings
                WHERE TenantId=@TenantId AND Category=@Category AND EffectiveFrom=@EffectiveFrom
                  AND ((BusinessId=@BusinessId) OR (BusinessId IS NULL AND @BusinessId IS NULL));
                INSERT dbo.AccountingAccountMappings
                (MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                VALUES(@MappingId,@TenantId,@BusinessId,@Category,@AccountId,@EffectiveFrom,@EffectiveTo,@Now);
                """, connection, transaction))
            {
                AddMapping(upsert, request); upsert.Parameters.AddWithValue("@MappingId", ids.NewId()); upsert.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (IsConflict(exception))
        { await transaction.RollbackAsync(CancellationToken.None); throw new AccountingConflictException(exception.Message); }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task ClosePeriodAsync(
        AccountingUserIdentity user, Guid periodId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            DateTime startsOn; DateTime endsOn; string status;
            await using (var read = new SqlCommand("""
                SELECT StartsOn,EndsOn,Status FROM dbo.AccountingPeriods WITH(UPDLOCK,HOLDLOCK)
                WHERE PeriodId=@PeriodId AND TenantId=@TenantId;
                """, connection, transaction))
            {
                read.Parameters.AddWithValue("@PeriodId", periodId); read.Parameters.AddWithValue("@TenantId", user.TenantId);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) throw new AccountingConflictException("El periodo contable no existe.");
                startsOn = reader.GetDateTime(0); endsOn = reader.GetDateTime(1); status = reader.GetString(2);
            }
            if (status == "Closed") { await transaction.CommitAsync(cancellationToken); return; }
            await using (var pending = new SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs
                   WHERE TenantId=@TenantId AND CAST(OccurredAt AS date) BETWEEN @StartsOn AND @EndsOn
                     AND Status NOT IN(N'Posted',N'CommercialEffectsApplied'))
                  +
                  (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments source
                   WHERE source.TenantId=@TenantId
                     AND CAST(source.OccurredAt AS date) BETWEEN @StartsOn AND @EndsOn
                     AND NOT EXISTS(
                       SELECT 1 FROM dbo.AccountingPostingJobs job
                       WHERE job.SourceDocumentId=source.SourceDocumentId
                         AND job.SourceDocumentType=source.SourceDocumentType
                         AND job.BusinessId=source.BusinessId
                         AND job.TenantId=source.TenantId));
                """, connection, transaction))
            {
                pending.Parameters.AddWithValue("@TenantId", user.TenantId); pending.Parameters.AddWithValue("@StartsOn", startsOn); pending.Parameters.AddWithValue("@EndsOn", endsOn);
                if (Convert.ToInt32(await pending.ExecuteScalarAsync(cancellationToken)) > 0)
                    throw new AccountingConflictException("El periodo tiene documentos pendientes de configuración o contabilización.");
            }
            await using (var close = new SqlCommand("""
                UPDATE dbo.AccountingPeriods SET Status=N'Closed',ClosedAt=@Now,ClosedByUserId=@UserId
                WHERE PeriodId=@PeriodId AND TenantId=@TenantId AND Status=N'Open';
                """, connection, transaction))
            {
                close.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow()); close.Parameters.AddWithValue("@UserId", user.UserId); close.Parameters.AddWithValue("@PeriodId", periodId); close.Parameters.AddWithValue("@TenantId", user.TenantId);
                if (await close.ExecuteNonQueryAsync(cancellationToken) != 1) throw new DBConcurrencyException("The accounting period could not be closed.");
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<AccountingPostingView?> RetryPostingAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        var source = await FindPostingSourceAsync(user, documentId, cancellationToken) ??
            await RecoverMissingPostingJobAsync(user, documentId, cancellationToken);
        if (source is null) return null;
        await postingProcessor.ProcessAsync(documentId, source, user.BusinessId, cancellationToken);
        return await FindPostingAsync(user, documentId, cancellationToken);
    }

    private async Task<string?> RecoverMissingPostingJobAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @DocumentType nvarchar(64),@Hash binary(32),
                        @OccurredAt datetimeoffset(7),@AccountingEntryRequired bit;
                SELECT @DocumentType=source.SourceDocumentType,@Hash=source.PayloadHash,
                       @OccurredAt=source.OccurredAt,
                       @AccountingEntryRequired=source.AccountingEntryRequired
                FROM dbo.AccountingSourceDocuments source WITH(UPDLOCK,HOLDLOCK)
                WHERE source.SourceDocumentId=@DocumentId
                  AND source.BusinessId=@BusinessId AND source.TenantId=@TenantId
                  AND source.SourceDocumentType IN(SELECT value FROM OPENJSON(@AccountableTypes));
                IF @DocumentType IS NULL SELECT CAST(NULL AS nvarchar(64));
                ELSE IF @AccountingEntryRequired IS NULL
                  THROW 51405,N'The legacy accounting source has no provable frozen mode.',1;
                ELSE BEGIN
                  IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs WITH(UPDLOCK,HOLDLOCK)
                    WHERE SourceDocumentId=@DocumentId AND SourceDocumentType=@DocumentType)
                    INSERT dbo.AccountingPostingJobs
                      (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                       SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,
                       Status,AttemptCount,CreatedAt)
                    VALUES(@JobId,@TenantId,@BusinessId,@DocumentId,@DocumentType,@Hash,
                           @OccurredAt,@AccountingEntryRequired,N'Pending',0,@Now);
                  UPDATE source SET AccountingPostingJobId=job.AccountingPostingJobId
                  FROM dbo.AccountingSourceDocuments source
                  JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId
                    AND job.SourceDocumentType=source.SourceDocumentType
                    AND job.TenantId=source.TenantId AND job.BusinessId=source.BusinessId
                  WHERE source.SourceDocumentId=@DocumentId AND source.SourceDocumentType=@DocumentType
                    AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
                    AND source.AccountingPostingJobId IS NULL;
                  SELECT @DocumentType;
                END;
                """, connection, transaction);
            AddScope(command, user, documentId);
            AddAccountableTypes(command);
            command.Parameters.AddWithValue("@JobId", ids.NewId());
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            var source = await command.ExecuteScalarAsync(cancellationToken) as string;
            await transaction.CommitAsync(cancellationToken);
            return source;
        }
        catch (SqlException error) when (error.Number == 51405)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task<AccountingPostingView?> GetPostingAsync(
        AccountingUserIdentity user, Guid documentId,
        CancellationToken cancellationToken) =>
        FindPostingAsync(user, documentId, cancellationToken);

    public async Task<AccountingEntryView?> GetEntryAsync(
        AccountingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        Guid entryId; Guid? mainPartyId; string number; string type; string description; DateTimeOffset occurred; DateTimeOffset posted; decimal debit; decimal credit;
        await using (var command = new SqlCommand("""
            SELECT job.EntryId,job.EntryNumber,job.SourceDocumentType,job.OccurredAt,job.PostedAt,job.Description,job.DebitTotal,job.CreditTotal,
              mainParty.PartyId
            FROM dbo.AccountingEntries job
            LEFT JOIN accounting.VoucherDrafts draft ON draft.DocumentId=job.SourceDocumentId
              AND draft.DocumentType=job.SourceDocumentType AND draft.TenantId=job.TenantId AND draft.BusinessId=job.BusinessId
            """ + "\n" + DocumentPrincipalPartySql + "\n" + """
            WHERE job.TenantId=@TenantId AND job.BusinessId=@BusinessId AND job.SourceDocumentId=@DocumentId;
            """, connection))
        {
            AddScope(command, user, documentId); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            entryId = reader.GetGuid(0); number = reader.GetString(1); type = reader.GetString(2); occurred = reader.GetDateTimeOffset(3); posted = reader.GetDateTimeOffset(4); description = reader.GetString(5); debit = reader.GetDecimal(6); credit = reader.GetDecimal(7);
            mainPartyId = NullableId(reader, 8);
        }
        var lines = new List<AccountingEntryLineView>();
        await using (var command = new SqlCommand("""
            SELECT l.LineNumber,a.Code,a.Name,l.Debit,l.Credit,l.PartyId,
                   l.PartyIdentificationSnapshot,l.PartyNameSnapshot,l.CostCenterId,
                   l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot,l.Description,l.AccountId,l.SourceLineId,l.BaseAmount
            FROM dbo.AccountingEntryLines l
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId
            WHERE l.EntryId=@EntryId ORDER BY l.LineNumber;
            """, connection))
        {
            command.Parameters.AddWithValue("@EntryId", entryId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) lines.Add(new(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.GetGuid(12), NullableId(reader, 13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14)));
        }
        return new(entryId, number, documentId, type, occurred, posted, description, debit, credit, lines, mainPartyId);
    }

    public async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            -- Materialize balances once before expanding the configured hierarchy.
            SELECT a.AccountId,a.Code,a.ParentAccountId,
              SUM(CASE WHEN CAST(e.OccurredAt AS date)<@From THEN l.Debit-l.Credit ELSE 0 END) OpeningBalance,
              SUM(CASE WHEN CAST(e.OccurredAt AS date) BETWEEN @From AND @To THEN l.Debit ELSE 0 END) Debit,
              SUM(CASE WHEN CAST(e.OccurredAt AS date) BETWEEN @From AND @To THEN l.Credit ELSE 0 END) Credit,
              SUM(l.Debit-l.Credit) ClosingBalance
            INTO #ReportAccounts
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND CAST(e.OccurredAt AS date)<=@To
            GROUP BY a.AccountId,a.Code,a.ParentAccountId;

            """ + "\n" + ReportAccountHierarchySql + "\n" + """
            SELECT hierarchy.Code,COALESCE(configured.Name,hierarchy.Code),
              CASE LEN(hierarchy.Code)
                WHEN 1 THEN N'Class' WHEN 2 THEN N'Group'
                WHEN 4 THEN N'Account' WHEN 6 THEN N'Subaccount' ELSE N'Auxiliary' END,
              SUM(leaf.OpeningBalance),SUM(leaf.Debit),SUM(leaf.Credit),SUM(leaf.ClosingBalance)
            FROM Hierarchy hierarchy
            JOIN #ReportAccounts leaf ON leaf.AccountId=hierarchy.LeafAccountId
            LEFT JOIN dbo.AccountingAccounts configured
              ON configured.TenantId=@TenantId AND configured.Code=hierarchy.Code
            GROUP BY hierarchy.Code,configured.Name
            ORDER BY hierarchy.Code
            OPTION(MAXRECURSION 32);
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        var rows = new List<TrialBalanceRow>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var opening = reader.GetDecimal(3);
            var closing = reader.GetDecimal(6);
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                Math.Max(opening, 0), Math.Max(-opening, 0), reader.GetDecimal(4),
                reader.GetDecimal(5), Math.Max(closing, 0), Math.Max(-closing, 0)));
        }
        return rows;
    }

    private static string PucLevel(string code) => code.Length switch
    {
        1 => "Class",
        2 => "Group",
        4 => "Account",
        6 => "Subaccount",
        > 6 => "Auxiliary",
        _ => "Invalid"
    };

    public async Task<IReadOnlyList<AccountingJournalRow>> GetJournalAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT e.EntryId,e.EntryNumber,e.OccurredAt,e.SourceDocumentId,
                   e.SourceDocumentType,l.LineNumber,a.Code,a.Name,l.PartyId,
                   l.CostCenterId,l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot,
                   l.Description,l.Debit,l.Credit
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND CAST(e.OccurredAt AS date) BETWEEN @From AND @To
            ORDER BY e.OccurredAt,e.EntryNumber,l.LineNumber;
            """, connection);
        AddReportScope(command, user, from, to);
        var rows = new List<AccountingJournalRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetInt32(5), reader.GetString(6),
                reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12),
                reader.GetDecimal(13), reader.GetDecimal(14)));
        return rows;
    }

    public async Task<IReadOnlyList<GeneralLedgerRow>> GetGeneralLedgerAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT a.Code,a.Name,a.AccountType,
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date)<@From THEN l.Debit-l.Credit ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date) BETWEEN @From AND @To THEN l.Debit ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date) BETWEEN @From AND @To THEN l.Credit ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN CAST(e.OccurredAt AS date)<=@To THEN l.Debit-l.Credit ELSE 0 END),0)
            FROM dbo.AccountingAccounts a
            LEFT JOIN dbo.AccountingEntryLines l ON l.AccountId=a.AccountId
            LEFT JOIN dbo.AccountingEntries e ON e.EntryId=l.EntryId AND e.BusinessId=@BusinessId
            WHERE a.TenantId=@TenantId AND a.AllowsPosting=1
            GROUP BY a.Code,a.Name,a.AccountType
            HAVING COALESCE(SUM(CASE WHEN e.BusinessId=@BusinessId AND CAST(e.OccurredAt AS date)<=@To
                               THEN ABS(l.Debit)+ABS(l.Credit) ELSE 0 END),0)>0
            ORDER BY a.Code;
            """, connection);
        AddReportScope(command, user, from, to);
        var rows = new List<GeneralLedgerRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6)));
        return rows;
    }

    public async Task<IReadOnlyList<FinancialStatementRow>> GetBalanceSheetAsync(
        AccountingUserIdentity user, DateOnly asOf, CancellationToken cancellationToken)
    {
        var start = new DateOnly(1900, 1, 1);
        var rows = (await GetFinancialStatementAsync(user, start, asOf,
            ["Asset", "Liability", "Equity"], cancellationToken)).ToList();
        var income = await GetFinancialStatementAsync(user, start, asOf,
            ["Revenue", "ContraRevenue", "Expense"], cancellationToken);
        var netIncome = income.Sum(row => row.Section == "Revenue"
            ? row.Amount : -row.Amount);
        if (netIncome != 0)
            rows.Add(new("Equity", "RESULTADO", "Resultado acumulado del ejercicio",
                decimal.Round(netIncome, 4)));
        return rows;
    }

    public Task<IReadOnlyList<FinancialStatementRow>> GetIncomeStatementAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken) => GetFinancialStatementAsync(
            user, from, to, ["Revenue", "ContraRevenue", "Expense"], cancellationToken);

    private async Task<IReadOnlyList<FinancialStatementRow>> GetFinancialStatementAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        IReadOnlyList<string> accountTypes, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT a.AccountType,a.Code,a.Name,
              SUM(CASE WHEN a.AccountType IN(N'Liability',N'Equity',N'Revenue')
                       THEN l.Credit-l.Debit ELSE l.Debit-l.Credit END)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND CAST(e.OccurredAt AS date) BETWEEN @From AND @To
              AND a.AccountType IN (SELECT value FROM STRING_SPLIT(@Types,N','))
            GROUP BY a.AccountType,a.Code,a.Name
            HAVING SUM(ABS(l.Debit)+ABS(l.Credit))>0
            ORDER BY CASE a.AccountType WHEN N'Asset' THEN 1 WHEN N'Liability' THEN 2
                     WHEN N'Equity' THEN 3 WHEN N'Revenue' THEN 4
                     WHEN N'ContraRevenue' THEN 5 ELSE 6 END,a.Code;
            """, connection);
        AddReportScope(command, user, from, to);
        command.Parameters.AddWithValue("@Types", string.Join(',', accountTypes));
        var rows = new List<FinancialStatementRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3)));
        return rows;
    }

    public async Task<IReadOnlyList<AccountingExceptionRow>> GetExceptionsAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT exception.SourceDocumentId,exception.SourceDocumentType,
                   exception.OccurredAt,exception.Status,
                   exception.LastErrorCode,exception.LastErrorMessage
            FROM (
              SELECT SourceDocumentId,SourceDocumentType,OccurredAt,Status,
                     LastErrorCode,LastErrorMessage
              FROM dbo.AccountingPostingJobs
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                AND Status NOT IN(N'Posted',N'CommercialEffectsApplied')
              UNION ALL
              SELECT source.SourceDocumentId,source.SourceDocumentType,source.OccurredAt,
                     N'MissingAccountingJob',N'MissingAccountingJob',
                     N'The immutable accounting source has no durable posting job.'
              FROM dbo.AccountingSourceDocuments source
              WHERE source.TenantId=@TenantId AND source.BusinessId=@BusinessId
                AND NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingPostingJobs job
                  WHERE job.SourceDocumentId=source.SourceDocumentId
                    AND job.SourceDocumentType=source.SourceDocumentType
                    AND job.BusinessId=source.BusinessId
                    AND job.TenantId=source.TenantId)
            ) exception
            WHERE CAST(exception.OccurredAt AS date) BETWEEN @From AND @To
            ORDER BY OccurredAt,SourceDocumentType,SourceDocumentId;
            """, connection);
        AddReportScope(command, user, from, to);
        var rows = new List<AccountingExceptionRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetDateTimeOffset(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }

    public async Task<AccountingDocumentPage> ListDocumentsAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType,
        string? status, string? search, int page, int pageSize,
        CancellationToken cancellationToken, Guid? partyId = null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(TraceabilityDocumentsSql(search, partyId, status is not null) + """
            SELECT * FROM #Documents ORDER BY OccurredAt DESC,SourceDocumentId,SourceDocumentType;
            SELECT COUNT(*) FROM #DocumentKeys;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@DocumentType", (object?)documentType ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartyId", (object?)partyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var rows = new List<AccountingDocumentRow>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTimeOffset(3),
                reader.GetString(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetDateTimeOffset(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17), reader.GetBoolean(18),
                NullableId(reader, 19), NullableText(reader, 20), NullableText(reader, 21),
                NullableId(reader, 22), NullableText(reader, 23)));
        }
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        total = reader.GetInt32(0);
        return new(rows, page, pageSize, total);
    }

    public async Task<FinancialTraceabilityLinePage> ListFinancialTraceabilityLinesAsync(
        AccountingUserIdentity user, DateOnly from, DateOnly to, string? documentType,
        string? status, string? search, int page, int pageSize,
        CancellationToken cancellationToken, Guid? partyId = null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(TraceabilityDocumentKeysSql(search, partyId) + """
            SELECT d.*,line.LineNumber INTO #TraceabilityLinePage
            FROM #DocumentKeys d
            OUTER APPLY(
              SELECT l.LineNumber FROM dbo.AccountingEntryLines l WHERE l.EntryId=d.EntryId
              UNION ALL
              SELECT l.LineNumber FROM accounting.VoucherDraftLines l
                WHERE l.DocumentId=d.SourceDocumentId AND d.EntryId IS NULL
            ) line
            ORDER BY d.OccurredAt DESC,d.SourceDocumentId,d.SourceDocumentType,line.LineNumber
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            SELECT DISTINCT SourceDocumentId,SourceDocumentType,OccurredAt,Status,EntryId
            INTO #DocumentPage FROM #TraceabilityLinePage;
            """ + TraceabilityDocumentDetailsSql + """
            SELECT d.SourceDocumentId,d.SourceDocumentType,d.DocumentNumber,d.OccurredAt,d.Status,d.LastErrorMessage,
              d.EntryNumber,d.PostedAt,d.FiscalDocumentType,d.FiscalNumber,d.FiscalStatus,
              selected.LineNumber,account.Code,account.Name,
              CASE WHEN d.EntryId IS NOT NULL THEN posted.PartyIdentificationSnapshot ELSE party.Identification END,
              CASE WHEN d.EntryId IS NOT NULL THEN posted.PartyNameSnapshot ELSE party.DisplayName END,
              CASE WHEN d.EntryId IS NOT NULL THEN posted.CostCenterCodeSnapshot ELSE center.Code END,
              CASE WHEN d.EntryId IS NOT NULL THEN posted.CostCenterNameSnapshot ELSE center.Name END,
              COALESCE(posted.Description,draft.Description),COALESCE(posted.Debit,draft.Debit),COALESCE(posted.Credit,draft.Credit)
            FROM #TraceabilityLinePage selected
            JOIN #Documents d ON d.SourceDocumentId=selected.SourceDocumentId AND d.SourceDocumentType=selected.SourceDocumentType
            LEFT JOIN dbo.AccountingEntryLines posted ON posted.EntryId=d.EntryId AND posted.LineNumber=selected.LineNumber
            LEFT JOIN accounting.VoucherDraftLines draft ON draft.DocumentId=d.SourceDocumentId
              AND draft.LineNumber=selected.LineNumber AND d.EntryId IS NULL
            LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=COALESCE(posted.AccountId,draft.AccountId) AND account.TenantId=@TenantId
            LEFT JOIN dbo.Parties party ON party.PartyId=draft.PartyId AND party.TenantId=@TenantId
            LEFT JOIN dbo.AccountingCostCenters center ON center.CostCenterId=draft.CostCenterId AND center.BusinessId=@BusinessId
            ORDER BY d.OccurredAt DESC,d.SourceDocumentId,d.SourceDocumentType,selected.LineNumber;
            SELECT COALESCE(SUM(CASE WHEN totals.LineCount=0 THEN 1 ELSE totals.LineCount END),0)
            FROM #DocumentKeys d
            CROSS APPLY(SELECT
              (SELECT COUNT(*) FROM dbo.AccountingEntryLines l WHERE l.EntryId=d.EntryId) +
              (SELECT COUNT(*) FROM accounting.VoucherDraftLines l WHERE l.DocumentId=d.SourceDocumentId AND d.EntryId IS NULL) LineCount
            ) totals;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@DocumentType", (object?)documentType ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartyId", (object?)partyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var rows = new List<FinancialTraceabilityLineRow>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTimeOffset(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20)));
        }
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        total = reader.GetInt32(0);
        return new(rows, page, pageSize, total);
    }

    private static void AddReportScope(
        SqlCommand command, AccountingUserIdentity user, DateOnly from, DateOnly to)
    {
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
    }

    private static void AddAccountableTypes(SqlCommand command) =>
        command.Parameters.AddWithValue("@AccountableTypes",
            JsonSerializer.Serialize(AccountingProcessingPolicy.DocumentTypes));

    public async Task<AccountingOpeningBalanceView?> GetOpeningBalanceAsync(
        AccountingUserIdentity user, DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadOpeningBalanceAsync(connection, null, user, effectiveOn, cancellationToken);
    }

    private const string OpeningBalanceLinesTableSql = """
        DECLARE @OpeningLines TABLE(LineNumber int PRIMARY KEY,AccountId uniqueidentifier,
          PartyId uniqueidentifier,CostCenterId uniqueidentifier,Description nvarchar(300),
          Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,
          ExistingPayableId uniqueidentifier,ExistingReceivableId uniqueidentifier);
        """;

    private const string OpeningBalancePositionsValidationSql = """
        DECLARE @ReconciliationLines nvarchar(max)=(SELECT LineNumber,AccountId,PartyId,Debit,Credit,
          LineId,ExistingPayableId,ExistingReceivableId FROM @OpeningLines FOR JSON PATH);
        EXEC accounting.OpeningPositionsValidate @TenantId,@BusinessId,@EffectiveOn,@ReconciliationLines;
        """;

    // Save and approval validate the same scoped batch. Neither path resolves
    // accounts, roles or centers through a separate request for each line.
    private const string OpeningBalanceDimensionsValidationSql = """
        IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
          THROW 51408,N'La sede no pertenece a la empresa.',1;
        IF EXISTS(
          SELECT 1 FROM @OpeningLines line
          LEFT JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
            AND account.IsActive=1 AND account.AllowsPosting=1
            AND (account.BusinessId IS NULL OR account.BusinessId=@BusinessId)
          LEFT JOIN dbo.Parties party ON party.PartyId=line.PartyId AND party.TenantId=@TenantId AND party.IsActive=1
          LEFT JOIN dbo.AccountingCostCenters center ON center.CostCenterId=line.CostCenterId
            AND center.BusinessId=@BusinessId AND center.IsActive=1
          WHERE account.AccountId IS NULL OR (account.RequiresParty=1 AND line.PartyId IS NULL)
            OR (line.CostCenterId IS NOT NULL AND center.CostCenterId IS NULL)
            OR (line.PartyId IS NOT NULL AND (party.PartyId IS NULL OR NOT (
              EXISTS(SELECT 1 FROM dbo.Customers customer WHERE customer.PartyId=line.PartyId AND customer.TenantId=@TenantId)
              OR EXISTS(SELECT 1 FROM dbo.Suppliers supplier WHERE supplier.PartyId=line.PartyId AND supplier.TenantId=@TenantId)
              OR EXISTS(SELECT 1 FROM dbo.CommerceSellers seller WHERE seller.PartyId=line.PartyId AND seller.TenantId=@TenantId)
              OR EXISTS(SELECT 1 FROM dbo.Carriers carrier WHERE carrier.PartyId=line.PartyId AND carrier.TenantId=@TenantId)
              OR EXISTS(SELECT 1 FROM dbo.Employees employee WHERE employee.PartyId=line.PartyId AND employee.TenantId=@TenantId)
              OR EXISTS(SELECT 1 FROM dbo.AppUsers appUser WHERE appUser.PartyId=line.PartyId AND appUser.TenantId=@TenantId)))))
          THROW 51408,N'Hay partidas con cuentas, terceros o centros de costo inválidos para esta sede.',1;
        """;

    public async Task<AccountingOpeningBalanceView> SaveOpeningBalanceAsync(
        AccountingUserIdentity user, SaveAccountingOpeningBalanceRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await EnsureOpeningBalancesEditableAsync(connection, transaction, user, cancellationToken);
            byte[]? expectedVersion = null;
            if (!string.IsNullOrWhiteSpace(request.RowVersion))
            {
                try { expectedVersion = Convert.FromBase64String(request.RowVersion); }
                catch (FormatException) { throw new AccountingValidationException("La versión del saldo inicial no es válida."); }
            }
            var now = timeProvider.GetUtcNow();
            await using (var command = new SqlCommand(OpeningBalanceLinesTableSql + "\n" + """
                INSERT @OpeningLines
                SELECT LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit,LineId,ExistingPayableId,ExistingReceivableId
                FROM OPENJSON(@Lines) WITH(LineNumber int,AccountId uniqueidentifier,PartyId uniqueidentifier,
                  CostCenterId uniqueidentifier,Description nvarchar(300),Debit decimal(19,4),Credit decimal(19,4),
                  LineId uniqueidentifier,ExistingPayableId uniqueidentifier,ExistingReceivableId uniqueidentifier);
                """ + "\n" + OpeningBalanceDimensionsValidationSql + "\n" + OpeningBalancePositionsValidationSql + "\n" + """
                DECLARE @ExistingId uniqueidentifier=(SELECT BatchId
                  FROM dbo.AccountingOpeningBalanceBatches WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND EffectiveOn=@EffectiveOn);
                IF @ExistingId IS NOT NULL AND @ExistingId<>@BatchId THROW 51405,N'An opening balance already exists for this business and date.',1;

                IF EXISTS(SELECT 1 FROM dbo.AccountingOpeningBalanceBatches WHERE BatchId=@BatchId)
                BEGIN
                  UPDATE dbo.AccountingOpeningBalanceBatches
                  SET CurrencyCode=@Currency,Description=@Description,
                      UpdatedByUserId=@UserId,UpdatedAt=@Now
                  WHERE BatchId=@BatchId AND TenantId=@TenantId AND BusinessId=@BusinessId
                    AND Status=N'Draft' AND (@ExpectedVersion IS NULL OR RowVersion=@ExpectedVersion);
                  IF @@ROWCOUNT<>1 THROW 51406,N'The opening balance changed or is no longer editable.',1;
                  DELETE dbo.AccountingOpeningBalanceLines WHERE BatchId=@BatchId;
                END
                ELSE
                  INSERT dbo.AccountingOpeningBalanceBatches
                    (BatchId,TenantId,BusinessId,EffectiveOn,CurrencyCode,Description,Status,
                     CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
                  VALUES(@BatchId,@TenantId,@BusinessId,@EffectiveOn,@Currency,@Description,N'Draft',
                         @UserId,@Now,@UserId,@Now);
                INSERT dbo.AccountingOpeningBalanceLines
                  (BatchId,LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit,LineId,ExistingPayableId,ExistingReceivableId)
                SELECT @BatchId,LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit,LineId,
                  ExistingPayableId,ExistingReceivableId FROM @OpeningLines;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@BatchId", request.BatchId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@EffectiveOn", request.EffectiveOn.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@Currency", request.CurrencyCode);
                command.Parameters.AddWithValue("@Description", request.Description.Trim());
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
                    request.Lines.Select((line, index) => new { LineNumber = index + 1, line.AccountId,
                        line.PartyId, line.CostCenterId, Description = line.Description.Trim(), line.Debit, line.Credit,
                        LineId = line.LineId ?? ids.NewId(),
                        ExistingPayableId = line.ExistingPosition?.SubledgerKind == "Payable" ? line.ExistingPosition.SubledgerId : (Guid?)null,
                        ExistingReceivableId = line.ExistingPosition?.SubledgerKind == "Receivable" ? line.ExistingPosition.SubledgerId : (Guid?)null }));
                command.Parameters.Add("@ExpectedVersion", SqlDbType.Timestamp).Value = (object?)expectedVersion ?? DBNull.Value;
                try { await command.ExecuteNonQueryAsync(cancellationToken); }
                catch (SqlException exception) when (exception.Number is 51405 or 51406)
                { throw new AccountingConflictException(exception.Message); }
            }
            var result = await ReadOpeningBalanceAsync(connection, transaction, user,
                request.EffectiveOn, cancellationToken) ?? throw new DBConcurrencyException("The opening balance was not persisted.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException error) when (error.Number == 51408)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingValidationException(error.Message, error);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AccountingOpeningBalanceView> ApproveOpeningBalanceAsync(
        AccountingUserIdentity user, Guid batchId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await EnsureOpeningBalancesEditableAsync(connection, transaction, user, cancellationToken);
            DateOnly effectiveOn;
            await using (var command = new SqlCommand("""
                SELECT EffectiveOn FROM dbo.AccountingOpeningBalanceBatches WITH(UPDLOCK,HOLDLOCK)
                WHERE BatchId=@BatchId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Draft';
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@BatchId", batchId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                if (value is not DateTime date)
                    throw new AccountingConflictException("El saldo inicial no existe o ya no está en borrador.");
                effectiveOn = DateOnly.FromDateTime(date);
            }
            await using (var validate = new SqlCommand(OpeningBalanceLinesTableSql + "\n" + """
                INSERT @OpeningLines SELECT LineNumber,AccountId,PartyId,CostCenterId,Description,Debit,Credit,
                  LineId,ExistingPayableId,ExistingReceivableId
                FROM dbo.AccountingOpeningBalanceLines WHERE BatchId=@BatchId;
                """ + "\n" + OpeningBalanceDimensionsValidationSql + "\n" + OpeningBalancePositionsValidationSql + "\n" + """
                SELECT COUNT(*),COALESCE(SUM(Debit),0),COALESCE(SUM(Credit),0) FROM @OpeningLines;
                """, connection, transaction))
            {
                validate.Parameters.AddWithValue("@BatchId", batchId);
                validate.Parameters.AddWithValue("@TenantId", user.TenantId);
                validate.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                validate.Parameters.AddWithValue("@EffectiveOn", effectiveOn.ToDateTime(TimeOnly.MinValue));
                await using var reader = await validate.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                var count = reader.GetInt32(0);
                var debit = reader.GetDecimal(1);
                var credit = reader.GetDecimal(2);
                if (count < 2) throw new AccountingValidationException("El saldo inicial requiere al menos dos líneas.");
                if (debit <= 0 || decimal.Round(debit, 4) != decimal.Round(credit, 4))
                    throw new AccountingValidationException("El saldo inicial debe estar cuadrado: débitos y créditos deben ser iguales.");
            }
            await using (var approve = new SqlCommand("""
                UPDATE dbo.AccountingOpeningBalanceBatches
                SET Status=N'Approved',ApprovedByUserId=@UserId,ApprovedAt=@Now,
                    UpdatedByUserId=@UserId,UpdatedAt=@Now
                WHERE BatchId=@BatchId AND Status=N'Draft';
                """, connection, transaction))
            {
                approve.Parameters.AddWithValue("@BatchId", batchId);
                approve.Parameters.AddWithValue("@UserId", user.UserId);
                approve.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                if (await approve.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException("The opening balance was not approved.");
            }
            var result = await ReadOpeningBalanceAsync(connection, transaction, user,
                effectiveOn, cancellationToken) ?? throw new DBConcurrencyException("The approved opening balance could not be read.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException error) when (error.Number == 51408)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingValidationException(error.Message, error);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task EnsureOpeningBalancesEditableAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        CancellationToken cancellationToken)
    {
        if (!await CanEditOpeningBalancesAsync(
                connection, transaction, user.TenantId, true, cancellationToken))
            throw new AccountingConflictException(
                "Los saldos iniciales no se pueden modificar porque la activación está en curso o ya existen movimientos contables.");
    }

    private static async Task<bool> CanEditOpeningBalancesAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid tenantId,
        bool lockSettings, CancellationToken cancellationToken)
    {
        var lockHint = lockSettings ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
        await using var command = new SqlCommand($"""
            DECLARE @Status nvarchar(16),@OpeningBalanceMode nvarchar(24),
                    @ActivatedAt datetimeoffset(7);
            SELECT @Status=Status,@OpeningBalanceMode=OpeningBalanceMode,@ActivatedAt=ActivatedAt
            FROM dbo.AccountingTenantSettings{lockHint} WHERE TenantId=@TenantId;
            SELECT CONVERT(bit,CASE
              WHEN @Status IS NULL OR @Status=N'Disabled' THEN 1
              WHEN @Status=N'Configuring' AND NOT EXISTS(
                SELECT 1 FROM dbo.AccountingTenantSettings
                WHERE TenantId=@TenantId AND EffectiveFrom IS NOT NULL) THEN 1
              WHEN @Status=N'Ready' AND @OpeningBalanceMode=N'ZeroDeclared'
               AND NOT EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments
                              WHERE TenantId=@TenantId AND AcceptedAt>=@ActivatedAt)
               AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries
                              WHERE TenantId=@TenantId AND PostedAt>=@ActivatedAt) THEN 1
              ELSE 0 END);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<AccountingOpeningBalanceView?> ReadOpeningBalanceAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT batch.BatchId,batch.BusinessId,batch.EffectiveOn,batch.CurrencyCode,batch.Description,batch.Status,
                   batch.RowVersion,batch.UpdatedAt,batch.ApprovedAt,batch.PostedAt,
                   line.LineNumber,line.AccountId,line.PartyId,line.CostCenterId,line.Description,line.Debit,line.Credit,
                   line.LineId,line.ExistingPayableId,line.ExistingReceivableId,
                   COALESCE(payable.DocumentNumber,receivable.DocumentNumber)
            FROM dbo.AccountingOpeningBalanceBatches batch
            LEFT JOIN dbo.AccountingOpeningBalanceLines line ON line.BatchId=batch.BatchId
            LEFT JOIN dbo.Payables payable ON payable.PayableId=line.ExistingPayableId AND payable.BusinessId=batch.BusinessId
            LEFT JOIN dbo.Receivables receivable ON receivable.ReceivableId=line.ExistingReceivableId AND receivable.BusinessId=batch.BusinessId
            WHERE batch.TenantId=@TenantId AND batch.BusinessId=@BusinessId AND batch.EffectiveOn=@EffectiveOn
            ORDER BY line.LineNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@EffectiveOn", effectiveOn.ToDateTime(TimeOnly.MinValue));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var batchId = reader.GetGuid(0);
        var businessId = reader.GetGuid(1);
        var currency = reader.GetString(3);
        var description = reader.GetString(4);
        var status = reader.GetString(5);
        var version = Convert.ToBase64String((byte[])reader[6]);
        var updatedAt = reader.GetDateTimeOffset(7);
        var approvedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(8);
        var postedAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(9);
        var lines = new List<AccountingOpeningBalanceLineView>();
        do
        {
            if (!reader.IsDBNull(10))
                lines.Add(new(reader.GetInt32(10), reader.GetGuid(11),
                    reader.IsDBNull(12) ? null : reader.GetGuid(12),
                    reader.IsDBNull(13) ? null : reader.GetGuid(13), reader.GetString(14),
                    reader.GetDecimal(15), reader.GetDecimal(16),
                    reader.IsDBNull(17) ? null : reader.GetGuid(17),
                    !reader.IsDBNull(18) ? new("Payable", reader.GetGuid(18)) :
                    !reader.IsDBNull(19) ? new("Receivable", reader.GetGuid(19)) : null,
                    reader.IsDBNull(20) ? null : reader.GetString(20)));
        } while (await reader.ReadAsync(cancellationToken));
        return new(batchId, businessId, effectiveOn, currency, description, status,
            lines.Sum(line => line.Debit), lines.Sum(line => line.Credit), version,
            updatedAt, approvedAt, postedAt, lines);
    }

    public async Task<AccountingReadinessView> GetReadinessAsync(
        AccountingUserIdentity user, DateOnly? effectiveFrom, string? openingBalanceMode,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var issues = await ReadActivationIssuesAsync(
            connection, null, user,
            effectiveFrom ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().Date),
            openingBalanceMode,
            cancellationToken);
        await using var command = new SqlCommand("""
            SELECT Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,ActivatedAt
            FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new(AccountingActivationStatuses.Disabled, "COP", null, null, null, issues, true);
        var status = reader.GetString(0);
        var currency = reader.GetString(1);
        var storedEffectiveFrom = reader.IsDBNull(2) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(2));
        var storedOpeningMode = reader.IsDBNull(3) ? null : reader.GetString(3);
        var activatedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(4);
        await reader.DisposeAsync();
        var canEditOpeningBalances = await CanEditOpeningBalancesAsync(
            connection, null, user.TenantId, false, cancellationToken);
        return new(status, currency,
            storedEffectiveFrom, storedOpeningMode, activatedAt, issues, canEditOpeningBalances);
    }

    public async Task<AccountingReadinessView> ActivateAsync(
        AccountingUserIdentity user, ActivateAccountingRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            AccountingReadinessView? existingReady = null;
            await using (var current = new SqlCommand("""
                SELECT Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,ActivatedAt
                FROM dbo.AccountingTenantSettings WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId;
                """, connection, transaction))
            {
                current.Parameters.AddWithValue("@TenantId", user.TenantId);
                await using var reader = await current.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken) && reader.GetString(0) == AccountingActivationStatuses.Ready)
                    existingReady = new(reader.GetString(0), reader.GetString(1),
                        reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4), [], false);
            }
            // An accepted activation is authoritative. A replay must not revalidate
            // later configuration or report a new activation timestamp.
            if (existingReady is not null && existingReady.EffectiveFrom == request.EffectiveFrom &&
                existingReady.FunctionalCurrencyCode == request.FunctionalCurrencyCode &&
                existingReady.OpeningBalanceMode == request.OpeningBalanceMode)
                return await CompleteExistingAsync(existingReady);

            var issues = await ReadActivationIssuesAsync(
                connection, transaction, user, request.EffectiveFrom,
                request.OpeningBalanceMode, cancellationToken);
            if (issues.Count > 0)
                throw new AccountingConflictException(
                    $"Accounting is not ready: {string.Join("; ", issues)}");
            var wasReady = existingReady is not null;
            var allowReadyReconfiguration = false;
            if (wasReady)
            {
                allowReadyReconfiguration = request.OpeningBalanceMode == "ImportedAndApproved"
                    && await CanEditOpeningBalancesAsync(
                        connection, transaction, user.TenantId, true, cancellationToken);
                if (request.OpeningBalanceMode == "ImportedAndApproved" && !allowReadyReconfiguration)
                    throw new AccountingConflictException(
                        "No se puede reemplazar el inicio en cero porque ya existen movimientos contables.");
            }
            if (existingReady is not null && !allowReadyReconfiguration)
                return await CompleteExistingAsync(existingReady);
            var now = timeProvider.GetUtcNow();
            await using var command = new SqlCommand("""
                MERGE dbo.AccountingTenantSettings WITH(HOLDLOCK) AS target
                USING(SELECT @TenantId TenantId) source ON target.TenantId=source.TenantId
                WHEN MATCHED AND (target.Status<>N'Ready' OR @AllowReadyReconfiguration=1) THEN UPDATE SET
                  Status=@ActivationStatus,FunctionalCurrencyCode=@Currency,EffectiveFrom=@EffectiveFrom,
                  OpeningBalanceMode=@OpeningMode,ActivationRequestedAt=@Now,
                  ActivationRequestedByUserId=@UserId,
                  ActivatedAt=CASE WHEN @ActivationStatus=N'Ready' THEN @Now ELSE NULL END,
                  ActivatedByUserId=CASE WHEN @ActivationStatus=N'Ready' THEN @UserId ELSE NULL END,
                  UpdatedAt=@Now
                WHEN NOT MATCHED THEN INSERT
                  (TenantId,Status,FunctionalCurrencyCode,EffectiveFrom,OpeningBalanceMode,
                   ActivationRequestedAt,ActivationRequestedByUserId,ActivatedAt,ActivatedByUserId,UpdatedAt)
                VALUES(@TenantId,@ActivationStatus,@Currency,@EffectiveFrom,@OpeningMode,@Now,@UserId,
                   CASE WHEN @ActivationStatus=N'Ready' THEN @Now ELSE NULL END,
                   CASE WHEN @ActivationStatus=N'Ready' THEN @UserId ELSE NULL END,@Now);

                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingVoucherCursors WITH(UPDLOCK,HOLDLOCK)
                              WHERE TenantId=@TenantId)
                  INSERT dbo.AccountingVoucherCursors(TenantId,LastAssignedNumber,UpdatedAt)
                  VALUES(@TenantId,0,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@Currency", request.FunctionalCurrencyCode);
            command.Parameters.AddWithValue("@EffectiveFrom", request.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@OpeningMode", request.OpeningBalanceMode);
            var activationStatus = request.OpeningBalanceMode == "ImportedAndApproved"
                ? AccountingActivationStatuses.Configuring : AccountingActivationStatuses.Ready;
            command.Parameters.AddWithValue("@ActivationStatus", activationStatus);
            command.Parameters.AddWithValue("@AllowReadyReconfiguration", allowReadyReconfiguration);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
            if (request.OpeningBalanceMode == "ImportedAndApproved")
                await QueueOpeningBalancesAsync(connection, transaction, user,
                    request.EffectiveFrom, now, cancellationToken);
            else if (!wasReady)
                await EnqueueBankAccountSynchronizationAsync(
                    connection, transaction, user.TenantId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(activationStatus,
                request.FunctionalCurrencyCode, request.EffectiveFrom,
                request.OpeningBalanceMode,
                activationStatus == AccountingActivationStatuses.Ready ? now : null,
                [], activationStatus != AccountingActivationStatuses.Configuring);
        }
        catch
        {
            if (transaction.Connection?.State == ConnectionState.Open)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        async Task<AccountingReadinessView> CompleteExistingAsync(AccountingReadinessView stored)
        {
            var canEdit = await CanEditOpeningBalancesAsync(
                connection, transaction, user.TenantId, true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return stored with { CanEditOpeningBalances = canEdit };
        }
    }

    private static async Task<IReadOnlyList<string>> ReadActivationIssuesAsync(
        SqlConnection connection, SqlTransaction? transaction,
        AccountingUserIdentity user, DateOnly effectiveFrom,
        string? openingBalanceMode,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Profile nvarchar(32)=(SELECT TOP(1) ProfileCode
              FROM dbo.AccountingConfigurationProfiles WHERE IsDefault=1 AND IsActive=1);
            DECLARE @NonZeroOpening bit=0,@PendingZeroCut bit=0;
            -- Only the initial declaration is checked. Current activity cannot
            -- invalidate a previously accepted zero opening or its replay.
            IF @OpeningMode=N'ZeroDeclared' AND NOT EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings
                WHERE TenantId=@TenantId AND Status=N'Ready')
            BEGIN
              IF EXISTS(SELECT 1 FROM dbo.Payables balance JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId
                  WHERE business.TenantId=@TenantId AND balance.OutstandingAmount<>0)
                OR EXISTS(SELECT 1 FROM dbo.Receivables balance JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId
                  WHERE business.TenantId=@TenantId AND balance.OutstandingAmount<>0)
                OR EXISTS(SELECT 1 FROM dbo.SupplierCredits balance JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId
                  WHERE business.TenantId=@TenantId AND balance.AvailableAmount<>0)
                OR EXISTS(SELECT 1 FROM dbo.CustomerCredits balance JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId
                  WHERE business.TenantId=@TenantId AND balance.AvailableAmount<>0)
                OR EXISTS(SELECT 1 FROM dbo.InventoryBalances balance JOIN dbo.Businesses business ON business.BusinessId=balance.BusinessId
                  WHERE business.TenantId=@TenantId AND balance.InventoryValue<>0)
                OR EXISTS(SELECT 1 FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                  WHERE entry.TenantId=@TenantId GROUP BY line.AccountId,line.PartyId HAVING SUM(line.Debit-line.Credit)<>0)
                SET @NonZeroOpening=1;
              IF EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs WHERE TenantId=@TenantId
                  AND Status IN(N'Pending',N'AccountingPendingConfiguration'))
                OR EXISTS(SELECT 1 FROM dbo.DocumentProcessingJobs job JOIN dbo.Businesses business ON business.BusinessId=job.BusinessId
                  WHERE business.TenantId=@TenantId AND job.Status<>N'Completed')
                SET @PendingZeroCut=1;
            END;
            SELECT
              (SELECT COUNT(*) FROM dbo.AccountingConfigurationProfileAccounts d
               WHERE d.ProfileCode=@Profile AND d.IsRequired=1 AND NOT EXISTS
               (SELECT 1 FROM dbo.AccountingAccountMappings m
                INNER JOIN dbo.AccountingAccounts a ON a.AccountId=m.AccountId
                WHERE m.TenantId=@TenantId AND (m.BusinessId IS NULL OR m.BusinessId=@BusinessId)
                  AND m.Category=d.Category AND a.IsActive=1 AND a.AllowsPosting=1
                  AND m.EffectiveFrom<=@EffectiveFrom
                  AND (m.EffectiveTo IS NULL OR m.EffectiveTo>=@EffectiveFrom))),
              CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingPeriods
                WHERE TenantId=@TenantId AND Status=N'Open'
                  AND @EffectiveFrom BETWEEN StartsOn AND EndsOn) THEN 1 ELSE 0 END),
              (SELECT COUNT(*) FROM dbo.Businesses b
               WHERE b.TenantId=@TenantId AND b.IsActive=1 AND NOT EXISTS
                 (SELECT 1 FROM dbo.AccountingCostCenters c
                  WHERE c.BusinessId=b.BusinessId AND c.IsDefault=1 AND c.IsActive=1)),
              (SELECT COUNT(*) FROM dbo.Businesses b
               WHERE @OpeningMode=N'ImportedAndApproved'
                 AND b.TenantId=@TenantId AND b.IsActive=1 AND NOT EXISTS
                 (SELECT 1 FROM dbo.AccountingOpeningBalanceBatches o
                  WHERE o.TenantId=@TenantId AND o.BusinessId=b.BusinessId
                    AND o.EffectiveOn=@EffectiveFrom
                    AND o.Status IN (N'Approved',N'Posted'))),
              @NonZeroOpening,@PendingZeroCut;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@EffectiveFrom", effectiveFrom.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@OpeningMode", (object?)openingBalanceMode ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var issues = new List<string>();
        if (reader.GetInt32(0) > 0) issues.Add("Faltan cuentas automáticas obligatorias asociadas a cuentas PUC activas.");
        if (!reader.GetBoolean(1)) issues.Add("No existe un periodo abierto que incluya la fecha efectiva.");
        if (reader.GetInt32(2) > 0) issues.Add("Hay un negocio activo sin centro de costo predeterminado.");
        if (reader.GetInt32(3) > 0) issues.Add("Hay un negocio activo sin saldos iniciales aprobados para la fecha efectiva.");
        if (reader.GetBoolean(4)) issues.Add("No se permite una apertura en cero: existen saldos contables, de cartera, créditos o inventario que requieren conciliación y saldos iniciales aprobados.");
        if (reader.GetBoolean(5)) issues.Add("No se permite una apertura en cero mientras existan operaciones aceptadas pendientes de procesar o corregir en el corte.");
        return issues;
    }

    private async Task QueueOpeningBalancesAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        DateOnly effectiveOn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var batches = new List<OpeningBatchPosting>();
        await using (var command = new SqlCommand("""
            SELECT b.BatchId,b.BusinessId,b.Description,l.LineNumber,l.AccountId,
                   l.PartyId,l.CostCenterId,l.Description,l.Debit,l.Credit,l.LineId,l.ExistingPayableId,l.ExistingReceivableId
            FROM dbo.AccountingOpeningBalanceBatches b WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses business ON business.BusinessId=b.BusinessId AND business.IsActive=1
            INNER JOIN dbo.AccountingOpeningBalanceLines l ON l.BatchId=b.BatchId
            WHERE b.TenantId=@TenantId AND b.EffectiveOn=@EffectiveOn AND b.Status=N'Approved'
            ORDER BY b.BusinessId,l.LineNumber;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@EffectiveOn", effectiveOn.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var batchId = reader.GetGuid(0);
                var batch = batches.LastOrDefault(value => value.BatchId == batchId);
                if (batch is null)
                {
                    batch = new(batchId, reader.GetGuid(1), reader.GetString(2), []);
                    batches.Add(batch);
                }
                batch.Lines.Add(new(reader.GetInt32(3), reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7),
                    reader.GetDecimal(8), reader.GetDecimal(9),
                    reader.IsDBNull(10) ? null : reader.GetGuid(10),
                    !reader.IsDBNull(11) ? new("Payable", reader.GetGuid(11)) :
                    !reader.IsDBNull(12) ? new("Receivable", reader.GetGuid(12)) : null));
            }
        }

        var occurredAt = new DateTimeOffset(effectiveOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        foreach (var page in batches.Chunk(100))
            await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                page.Select(batch => new SqlAccountingPostingJobWriter.Source(user.TenantId, batch.BusinessId,
                    batch.BatchId, "AccountingOpeningBalance",
                    batch.Lines.Any(line => line.ExistingPosition is not null)
                    ? JsonSerializer.Serialize(new AccountingReconciledOpeningPayload(
                        batch.BatchId, batch.BusinessId, occurredAt, "OPENING_BALANCE", batch.Description,
                        batch.Lines.Select(line => new AccountingReconciledOpeningLine(line.LineNumber,
                            line.AccountId, line.PartyId, line.CostCenterId, line.Description,
                            line.Debit, line.Credit, line.LineId, line.ExistingPosition)).ToArray()))
                    : JsonSerializer.Serialize(new ConfirmManualAccountingVoucherRequest(
                        batch.BatchId, batch.BusinessId, occurredAt, "OPENING_BALANCE", batch.Description,
                        batch.Lines.Select(line => new ManualVoucherLineRequest(line.AccountId, line.PartyId,
                            line.CostCenterId, line.Description, line.Debit, line.Credit)).ToArray())),
                    occurredAt, ids.NewId())).ToArray(), now, AccountingJobRequirement.OpeningBalance, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingOpeningBalancePosting>> ListPendingOpeningPostingsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT SourceDocumentId,BusinessId FROM dbo.AccountingPostingJobs
            WHERE TenantId=@TenantId AND SourceDocumentType=N'AccountingOpeningBalance'
              AND Status<>N'Posted' ORDER BY OccurredAt,BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        var values = new List<AccountingOpeningBalancePosting>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values.Add(new(reader.GetGuid(0),reader.GetGuid(1)));
        return values;
    }

    public async Task<IReadOnlyList<AccountingCostCenterAssignmentView>> ListCostCenterAssignmentsAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT assignment.AssignmentId,assignment.BusinessId,assignment.CostCenterId,
                   center.Code,center.Name,assignment.OperationKind,assignment.WarehouseId,
                   warehouse.Code,warehouse.Name,assignment.IsActive,assignment.RowVersion
            FROM dbo.AccountingCostCenterAssignments assignment
            INNER JOIN dbo.AccountingCostCenters center
              ON center.BusinessId=assignment.BusinessId AND center.CostCenterId=assignment.CostCenterId
            LEFT JOIN dbo.Warehouses warehouse
              ON warehouse.BusinessId=assignment.BusinessId AND warehouse.WarehouseId=assignment.WarehouseId
            WHERE assignment.TenantId=@TenantId AND assignment.BusinessId=@BusinessId
            ORDER BY assignment.OperationKind,warehouse.Code,center.Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        var values = new List<AccountingCostCenterAssignmentView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9), Convert.ToBase64String((byte[])reader[10])));
        return values;
    }

    public async Task<AccountingCostCenterAssignmentView> SaveCostCenterAssignmentAsync(
        AccountingUserIdentity user, SaveAccountingCostCenterAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF @IsActive=1 AND NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@CostCenterId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51000,'The cost center is not active in this business.',1;
                IF @IsActive=1 AND @WarehouseId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Warehouses WITH(UPDLOCK,HOLDLOCK)
                  WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51000,'The warehouse is not active in this business.',1;
                DECLARE @Existing uniqueidentifier=(SELECT AssignmentId
                  FROM dbo.AccountingCostCenterAssignments WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND AssignmentId=@AssignmentId);
                IF @Existing IS NOT NULL AND (@Version IS NULL OR NOT EXISTS(
                  SELECT 1 FROM dbo.AccountingCostCenterAssignments
                  WHERE AssignmentId=@Existing AND RowVersion=@Version))
                  THROW 51000,'La regla cambió. Actualiza la lista antes de guardar.',1;
                IF @Existing IS NULL AND @Version IS NOT NULL
                  THROW 51000,'La regla no pertenece a esta sede o ya no existe.',1;
                IF @Existing IS NULL
                BEGIN
                  INSERT dbo.AccountingCostCenterAssignments
                    (AssignmentId,TenantId,BusinessId,CostCenterId,OperationKind,WarehouseId,IsActive,CreatedAt,UpdatedAt)
                  VALUES(@AssignmentId,@TenantId,@BusinessId,@CostCenterId,@OperationKind,@WarehouseId,@IsActive,@Now,@Now);
                  SET @Existing=@AssignmentId;
                END
                ELSE UPDATE dbo.AccountingCostCenterAssignments SET CostCenterId=@CostCenterId,
                  OperationKind=@OperationKind,WarehouseId=@WarehouseId,
                  IsActive=@IsActive,UpdatedAt=@Now WHERE AssignmentId=@Existing;
                SELECT assignment.AssignmentId,assignment.BusinessId,assignment.CostCenterId,
                       center.Code,center.Name,assignment.OperationKind,assignment.WarehouseId,
                       warehouse.Code,warehouse.Name,assignment.IsActive,assignment.RowVersion
                FROM dbo.AccountingCostCenterAssignments assignment
                INNER JOIN dbo.AccountingCostCenters center ON center.CostCenterId=assignment.CostCenterId
                LEFT JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=assignment.WarehouseId
                WHERE assignment.AssignmentId=@Existing;
                """, connection, transaction);
            command.Parameters.Add("@Version", SqlDbType.Timestamp).Value =
                request.RowVersion is null ? DBNull.Value : RequiredRecordVersion(request.RowVersion);
            command.Parameters.AddWithValue("@AssignmentId", request.AssignmentId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@CostCenterId", request.CostCenterId);
            command.Parameters.AddWithValue("@OperationKind", request.OperationKind);
            command.Parameters.AddWithValue("@WarehouseId", (object?)request.WarehouseId ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsActive", request.IsActive);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var value = new AccountingCostCenterAssignmentView(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9), Convert.ToBase64String((byte[])reader[10]));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return value;
        }
        catch (SqlException error) when (error.Number is 51000 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(error.Number == 51000 ? error.Message :
                "Ya existe una regla para esa operación y bodega. Edita la regla existente.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<BankAccountView>> ListBankAccountsAsync(
        AccountingUserIdentity user, bool includeInactive, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadBankAccountsAsync(connection, null, user.TenantId, includeInactive, null, cancellationToken);
    }

    private static async Task<IReadOnlyList<BankAccountView>> ReadBankAccountsAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid tenantId, bool includeInactive,
        Guid? bankAccountId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT b.BankAccountId,CONVERT(uniqueidentifier,linked.SingleAccountId),linked.SingleCode,linked.SingleName,
                   b.AccountTypeOptionId,o.Code,o.Label,b.BankName,b.AccountNumber,
                   b.DisplayName,b.CurrencyCode,b.IsPrimary,b.IsActive,b.RowVersion,linked.AuxiliaryCount
            FROM accounting.BankAccounts b
            OUTER APPLY(SELECT COUNT(*) AuxiliaryCount,
              CASE WHEN COUNT(*)=1 THEN MAX(CONVERT(nvarchar(36),account.AccountId)) END SingleAccountId,
              CASE WHEN COUNT(*)=1 THEN MAX(account.Code) END SingleCode,
              CASE WHEN COUNT(*)=1 THEN MAX(account.Name) END SingleName
              FROM dbo.AccountingAccounts account
              WHERE account.TenantId=b.TenantId AND account.BankAccountId=b.BankAccountId) linked
            INNER JOIN reference.Options o
              ON o.OptionId=b.AccountTypeOptionId AND o.CatalogCode=N'bank-account-type'
            WHERE b.TenantId=@TenantId AND (@IncludeInactive=1 OR b.IsActive=1)
              AND (@BankAccountId IS NULL OR b.BankAccountId=@BankAccountId)
            ORDER BY b.IsPrimary DESC,b.DisplayName,b.BankAccountId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@IncludeInactive", includeInactive);
        command.Parameters.Add("@BankAccountId", SqlDbType.UniqueIdentifier).Value = (object?)bankAccountId ?? DBNull.Value;
        var values = new List<BankAccountView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetBoolean(11), reader.GetBoolean(12),
                Convert.ToBase64String((byte[])reader[13]),reader.GetInt32(14)));
        return values;
    }

    public Task<IReadOnlyList<BankAccountView>> ListActiveBankAccountsForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken) => ListBankAccountsAsync(
            new AccountingUserIdentity(Guid.Empty, tenantId, Guid.Empty,
                new HashSet<string>(StringComparer.Ordinal)), false, cancellationToken);

    public async Task<bool> IsAccountingEnabledAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1) FROM dbo.AccountingTenantSettings
            WHERE TenantId=@TenantId AND Status=N'Ready';
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<BankAccountView> SaveBankAccountAsync(
        AccountingUserIdentity user, SaveBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        byte[]? expectedVersion = null;
        if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            try { expectedVersion = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException) { throw new AccountingValidationException("La versión de la cuenta bancaria no es válida."); }
        }

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var validation = new SqlCommand("""
                IF NOT EXISTS(
                    SELECT 1 FROM reference.Options
                    WHERE OptionId=@AccountTypeOptionId AND CatalogCode=N'bank-account-type' AND IsActive=1)
                    THROW 51400,N'El tipo de cuenta bancaria no es válido.',1;
                """, connection, transaction))
            {
                validation.Parameters.AddWithValue("@AccountTypeOptionId", request.AccountTypeOptionId);
                await validation.ExecuteNonQueryAsync(cancellationToken);
            }

            var exists = false;
            var wasPrimary = false;
            await using (var current = new SqlCommand("""
                SELECT IsPrimary FROM accounting.BankAccounts WITH(UPDLOCK,HOLDLOCK)
                WHERE BankAccountId=@BankAccountId AND TenantId=@TenantId;
                """, connection, transaction))
            {
                current.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                current.Parameters.AddWithValue("@TenantId", user.TenantId);
                await using var reader = await current.ExecuteReaderAsync(cancellationToken);
                exists = await reader.ReadAsync(cancellationToken);
                if (exists) wasPrimary = reader.GetBoolean(0);
            }

            if (exists && expectedVersion is null)
                throw new AccountingConflictException("Actualiza la lista para obtener la versión vigente de la cuenta bancaria.");
            if (!exists && expectedVersion is not null)
                throw new AccountingConflictException("La cuenta bancaria no existe. Actualiza la lista antes de guardar.");
            // Compatibility input is translated once into the PUC relationship.
            // Updating a bank never rebinds its auxiliaries behind the account editor.
            if (exists && request.AccountingAccountId is { } existingAccountId)
            {
                await using var linked = new SqlCommand("""
                    SELECT COUNT(*) FROM dbo.AccountingAccounts WITH(UPDLOCK,HOLDLOCK)
                    WHERE TenantId=@TenantId AND AccountId=@AccountId AND BankAccountId=@BankAccountId;
                    """, connection, transaction);
                linked.Parameters.AddWithValue("@TenantId", user.TenantId);
                linked.Parameters.AddWithValue("@AccountId", existingAccountId);
                linked.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                if (Convert.ToInt32(await linked.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new AccountingConflictException("Configura los auxiliares y sus vínculos bancarios desde el PUC.");
            }

            var makePrimary = request.IsActive && (request.IsPrimary || !await HasActiveBankAccountAsync(
                connection, transaction, user.TenantId, request.BankAccountId, cancellationToken));
            if (wasPrimary && request.IsActive && !makePrimary)
                throw new AccountingConflictException("Selecciona otra cuenta principal antes de quitar esta condición.");
            if (wasPrimary && !request.IsActive && await HasActiveBankAccountAsync(
                    connection, transaction, user.TenantId, request.BankAccountId, cancellationToken))
                throw new AccountingConflictException("Selecciona otra cuenta principal antes de desactivar esta cuenta.");

            if (makePrimary)
            {
                await using var clear = new SqlCommand("""
                    UPDATE accounting.BankAccounts SET IsPrimary=0,UpdatedByUserId=@UserId,UpdatedAt=@Now
                    WHERE TenantId=@TenantId AND BankAccountId<>@BankAccountId AND IsPrimary=1;
                    """, connection, transaction);
                clear.Parameters.AddWithValue("@UserId", user.UserId);
                clear.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                clear.Parameters.AddWithValue("@TenantId", user.TenantId);
                clear.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var save = new SqlCommand(exists ? """
                UPDATE accounting.BankAccounts SET AccountTypeOptionId=@AccountTypeOptionId,BankName=@BankName,
                    AccountNumber=@AccountNumber,DisplayName=@DisplayName,
                    IsPrimary=@IsPrimary,IsActive=@IsActive,UpdatedByUserId=@UserId,UpdatedAt=@Now
                WHERE BankAccountId=@BankAccountId AND TenantId=@TenantId
                  AND (@ExpectedVersion IS NULL OR RowVersion=@ExpectedVersion);
                """ : """
                INSERT accounting.BankAccounts(BankAccountId,TenantId,
                    AccountTypeOptionId,BankName,AccountNumber,DisplayName,CurrencyCode,
                    IsPrimary,IsActive,CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
                VALUES(@BankAccountId,@TenantId,@AccountTypeOptionId,
                    @BankName,@AccountNumber,@DisplayName,N'COP',@IsPrimary,@IsActive,
                    @UserId,@Now,@UserId,@Now);
                """, connection, transaction);
            save.Parameters.AddWithValue("@BankAccountId", request.BankAccountId);
            save.Parameters.AddWithValue("@TenantId", user.TenantId);
            save.Parameters.AddWithValue("@AccountTypeOptionId", request.AccountTypeOptionId);
            save.Parameters.AddWithValue("@BankName", request.BankName);
            save.Parameters.AddWithValue("@AccountNumber", request.AccountNumber);
            save.Parameters.AddWithValue("@DisplayName", request.DisplayName);
            save.Parameters.AddWithValue("@IsPrimary", makePrimary);
            save.Parameters.AddWithValue("@IsActive", request.IsActive);
            save.Parameters.AddWithValue("@UserId", user.UserId);
            save.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            if (exists)
                save.Parameters.Add("@ExpectedVersion", SqlDbType.Timestamp).Value =
                    (object?)expectedVersion ?? DBNull.Value;
            if (await save.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new AccountingConflictException("La cuenta bancaria cambió. Actualiza la lista e inténtalo de nuevo.");

            if (!exists && request.AccountingAccountId is { } initialAccountId)
            {
                var account = await ReadAccountForEditAsync(connection, transaction, user.TenantId, initialAccountId, cancellationToken)
                    ?? throw new AccountingConflictException("El auxiliar no pertenece a esta empresa.");
                if (!account.IsActive || !account.AllowsPosting || account.AccountType != "Asset" || account.RequiresParty ||
                    account.Semantics is { FinancialFunction: not ("General" or "Bank") } ||
                    account.Semantics?.BankAccountId is not null)
                    throw new AccountingValidationException("Selecciona un auxiliar activo del activo, sin tercero obligatorio ni vínculo bancario previo.");
                var semantics = account.Semantics is { } configured
                    ? configured with { FinancialFunction = "Bank", BalanceSide = "Debit", BankAccountId = request.BankAccountId }
                    : new AccountingAccountSemantics(null, "Bank", "Debit", null, request.BankAccountId, null);
                await UpdateAccountInTransactionAsync(connection, transaction, user, initialAccountId,
                    new(account.Name, account.RequiresParty, account.RowVersion!, semantics), cancellationToken,
                    preserveLegacyParent: true);
            }

            await EnqueueBankAccountSynchronizationAsync(
                connection, transaction, user.TenantId, cancellationToken);

            var result = (await ReadBankAccountsAsync(connection, transaction, user.TenantId, true,
                request.BankAccountId, cancellationToken)).Single();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AccountingConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<bool> HasActiveBankAccountAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        Guid excludedId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM accounting.BankAccounts WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND IsActive=1 AND BankAccountId<>@ExcludedId)
                THEN 1 ELSE 0 END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@ExcludedId", excludedId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task EnqueueBankAccountSynchronizationAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await SqlAccountingPosSynchronizationOutbox.InsertTenantConfigurationAsync(
            connection, transaction, tenantId, ids, timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<string?> FindPostingSourceAsync(AccountingUserIdentity user, Guid documentId, CancellationToken token)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(token); await using var command = new SqlCommand("SELECT SourceDocumentType FROM dbo.AccountingPostingJobs WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND SourceDocumentId=@DocumentId", connection); AddScope(command, user, documentId); return await command.ExecuteScalarAsync(token) as string;
    }
    private async Task<AccountingPostingView?> FindPostingAsync(AccountingUserIdentity user, Guid documentId, CancellationToken token)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(token); await using var command = new SqlCommand("""
            SELECT p.SourceDocumentType,p.Status,p.LastErrorCode,p.LastErrorMessage,e.EntryId
            FROM dbo.AccountingPostingJobs p LEFT JOIN dbo.AccountingEntries e ON e.SourceDocumentId=p.SourceDocumentId AND e.SourceDocumentType=p.SourceDocumentType
            WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.SourceDocumentId=@DocumentId;
            """, connection); AddScope(command, user, documentId); await using var reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) return null; return new(documentId, reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4));
    }
    private static async Task ExecuteMutationAsync(SqlCommand command, CancellationToken token, string conflict) { try { await command.ExecuteNonQueryAsync(token); } catch (SqlException exception) when (IsConflict(exception)) { throw new AccountingConflictException(conflict); } }
    private static bool IsConflict(SqlException exception) => exception.Number is 2601 or 2627 or 547 or 51400 or 51401 or 51402 or 51403 or 51404 or 51405;
    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19;
        parameter.Scale = 4;
        parameter.Value = value;
    }
    private sealed record OpeningBatchPosting(
        Guid BatchId, Guid BusinessId, string Description,
        List<AccountingOpeningBalanceLineView> Lines);
    private static void AddPeriod(SqlCommand command, Guid tenantId, DateOnly starts, DateOnly ends) { command.Parameters.AddWithValue("@TenantId", tenantId); command.Parameters.AddWithValue("@StartsOn", starts.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EndsOn", ends.ToDateTime(TimeOnly.MinValue)); }
    private static void AddMapping(SqlCommand command, SetAccountMappingRequest value) { command.Parameters.AddWithValue("@TenantId", value.TenantId); command.Parameters.AddWithValue("@BusinessId", (object?)value.BusinessId ?? DBNull.Value); command.Parameters.AddWithValue("@Category", value.Category); command.Parameters.AddWithValue("@AccountId", value.AccountId); command.Parameters.AddWithValue("@EffectiveFrom", value.EffectiveFrom.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EffectiveTo", value.EffectiveTo is null ? DBNull.Value : value.EffectiveTo.Value.ToDateTime(TimeOnly.MinValue)); }
    private static void AddScope(SqlCommand command, AccountingUserIdentity user, Guid documentId) { command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@DocumentId", documentId); }
}
