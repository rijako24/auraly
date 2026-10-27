using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlBankReconciliationStore(
    AccountingSqlConnectionFactory connections,
    TimeProvider timeProvider) : IBankReconciliationStore
{
    public async Task<BankReconciliationPage> ListAsync(
        AccountingUserIdentity user, BankReconciliationQuery query, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await HasCompleteBusinessScopeAsync(connection, transaction, user, null,
                AccountingPermissionCodes.BankReconciliationRead, cancellationToken))
            throw new AccountingForbiddenException("No tienes permiso de conciliación sobre todas las sedes que usan las cuentas bancarias conciliadas.");
        var result = await ReadSummariesAsync(connection, transaction, user, cancellationToken, query: query);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<BankReconciliationDetailView?> GetAsync(
        AccountingUserIdentity user, Guid reconciliationId, BankReconciliationDetailQuery query, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await HasCompleteBusinessScopeAsync(connection, transaction, user, reconciliationId,
                AccountingPermissionCodes.BankReconciliationRead, cancellationToken))
            throw new AccountingForbiddenException("No tienes permiso de conciliación sobre todas las sedes que usan esta cuenta bancaria.");
        var result = await ReadDetailAsync(connection, transaction, user, reconciliationId, cancellationToken, query);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<BankReconciliationDetailView> ImportAsync(
        AccountingUserIdentity user, ImportBankReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        byte[] file;
        byte[] expectedHash;
        try
        {
            file = Convert.FromBase64String(request.OriginalFileBase64);
            expectedHash = Convert.FromHexString(request.FileSha256);
        }
        catch (FormatException)
        {
            throw new AccountingValidationException("El archivo o su huella SHA-256 no son válidos.");
        }
        if (file.Length is 0 or > 10_485_760 || expectedHash.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), expectedHash))
            throw new AccountingValidationException("El archivo supera 10 MB o no coincide con su huella SHA-256.");

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var header = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM accounting.BankAccounts b WITH(UPDLOCK,HOLDLOCK)
                  WHERE b.BankAccountId=@BankAccountId AND b.TenantId=@TenantId AND b.IsActive=1)
                  THROW 51501,N'La cuenta bancaria no existe o está inactiva.',1;
                IF NOT EXISTS(SELECT 1 FROM accounting.BankBookAccounts(@TenantId,@BankAccountId))
                  THROW 51501,N'Vincula al banco al menos un auxiliar bancario desde el PUC antes de conciliar.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankInaccessibleBusinesses(
                    @TenantId,@BankAccountId,@UserId,@ScopePermission))
                  THROW 51506,N'No tienes permiso de conciliación sobre todas las sedes que usan esta cuenta bancaria.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankReconciliations WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId AND BankAccountId=@BankAccountId
                    AND (FileSha256=@FileSha256 OR (PeriodFrom<=@PeriodTo AND PeriodTo>=@PeriodFrom)))
                  THROW 51502,N'El extracto ya fue importado o el periodo se cruza con otra conciliación.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankReconciliations
                  WHERE TenantId=@TenantId AND BankAccountId=@BankAccountId AND PeriodTo<@PeriodFrom AND Status<>N'Closed')
                  THROW 51502,N'Existe una conciliación anterior abierta. Ciérrala antes de importar el siguiente periodo.',1;
                DECLARE @PreviousClosing decimal(19,4)=(SELECT TOP(1) ClosingBalance
                  FROM accounting.BankReconciliations WHERE TenantId=@TenantId AND BankAccountId=@BankAccountId
                    AND PeriodTo<@PeriodFrom ORDER BY PeriodTo DESC,UpdatedAt DESC);
                IF @PreviousClosing IS NOT NULL AND ABS(@PreviousClosing-@OpeningBalance)>.0001
                  THROW 51502,N'El saldo inicial no continúa el saldo final de la conciliación anterior.',1;
                IF EXISTS(
                  SELECT 1 FROM accounting.BankReconciliations prior
                  CROSS APPLY(SELECT TOP(1) ev.OccurredAt FROM accounting.BankReconciliationStatusEvents ev
                    WHERE ev.ReconciliationId=prior.ReconciliationId AND ev.Status=N'Closed'
                    ORDER BY ev.OccurredAt DESC,ev.StatusEventId DESC) lastClose
                  JOIN accounting.BankAccounts priorBank ON priorBank.BankAccountId=prior.BankAccountId
                  JOIN dbo.AccountingEntries e ON e.TenantId=prior.TenantId
                    AND CAST(e.OccurredAt AS date)<=prior.PeriodTo AND e.PostedAt>lastClose.OccurredAt
                  JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId AND l.AccountId IN(
                    SELECT AccountId FROM accounting.BankBookAccounts(priorBank.TenantId,priorBank.BankAccountId))
                  WHERE prior.TenantId=@TenantId AND prior.BankAccountId=@BankAccountId AND prior.Status=N'Closed')
                  THROW 51502,N'Una conciliación anterior recibió movimientos contables retroactivos. Reábrela y revísala antes de continuar.',1;
                INSERT accounting.BankReconciliations
                (ReconciliationId,TenantId,BusinessId,BankAccountId,PeriodFrom,PeriodTo,
                 OpeningBalance,ClosingBalance,FileName,FileSha256,OriginalFile,Status,
                 CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
                VALUES(@ReconciliationId,@TenantId,@BusinessId,@BankAccountId,@PeriodFrom,@PeriodTo,
                 @OpeningBalance,@ClosingBalance,@FileName,@FileSha256,@OriginalFile,N'Preparation',
                 @UserId,@Now,@UserId,@Now);
                """, connection, transaction))
            {
                AddHeaderParameters(header, user, request, file, expectedHash, timeProvider.GetUtcNow());
                header.Parameters.AddWithValue("@ScopePermission", AccountingPermissionCodes.BankReconciliationManage);
                await header.ExecuteNonQueryAsync(cancellationToken);
            }
            var lines = request.Lines.Select(line => new
            {
                StatementLineId = Guid.NewGuid(), line.LineNumber,
                TransactionDate = line.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Description = line.Description.Trim(), Reference = line.Reference?.Trim(), line.Amount, line.Balance,
                Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
                    $"{line.LineNumber}|{line.TransactionDate:yyyy-MM-dd}|{line.Amount:0.0000}|{line.Reference?.Trim()}|{line.Description.Trim()}"))))
            }).ToArray();
            await using (var insert = new SqlCommand("""
                    INSERT accounting.BankStatementLines
                    (StatementLineId,ReconciliationId,LineNumber,TransactionDate,Description,Reference,Amount,Balance,Fingerprint)
                    SELECT StatementLineId,@ReconciliationId,LineNumber,TransactionDate,Description,Reference,Amount,Balance,
                      CONVERT(binary(32),Fingerprint,2)
                    FROM OPENJSON(@Lines) WITH(StatementLineId uniqueidentifier,LineNumber int,
                      TransactionDate date,Description nvarchar(1000),Reference nvarchar(200),
                      Amount decimal(19,4),Balance decimal(19,4),Fingerprint varchar(64));
                    IF @@ROWCOUNT<>@ExpectedCount THROW 51501,N'No se importaron todas las líneas del extracto.',1;
                    """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@ReconciliationId", request.ReconciliationId);
                insert.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
                insert.Parameters.AddWithValue("@ExpectedCount", lines.Length);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            var result = await ReadDetailAsync(connection, transaction, user, request.ReconciliationId, cancellationToken)
                ?? throw new AccountingForbiddenException("No tienes permiso de consulta sobre todas las sedes de la conciliación importada.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException exception) when (exception.Number is 51501 or 51502 or 51506 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (exception.Number == 51506)
                throw new AccountingForbiddenException(exception.Message);
            throw new AccountingConflictException(exception.Message);
        }
    }

    public Task<BankReconciliationDetailView> AllocateAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        CreateBankReconciliationAllocationRequest request, CancellationToken cancellationToken) =>
        MutateAsync(user, reconciliationId, request.RowVersion, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                DECLARE @StatementAmount decimal(19,4),@BookAmount decimal(19,4);
                SELECT @StatementAmount=s.Amount FROM accounting.BankStatementLines s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.StatementLineId=@StatementLineId AND s.ReconciliationId=@ReconciliationId;
                SELECT @BookAmount=l.Debit-l.Credit FROM dbo.AccountingEntryLines l WITH(UPDLOCK,HOLDLOCK)
                  JOIN dbo.AccountingEntries e ON e.EntryId=l.EntryId
                  JOIN accounting.BankReconciliations r ON r.ReconciliationId=@ReconciliationId
                WHERE l.EntryId=@EntryId AND l.LineNumber=@EntryLineNumber AND l.AccountId IN(
                    SELECT AccountId FROM accounting.BankBookAccounts(r.TenantId,r.BankAccountId))
                  AND e.TenantId=r.TenantId AND CAST(e.OccurredAt AS date)<=r.PeriodTo;
                IF @StatementAmount IS NULL OR @BookAmount IS NULL
                  THROW 51503,N'La línea bancaria o contable no pertenece a esta conciliación.',1;
                IF SIGN(@StatementAmount)<>SIGN(@BookAmount)
                  THROW 51504,N'Los movimientos que se cruzan deben tener el mismo sentido.',1;
                IF @Amount > ABS(@StatementAmount)-COALESCE((SELECT SUM(Amount) FROM accounting.BankReconciliationAllocations WHERE StatementLineId=@StatementLineId AND ReversedAt IS NULL),0)
                   OR @Amount > ABS(@BookAmount)-COALESCE((SELECT SUM(Amount) FROM accounting.BankReconciliationAllocations WHERE EntryId=@EntryId AND EntryLineNumber=@EntryLineNumber AND ReversedAt IS NULL),0)
                  THROW 51504,N'El valor supera el saldo disponible de una de las partidas.',1;
                INSERT accounting.BankReconciliationAllocations
                (MatchId,ReconciliationId,StatementLineId,EntryId,EntryLineNumber,Amount,CreatedByUserId,CreatedAt)
                VALUES(@MatchId,@ReconciliationId,@StatementLineId,@EntryId,@EntryLineNumber,@Amount,@UserId,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@MatchId", request.MatchId);
            command.Parameters.AddWithValue("@StatementLineId", request.StatementLineId);
            command.Parameters.AddWithValue("@EntryId", request.EntryId);
            command.Parameters.AddWithValue("@EntryLineNumber", request.EntryLineNumber);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@ReconciliationId", reconciliationId);
            AddMoney(command, "@Amount", request.Amount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken, AccountingPermissionCodes.BankReconciliationManage);

    public Task<BankReconciliationDetailView> ReverseAllocationAsync(
        AccountingUserIdentity user, Guid reconciliationId, Guid matchId,
        ReverseBankReconciliationAllocationRequest request, CancellationToken cancellationToken) =>
        MutateAsync(user, reconciliationId, request.RowVersion, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                UPDATE accounting.BankReconciliationAllocations SET ReversedByUserId=@UserId,
                  ReversedAt=@Now,ReversalReason=@Reason
                WHERE MatchId=@MatchId AND ReconciliationId=@ReconciliationId AND ReversedAt IS NULL;
                IF @@ROWCOUNT<>1 THROW 51503,N'El cruce no existe o ya fue reversado.',1;
                """, connection, transaction);
            command.Parameters.AddWithValue("@MatchId", matchId);
            command.Parameters.AddWithValue("@ReconciliationId", reconciliationId);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@Reason", request.Reason.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken, AccountingPermissionCodes.BankReconciliationManage);

    public Task<BankReconciliationDetailView> CloseAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken) =>
        MutateAsync(user, reconciliationId, request.RowVersion, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                DECLARE @BankAccountId uniqueidentifier,@From date,@To date,@Opening decimal(19,4),@Closing decimal(19,4),
                        @BookClosing decimal(19,4),@Cutoff datetimeoffset(7);
                SELECT @BankAccountId=b.BankAccountId,@From=r.PeriodFrom,@To=r.PeriodTo,
                       @Opening=r.OpeningBalance,@Closing=r.ClosingBalance
                FROM accounting.BankReconciliations r JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId
                WHERE r.ReconciliationId=@ReconciliationId;
                IF EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs job
                  JOIN dbo.AccountingSourceDocuments source
                    ON source.SourceDocumentId=job.SourceDocumentId AND source.SourceDocumentType=job.SourceDocumentType
                  WHERE job.TenantId=@TenantId AND job.AccountingEntryRequired=1
                    AND CAST(job.OccurredAt AS date)<=@To AND job.Status<>N'Posted'
                    AND (CHARINDEX(CONVERT(nvarchar(36),@BankAccountId),source.PayloadJson)>0
                      OR EXISTS(SELECT 1 FROM accounting.BankBookAccounts(@TenantId,@BankAccountId) account
                        WHERE CHARINDEX(CONVERT(nvarchar(36),account.AccountId),source.PayloadJson)>0)))
                  THROW 51505,N'Hay documentos contables pendientes que podrían afectar el corte.',1;
                IF ABS(@Opening+COALESCE((SELECT SUM(Amount) FROM accounting.BankStatementLines WHERE ReconciliationId=@ReconciliationId),0)-@Closing)>.0001
                  THROW 51505,N'El saldo inicial, los movimientos del extracto y el saldo final no cuadran.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankStatementLines s
                  OUTER APPLY(SELECT SUM(a.Amount) Allocated FROM accounting.BankReconciliationAllocations a WHERE a.StatementLineId=s.StatementLineId AND a.ReversedAt IS NULL) x
                  WHERE s.ReconciliationId=@ReconciliationId AND ABS(s.Amount)<>COALESCE(x.Allocated,0))
                  THROW 51505,N'Quedan movimientos del extracto sin conciliar.',1;
                IF EXISTS(SELECT 1 FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
                  OUTER APPLY(SELECT SUM(a.Amount) Allocated FROM accounting.BankReconciliationAllocations a WHERE a.EntryId=l.EntryId AND a.EntryLineNumber=l.LineNumber AND a.ReversedAt IS NULL) x
                  WHERE e.TenantId=@TenantId AND l.AccountId IN(SELECT AccountId FROM accounting.BankBookAccounts(@TenantId,@BankAccountId))
                    AND CAST(e.OccurredAt AS date)<=@To AND ABS(l.Debit-l.Credit)<>COALESCE(x.Allocated,0))
                  THROW 51505,N'Quedan movimientos contables de la cuenta bancaria sin conciliar.',1;
                SET @BookClosing=COALESCE((SELECT SUM(l.Debit-l.Credit)
                  FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
                  WHERE e.TenantId=@TenantId AND l.AccountId IN(SELECT AccountId FROM accounting.BankBookAccounts(@TenantId,@BankAccountId)) AND CAST(e.OccurredAt AS date)<=@To),0);
                IF ABS(@BookClosing-@Closing)>.0001 THROW 51505,N'El saldo del banco y el saldo contable no cuadran.',1;
                SET @Cutoff=(SELECT MAX(e.PostedAt) FROM dbo.AccountingEntries e
                  JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
                  WHERE e.TenantId=@TenantId AND l.AccountId IN(SELECT AccountId FROM accounting.BankBookAccounts(@TenantId,@BankAccountId)) AND CAST(e.OccurredAt AS date)<=@To);
                UPDATE accounting.BankReconciliations SET Status=N'Closed',ClosedByUserId=@UserId,ClosedAt=@Now,
                  UpdatedByUserId=@UserId,UpdatedAt=@Now WHERE ReconciliationId=@ReconciliationId;
                INSERT accounting.BankReconciliationStatusEvents
                  (StatusEventId,ReconciliationId,Status,ActorUserId,OccurredAt,StatementClosingBalance,BookClosingBalance,AccountingCutoffPostedAt)
                VALUES(NEWID(),@ReconciliationId,N'Closed',@UserId,@Now,@Closing,@BookClosing,@Cutoff);
                """, connection, transaction);
            AddMutationParameters(command, user, reconciliationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken, AccountingPermissionCodes.BankReconciliationClose);

    public Task<BankReconciliationDetailView> ReopenAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        ChangeBankReconciliationStatusRequest request, CancellationToken cancellationToken) =>
        MutateAsync(user, reconciliationId, request.RowVersion, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM accounting.BankReconciliations WHERE ReconciliationId=@ReconciliationId AND Status=N'Closed')
                  THROW 51503,N'Solo una conciliación cerrada puede reabrirse.',1;
                UPDATE accounting.BankReconciliations SET Status=N'Reopened',ReopenedByUserId=@UserId,
                  ReopenedAt=@Now,ReopenReason=@Reason,UpdatedByUserId=@UserId,UpdatedAt=@Now
                WHERE ReconciliationId=@ReconciliationId;
                INSERT accounting.BankReconciliationStatusEvents
                  (StatusEventId,ReconciliationId,Status,Reason,ActorUserId,OccurredAt)
                VALUES(NEWID(),@ReconciliationId,N'Reopened',@Reason,@UserId,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@ReconciliationId", reconciliationId);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@Reason", request.Reason!.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken, AccountingPermissionCodes.BankReconciliationClose, allowClosed: true);

    private async Task<BankReconciliationDetailView> MutateAsync(
        AccountingUserIdentity user, Guid reconciliationId, string rowVersion,
        Func<SqlConnection, SqlTransaction, Task> mutation, CancellationToken cancellationToken,
        string scopePermission, bool allowClosed = false)
    {
        byte[] version;
        try { version = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new AccountingConflictException("Actualiza la conciliación antes de continuar."); }
        await using var connection = connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var scope = new SqlCommand($"""
                IF NOT EXISTS(SELECT 1 FROM accounting.BankReconciliations WITH(UPDLOCK,HOLDLOCK)
                  WHERE ReconciliationId=@ReconciliationId AND TenantId=@TenantId
                    AND RowVersion=@RowVersion {(allowClosed ? "" : "AND Status IN(N'Preparation',N'Reopened')")})
                  THROW 51500,N'La conciliación cambió, no pertenece a la sede o no admite cambios.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankReconciliations reconciliation
                  CROSS APPLY accounting.BankInaccessibleBusinesses(
                    @TenantId,reconciliation.BankAccountId,@UserId,@ScopePermission) denied
                  WHERE reconciliation.ReconciliationId=@ReconciliationId AND reconciliation.TenantId=@TenantId)
                  THROW 51506,N'No tienes permiso de conciliación sobre todas las sedes que usan esta cuenta bancaria.',1;
                """, connection, transaction))
            {
                scope.Parameters.AddWithValue("@ReconciliationId", reconciliationId);
                scope.Parameters.AddWithValue("@TenantId", user.TenantId);
                scope.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                scope.Parameters.Add("@RowVersion", SqlDbType.Timestamp, 8).Value = version;
                scope.Parameters.AddWithValue("@UserId", user.UserId);
                scope.Parameters.AddWithValue("@ScopePermission", scopePermission);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }
            await mutation(connection, transaction);
            await using (var touch = new SqlCommand("""
                UPDATE accounting.BankReconciliations SET UpdatedByUserId=@UserId,UpdatedAt=@Now
                WHERE ReconciliationId=@ReconciliationId;
                """, connection, transaction))
            { AddMutationParameters(touch, user, reconciliationId); await touch.ExecuteNonQueryAsync(cancellationToken); }
            var result = await ReadDetailAsync(connection, transaction, user, reconciliationId, cancellationToken)
                ?? throw new AccountingForbiddenException("No tienes permiso de consulta sobre todas las sedes de esta conciliación.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException exception) when (exception.Number is >= 51500 and <= 51506 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw exception.Number == 51500 ? new AccountingConflictException(exception.Message)
                : exception.Number == 51506 ? new AccountingForbiddenException(exception.Message)
                : new AccountingValidationException(exception.Message);
        }
    }

    private async Task<BankReconciliationDetailView?> ReadDetailAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        Guid reconciliationId, CancellationToken cancellationToken, BankReconciliationDetailQuery? query = null)
    {
        query ??= new();
        var summaries = await ReadSummariesAsync(connection, transaction, user, cancellationToken, reconciliationId,
            new(PageSize: 1));
        var summary = summaries.Items.SingleOrDefault();
        if (summary is null) return null;
        var statement = new List<BankStatementLineView>();
        int statementCount;
        await using (var command = new SqlCommand("""
            SELECT COUNT(*) FROM accounting.BankStatementLines
            WHERE ReconciliationId=@Id AND (@StatementSearch IS NULL OR Description LIKE @StatementSearch ESCAPE N'~'
              OR Reference LIKE @StatementSearch ESCAPE N'~');
            WITH SelectedLines AS(
              SELECT StatementLineId FROM accounting.BankStatementLines
              WHERE ReconciliationId=@Id AND (@StatementSearch IS NULL OR Description LIKE @StatementSearch ESCAPE N'~'
                OR Reference LIKE @StatementSearch ESCAPE N'~')
              ORDER BY LineNumber,StatementLineId OFFSET @StatementOffset ROWS FETCH NEXT @PageSize ROWS ONLY
            )
            SELECT s.StatementLineId,s.LineNumber,s.TransactionDate,s.Description,s.Reference,s.Amount,s.Balance,
                   COALESCE(a.Allocated,0)
            FROM SelectedLines selected JOIN accounting.BankStatementLines s ON s.StatementLineId=selected.StatementLineId
            OUTER APPLY(SELECT SUM(Amount) Allocated FROM accounting.BankReconciliationAllocations
              WHERE StatementLineId=s.StatementLineId AND ReversedAt IS NULL) a
            ORDER BY s.LineNumber,s.StatementLineId;
            """, connection, transaction))
        {
            AddDetailParameters(command, reconciliationId, query);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            statementCount = reader.GetInt32(0);
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var allocated = reader.GetDecimal(7);
                statement.Add(new(reader.GetGuid(0),reader.GetInt32(1),DateOnly.FromDateTime(reader.GetDateTime(2)),
                    reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetDecimal(5),
                    reader.IsDBNull(6)?null:reader.GetDecimal(6),allocated,allocated==Math.Abs(reader.GetDecimal(5))));
            }
        }
        var books = new List<BankBookLineView>();
        int bookCount, unmatchedBookCount;
        await using (var command = new SqlCommand("""
            -- Only keys and reconciliation amounts are materialized, scoped to this bank and cutoff.
            -- Descriptive details are joined after the page is selected.
            SELECT e.EntryId,l.LineNumber,e.OccurredAt,e.EntryNumber,l.Debit-l.Credit Amount,
              COALESCE(a.Allocated,0) Allocated
            INTO #BankBookKeys
            FROM accounting.BankReconciliations r
            JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId AND b.TenantId=r.TenantId
            JOIN dbo.AccountingEntryLines l ON l.AccountId IN(
              SELECT AccountId FROM accounting.BankBookAccounts(b.TenantId,b.BankAccountId))
            JOIN dbo.AccountingEntries e ON e.EntryId=l.EntryId AND e.TenantId=r.TenantId
              AND CAST(e.OccurredAt AS date)<=r.PeriodTo
            OUTER APPLY(SELECT SUM(Amount) Allocated,
                SUM(CASE WHEN ReconciliationId=@Id THEN 1 ELSE 0 END) InCurrent
              FROM accounting.BankReconciliationAllocations
              WHERE EntryId=l.EntryId AND EntryLineNumber=l.LineNumber AND ReversedAt IS NULL) a
            WHERE r.ReconciliationId=@Id AND
              (ABS(l.Debit-l.Credit)>COALESCE(a.Allocated,0) OR a.InCurrent>0);
            SELECT COUNT(*) FROM #BankBookKeys WHERE ABS(Amount)>Allocated;
            IF @BookSearch IS NOT NULL
              DELETE k FROM #BankBookKeys k WHERE NOT EXISTS(
                SELECT 1 FROM dbo.AccountingEntries e
                JOIN dbo.Businesses business ON business.BusinessId=e.BusinessId AND business.TenantId=e.TenantId
                JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId AND l.LineNumber=k.LineNumber
                WHERE e.EntryId=k.EntryId AND (e.EntryNumber LIKE @BookSearch ESCAPE N'~'
                  OR business.Name LIKE @BookSearch ESCAPE N'~' OR l.Description LIKE @BookSearch ESCAPE N'~'));
            SELECT COUNT(*) FROM #BankBookKeys;
            WITH SelectedKeys AS(
              SELECT EntryId,LineNumber,Amount,Allocated FROM #BankBookKeys
              ORDER BY OccurredAt,EntryNumber,EntryId,LineNumber
              OFFSET @BookOffset ROWS FETCH NEXT @PageSize ROWS ONLY
            )
            SELECT e.EntryId,l.LineNumber,e.EntryNumber,e.OccurredAt,e.BusinessId,business.Name,
              e.SourceDocumentType,e.SourceDocumentId,l.Description,k.Amount,k.Allocated
            FROM SelectedKeys k JOIN dbo.AccountingEntries e ON e.EntryId=k.EntryId
            JOIN dbo.Businesses business ON business.BusinessId=e.BusinessId AND business.TenantId=e.TenantId
            JOIN dbo.AccountingEntryLines l ON l.EntryId=k.EntryId AND l.LineNumber=k.LineNumber
            ORDER BY e.OccurredAt,e.EntryNumber,e.EntryId,l.LineNumber;
            """, connection, transaction))
        {
            AddDetailParameters(command, reconciliationId, query);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            unmatchedBookCount = reader.GetInt32(0);
            await reader.NextResultAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            bookCount = reader.GetInt32(0);
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var amount = reader.GetDecimal(9);
                var allocated = reader.GetDecimal(10);
                books.Add(new(reader.GetGuid(0),reader.GetInt32(1),reader.GetString(2),reader.GetDateTimeOffset(3),
                    reader.GetGuid(4),reader.GetString(5),reader.GetString(6),reader.GetGuid(7),reader.GetString(8),
                    amount,allocated,Math.Max(0,Math.Abs(amount)-allocated)));
            }
        }
        var allocations = new List<BankReconciliationAllocationView>();
        int allocationCount;
        await using (var command = new SqlCommand("""
            SELECT COUNT(*) FROM accounting.BankReconciliationAllocations WHERE ReconciliationId=@Id AND ReversedAt IS NULL;
            SELECT MatchId,StatementLineId,EntryId,EntryLineNumber,Amount,CreatedAt
            FROM accounting.BankReconciliationAllocations WHERE ReconciliationId=@Id AND ReversedAt IS NULL
            ORDER BY CreatedAt,MatchId OFFSET @AllocationOffset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection, transaction))
        {
            AddDetailParameters(command, reconciliationId, query);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            allocationCount = reader.GetInt32(0);
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                allocations.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetDateTimeOffset(5)));
        }
        var events = new List<BankReconciliationStatusEventView>();
        int eventCount;
        await using (var command = new SqlCommand("""
            SELECT COUNT(*) FROM accounting.BankReconciliationStatusEvents WHERE ReconciliationId=@Id;
            WITH SelectedEvents AS(
              SELECT StatusEventId FROM accounting.BankReconciliationStatusEvents WHERE ReconciliationId=@Id
              ORDER BY OccurredAt,StatusEventId OFFSET @EventOffset ROWS FETCH NEXT @PageSize ROWS ONLY
            )
            SELECT ev.StatusEventId,ev.Status,ev.Reason,ev.ActorUserId,
              CONCAT(u.FirstName,N' ',u.LastName),ev.OccurredAt,ev.StatementClosingBalance,
              ev.BookClosingBalance,ev.AccountingCutoffPostedAt
            FROM SelectedEvents selected
            JOIN accounting.BankReconciliationStatusEvents ev ON ev.StatusEventId=selected.StatusEventId
            JOIN dbo.AppUsers u ON u.UserId=ev.ActorUserId
            ORDER BY ev.OccurredAt,ev.StatusEventId;
            """, connection, transaction))
        {
            AddDetailParameters(command, reconciliationId, query);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            eventCount = reader.GetInt32(0);
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                events.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetGuid(3),
                    reader.GetString(4),reader.GetDateTimeOffset(5),reader.IsDBNull(6)?null:reader.GetDecimal(6),
                    reader.IsDBNull(7)?null:reader.GetDecimal(7),reader.IsDBNull(8)?null:reader.GetDateTimeOffset(8)));
        }
        return new(summary,statement,books,allocations,events,
            new(query,statementCount,bookCount,allocationCount,eventCount,unmatchedBookCount));
    }

    private static void AddDetailParameters(SqlCommand command, Guid reconciliationId, BankReconciliationDetailQuery query)
    {
        command.Parameters.AddWithValue("@Id", reconciliationId);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        command.Parameters.AddWithValue("@StatementOffset", (query.StatementPage - 1) * query.PageSize);
        command.Parameters.AddWithValue("@BookOffset", (query.BookPage - 1) * query.PageSize);
        command.Parameters.AddWithValue("@AllocationOffset", (query.AllocationPage - 1) * query.PageSize);
        command.Parameters.AddWithValue("@EventOffset", (query.EventPage - 1) * query.PageSize);
        command.Parameters.Add("@StatementSearch", SqlDbType.NVarChar, 325).Value = SearchPattern(query.StatementSearch);
        command.Parameters.Add("@BookSearch", SqlDbType.NVarChar, 325).Value = SearchPattern(query.BookSearch);
    }

    private static async Task<BankReconciliationPage> ReadSummariesAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        CancellationToken cancellationToken, Guid? reconciliationId = null, BankReconciliationQuery? query = null)
    {
        query ??= new();
        var sortColumn = query.SortBy switch
        {
            "periodTo" => "r.PeriodTo",
            "updatedAt" => "r.UpdatedAt",
            "bankDisplayName" => "b.DisplayName",
            _ => throw new AccountingValidationException("El orden de conciliación no es válido.")
        };
        var direction = query.SortDirection == "asc" ? "ASC" : "DESC";
        const string scope = """
            FROM accounting.BankReconciliations r
            JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId AND b.TenantId=r.TenantId
            WHERE r.TenantId=@TenantId AND (@Id IS NULL OR r.ReconciliationId=@Id)
              AND (@BankAccountId IS NULL OR r.BankAccountId=@BankAccountId)
              AND (@Search IS NULL OR r.FileName LIKE @Search ESCAPE N'~' OR b.DisplayName LIKE @Search ESCAPE N'~')
              AND EXISTS(SELECT 1 FROM #ReadableBanks visible WHERE visible.BankAccountId=r.BankAccountId)
            """;
        await using var command = new SqlCommand($"""
            SELECT DISTINCT r.BankAccountId INTO #ReadableBanks
            FROM accounting.BankReconciliations r JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId AND b.TenantId=r.TenantId
            WHERE r.TenantId=@TenantId AND (@Id IS NULL OR r.ReconciliationId=@Id)
              AND (@BankAccountId IS NULL OR r.BankAccountId=@BankAccountId)
              AND (@Search IS NULL OR r.FileName LIKE @Search ESCAPE N'~' OR b.DisplayName LIKE @Search ESCAPE N'~');
            DELETE visible FROM #ReadableBanks visible
            WHERE EXISTS(SELECT 1 FROM accounting.BankInaccessibleBusinesses(
              @TenantId,visible.BankAccountId,@UserId,@ReadPermission));
            SELECT COUNT(*) {scope};
            WITH SelectedHeaders AS(
              SELECT r.ReconciliationId {scope}
              ORDER BY {sortColumn} {direction},r.ReconciliationId
              OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            )
            SELECT r.ReconciliationId,r.BankAccountId,b.DisplayName,a.Code,a.Name,r.PeriodFrom,r.PeriodTo,
              r.OpeningBalance,r.ClosingBalance,r.FileName,CONVERT(varchar(64),r.FileSha256,2),r.Status,
              COUNT(DISTINCT s.StatementLineId),COUNT(DISTINCT CASE WHEN ABS(s.Amount)=COALESCE(x.Allocated,0) THEN s.StatementLineId END),
              COALESCE(SUM(s.Amount),0),
              COALESCE((SELECT SUM(l.Debit-l.Credit) FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId WHERE e.TenantId=r.TenantId AND l.AccountId IN(SELECT AccountId FROM accounting.BankBookAccounts(b.TenantId,b.BankAccountId)) AND CAST(e.OccurredAt AS date) BETWEEN r.PeriodFrom AND r.PeriodTo),0),
              r.ClosingBalance-COALESCE((SELECT SUM(l.Debit-l.Credit) FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId WHERE e.TenantId=r.TenantId AND l.AccountId IN(SELECT AccountId FROM accounting.BankBookAccounts(b.TenantId,b.BankAccountId)) AND CAST(e.OccurredAt AS date)<=r.PeriodTo),0),
              CONVERT(bit,CASE WHEN r.Status=N'Closed' AND EXISTS(
                SELECT 1 FROM dbo.AccountingEntries changed
                JOIN dbo.AccountingEntryLines changedLine ON changedLine.EntryId=changed.EntryId AND changedLine.AccountId IN(SELECT AccountId FROM accounting.BankBookAccounts(b.TenantId,b.BankAccountId))
                WHERE changed.TenantId=r.TenantId AND CAST(changed.OccurredAt AS date)<=r.PeriodTo
                  AND changed.PostedAt>(SELECT MAX(ev.OccurredAt) FROM accounting.BankReconciliationStatusEvents ev WHERE ev.ReconciliationId=r.ReconciliationId AND ev.Status=N'Closed')) THEN 1 ELSE 0 END),
              r.RowVersion,r.UpdatedAt
            FROM SelectedHeaders selected JOIN accounting.BankReconciliations r ON r.ReconciliationId=selected.ReconciliationId
            JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId
            OUTER APPLY(SELECT CASE WHEN COUNT(*)=1 THEN MAX(account.Code) END Code,
              CASE WHEN COUNT(*)=1 THEN MAX(account.Name) END Name
              FROM accounting.BankBookAccounts(b.TenantId,b.BankAccountId) linked
              JOIN dbo.AccountingAccounts account ON account.AccountId=linked.AccountId) a
            LEFT JOIN accounting.BankStatementLines s ON s.ReconciliationId=r.ReconciliationId
            OUTER APPLY(SELECT SUM(ra.Amount) Allocated FROM accounting.BankReconciliationAllocations ra WHERE ra.StatementLineId=s.StatementLineId AND ra.ReversedAt IS NULL)x
            GROUP BY r.ReconciliationId,r.BankAccountId,b.BankAccountId,b.TenantId,b.DisplayName,a.Code,a.Name,r.PeriodFrom,r.PeriodTo,r.OpeningBalance,r.ClosingBalance,r.FileName,r.FileSha256,r.Status,r.RowVersion,r.UpdatedAt,r.TenantId,r.BusinessId
            ORDER BY {sortColumn} {direction},r.ReconciliationId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@ReadPermission", AccountingPermissionCodes.BankReconciliationRead);
        command.Parameters.AddWithValue("@Id", (object?)reconciliationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@BankAccountId", (object?)query.BankAccountId ?? DBNull.Value);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 325).Value = SearchPattern(query.Search);
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var result = new List<BankReconciliationSummaryView>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),DateOnly.FromDateTime(reader.GetDateTime(5)),DateOnly.FromDateTime(reader.GetDateTime(6)),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetInt32(12),reader.GetInt32(13),reader.GetDecimal(14),reader.GetDecimal(15),reader.GetDecimal(16),reader.GetBoolean(17),Convert.ToBase64String((byte[])reader[18]),reader.GetDateTimeOffset(19)));
        return new(result, query.Page, query.PageSize, count);
    }

    private static object SearchPattern(string? search) => string.IsNullOrWhiteSpace(search)
        ? DBNull.Value
        : "%" + search.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    private static void AddHeaderParameters(SqlCommand command, AccountingUserIdentity user,
        ImportBankReconciliationRequest request, byte[] file, byte[] hash, DateTimeOffset now)
    { command.Parameters.AddWithValue("@ReconciliationId",request.ReconciliationId);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@BankAccountId",request.BankAccountId);command.Parameters.AddWithValue("@PeriodFrom",request.PeriodFrom.ToDateTime(TimeOnly.MinValue));command.Parameters.AddWithValue("@PeriodTo",request.PeriodTo.ToDateTime(TimeOnly.MinValue));AddMoney(command,"@OpeningBalance",request.OpeningBalance);AddMoney(command,"@ClosingBalance",request.ClosingBalance);command.Parameters.AddWithValue("@FileName",request.FileName.Trim());command.Parameters.Add("@FileSha256",SqlDbType.Binary,32).Value=hash;command.Parameters.Add("@OriginalFile",SqlDbType.VarBinary,-1).Value=file;command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",now); }
    private void AddMutationParameters(SqlCommand command, AccountingUserIdentity user, Guid id)
    {command.Parameters.AddWithValue("@ReconciliationId",id);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());}
    private static void AddMoney(SqlCommand command,string name,decimal value){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=4;p.Value=value;}

    private static async Task<bool> HasCompleteBusinessScopeAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, Guid? reconciliationId, string permission, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT DISTINCT BankAccountId INTO #ScopeBanks
            FROM accounting.BankReconciliations
            WHERE TenantId=@TenantId AND (@ReconciliationId IS NULL OR ReconciliationId=@ReconciliationId);
            SELECT CONVERT(bit,CASE WHEN EXISTS(
              SELECT 1 FROM #ScopeBanks bank
              CROSS APPLY accounting.BankInaccessibleBusinesses(@TenantId,bank.BankAccountId,@UserId,@Permission) denied
            ) THEN 0 ELSE 1 END);
            """, connection, transaction);
        command.Parameters.AddWithValue("@ReconciliationId", (object?)reconciliationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Permission", permission);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
