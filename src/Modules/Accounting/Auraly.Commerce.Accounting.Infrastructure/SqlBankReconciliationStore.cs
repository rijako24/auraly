using System.Data;
using System.Security.Cryptography;
using System.Text;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlBankReconciliationStore(
    AccountingSqlConnectionFactory connections,
    TimeProvider timeProvider) : IBankReconciliationStore
{
    public async Task<IReadOnlyList<BankReconciliationSummaryView>> ListAsync(
        AccountingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        if (!await HasCompleteBusinessScopeAsync(connection, user, null,
                AccountingPermissionCodes.BankReconciliationRead, cancellationToken))
            throw new AccountingForbiddenException("No tienes permiso de conciliación sobre todas las sedes que usan las cuentas bancarias conciliadas.");
        return await ReadSummariesAsync(connection, null, user, cancellationToken);
    }

    public async Task<BankReconciliationDetailView?> GetAsync(
        AccountingUserIdentity user, Guid reconciliationId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        if (!await HasCompleteBusinessScopeAsync(connection, user, reconciliationId,
                AccountingPermissionCodes.BankReconciliationRead, cancellationToken))
            throw new AccountingForbiddenException("No tienes permiso de conciliación sobre todas las sedes que usan esta cuenta bancaria.");
        return await ReadDetailAsync(connection, null, user, reconciliationId, cancellationToken);
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
                IF EXISTS(SELECT 1 FROM accounting.BankAccounts currentBank
                  JOIN accounting.BankAccounts otherBank ON otherBank.TenantId=currentBank.TenantId
                    AND otherBank.AccountingAccountId=currentBank.AccountingAccountId
                    AND otherBank.BankAccountId<>currentBank.BankAccountId
                  WHERE currentBank.BankAccountId=@BankAccountId)
                  THROW 51501,N'El auxiliar PUC está compartido por varias cuentas bancarias. Asigna un auxiliar exclusivo antes de conciliar.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankAccounts currentBank
                  JOIN dbo.AccountingEntryLines bankLine ON bankLine.AccountId=currentBank.AccountingAccountId
                  JOIN dbo.AccountingEntries bankEntry ON bankEntry.EntryId=bankLine.EntryId AND bankEntry.TenantId=currentBank.TenantId
                  WHERE currentBank.BankAccountId=@BankAccountId AND NOT EXISTS(
                    SELECT 1 FROM dbo.UserRoles ur JOIN dbo.AppRoles role ON role.RoleId=ur.RoleId AND role.IsActive=1
                    JOIN dbo.RolePermissions rp ON rp.RoleId=role.RoleId JOIN dbo.Permissions permission ON permission.PermissionId=rp.PermissionId
                    WHERE ur.UserId=@UserId AND (ur.BusinessId IS NULL OR ur.BusinessId=bankEntry.BusinessId)
                      AND (role.TenantId IS NULL OR role.TenantId=@TenantId) AND permission.Resource=@ScopePermission))
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
                  JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId AND l.AccountId=priorBank.AccountingAccountId
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
            foreach (var line in request.Lines.OrderBy(value => value.LineNumber))
            {
                var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{line.LineNumber}|{line.TransactionDate:yyyy-MM-dd}|{line.Amount:0.0000}|{line.Reference?.Trim()}|{line.Description.Trim()}"));
                await using var insert = new SqlCommand("""
                    INSERT accounting.BankStatementLines
                    (StatementLineId,ReconciliationId,LineNumber,TransactionDate,Description,Reference,Amount,Balance,Fingerprint)
                    VALUES(@StatementLineId,@ReconciliationId,@LineNumber,@TransactionDate,@Description,@Reference,@Amount,@Balance,@Fingerprint);
                    """, connection, transaction);
                insert.Parameters.AddWithValue("@StatementLineId", Guid.NewGuid());
                insert.Parameters.AddWithValue("@ReconciliationId", request.ReconciliationId);
                insert.Parameters.AddWithValue("@LineNumber", line.LineNumber);
                insert.Parameters.AddWithValue("@TransactionDate", line.TransactionDate.ToDateTime(TimeOnly.MinValue));
                insert.Parameters.AddWithValue("@Description", line.Description.Trim());
                insert.Parameters.AddWithValue("@Reference", (object?)line.Reference?.Trim() ?? DBNull.Value);
                AddMoney(insert, "@Amount", line.Amount);
                AddNullableMoney(insert, "@Balance", line.Balance);
                insert.Parameters.Add("@Fingerprint", SqlDbType.Binary, 32).Value = fingerprint;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 51501 or 51502 or 51506 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (exception.Number == 51506)
                throw new AccountingForbiddenException(exception.Message);
            throw new AccountingConflictException(exception.Message);
        }
        var result = await GetAsync(user, request.ReconciliationId, cancellationToken);
        return result ?? throw new AccountingConflictException("No fue posible recuperar la conciliación importada.");
    }

    public Task<BankReconciliationDetailView> AllocateAsync(
        AccountingUserIdentity user, Guid reconciliationId,
        CreateBankReconciliationAllocationRequest request, CancellationToken cancellationToken) =>
        MutateAsync(user, reconciliationId, request.RowVersion, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                DECLARE @StatementAmount decimal(19,4),@BookAmount decimal(19,4),@AccountId uniqueidentifier;
                SELECT @StatementAmount=s.Amount FROM accounting.BankStatementLines s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.StatementLineId=@StatementLineId AND s.ReconciliationId=@ReconciliationId;
                SELECT @AccountId=b.AccountingAccountId FROM accounting.BankReconciliations r
                  JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId
                WHERE r.ReconciliationId=@ReconciliationId;
                SELECT @BookAmount=l.Debit-l.Credit FROM dbo.AccountingEntryLines l WITH(UPDLOCK,HOLDLOCK)
                  JOIN dbo.AccountingEntries e ON e.EntryId=l.EntryId
                  JOIN accounting.BankReconciliations r ON r.ReconciliationId=@ReconciliationId
                WHERE l.EntryId=@EntryId AND l.LineNumber=@EntryLineNumber AND l.AccountId=@AccountId
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
                DECLARE @BankAccountId uniqueidentifier,@AccountId uniqueidentifier,@From date,@To date,@Opening decimal(19,4),@Closing decimal(19,4),
                        @BookClosing decimal(19,4),@Cutoff datetimeoffset(7);
                SELECT @BankAccountId=b.BankAccountId,@AccountId=b.AccountingAccountId,@From=r.PeriodFrom,@To=r.PeriodTo,
                       @Opening=r.OpeningBalance,@Closing=r.ClosingBalance
                FROM accounting.BankReconciliations r JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId
                WHERE r.ReconciliationId=@ReconciliationId;
                IF EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs job
                  JOIN dbo.AccountingSourceDocuments source
                    ON source.SourceDocumentId=job.SourceDocumentId AND source.SourceDocumentType=job.SourceDocumentType
                  WHERE job.TenantId=@TenantId AND job.AccountingEntryRequired=1
                    AND CAST(job.OccurredAt AS date)<=@To AND job.Status<>N'Posted'
                    AND (CHARINDEX(CONVERT(nvarchar(36),@BankAccountId),source.PayloadJson)>0
                      OR CHARINDEX(CONVERT(nvarchar(36),@AccountId),source.PayloadJson)>0))
                  THROW 51505,N'Hay documentos contables pendientes que podrían afectar el corte.',1;
                IF ABS(@Opening+COALESCE((SELECT SUM(Amount) FROM accounting.BankStatementLines WHERE ReconciliationId=@ReconciliationId),0)-@Closing)>.0001
                  THROW 51505,N'El saldo inicial, los movimientos del extracto y el saldo final no cuadran.',1;
                IF EXISTS(SELECT 1 FROM accounting.BankStatementLines s
                  OUTER APPLY(SELECT SUM(a.Amount) Allocated FROM accounting.BankReconciliationAllocations a WHERE a.StatementLineId=s.StatementLineId AND a.ReversedAt IS NULL) x
                  WHERE s.ReconciliationId=@ReconciliationId AND ABS(s.Amount)<>COALESCE(x.Allocated,0))
                  THROW 51505,N'Quedan movimientos del extracto sin conciliar.',1;
                IF EXISTS(SELECT 1 FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
                  OUTER APPLY(SELECT SUM(a.Amount) Allocated FROM accounting.BankReconciliationAllocations a WHERE a.EntryId=l.EntryId AND a.EntryLineNumber=l.LineNumber AND a.ReversedAt IS NULL) x
                  WHERE e.TenantId=@TenantId AND l.AccountId=@AccountId
                    AND CAST(e.OccurredAt AS date)<=@To AND ABS(l.Debit-l.Credit)<>COALESCE(x.Allocated,0))
                  THROW 51505,N'Quedan movimientos contables de la cuenta bancaria sin conciliar.',1;
                SET @BookClosing=COALESCE((SELECT SUM(l.Debit-l.Credit)
                  FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
                  WHERE e.TenantId=@TenantId AND l.AccountId=@AccountId AND CAST(e.OccurredAt AS date)<=@To),0);
                IF ABS(@BookClosing-@Closing)>.0001 THROW 51505,N'El saldo del banco y el saldo contable no cuadran.',1;
                SET @Cutoff=(SELECT MAX(e.PostedAt) FROM dbo.AccountingEntries e
                  JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
                  WHERE e.TenantId=@TenantId AND l.AccountId=@AccountId AND CAST(e.OccurredAt AS date)<=@To);
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
                  JOIN accounting.BankAccounts bank ON bank.BankAccountId=reconciliation.BankAccountId
                  JOIN dbo.AccountingEntryLines bankLine ON bankLine.AccountId=bank.AccountingAccountId
                  JOIN dbo.AccountingEntries bankEntry ON bankEntry.EntryId=bankLine.EntryId AND bankEntry.TenantId=reconciliation.TenantId
                  WHERE reconciliation.ReconciliationId=@ReconciliationId AND NOT EXISTS(
                    SELECT 1 FROM dbo.UserRoles ur JOIN dbo.AppRoles role ON role.RoleId=ur.RoleId AND role.IsActive=1
                    JOIN dbo.RolePermissions rp ON rp.RoleId=role.RoleId JOIN dbo.Permissions permission ON permission.PermissionId=rp.PermissionId
                    WHERE ur.UserId=@UserId AND (ur.BusinessId IS NULL OR ur.BusinessId=bankEntry.BusinessId)
                      AND (role.TenantId IS NULL OR role.TenantId=@TenantId) AND permission.Resource=@ScopePermission))
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
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is >= 51500 and <= 51506 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw exception.Number == 51500 ? new AccountingConflictException(exception.Message)
                : exception.Number == 51506 ? new AccountingForbiddenException(exception.Message)
                : new AccountingValidationException(exception.Message);
        }
        return await GetAsync(user, reconciliationId, cancellationToken)
            ?? throw new AccountingConflictException("La conciliación ya no está disponible.");
    }

    private async Task<BankReconciliationDetailView?> ReadDetailAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        Guid reconciliationId, CancellationToken cancellationToken)
    {
        var summaries = await ReadSummariesAsync(connection, transaction, user, cancellationToken, reconciliationId);
        var summary = summaries.SingleOrDefault(); if (summary is null) return null;
        var statement = new List<BankStatementLineView>();
        await using (var command = new SqlCommand("""
            SELECT s.StatementLineId,s.LineNumber,s.TransactionDate,s.Description,s.Reference,s.Amount,s.Balance,
                   COALESCE(SUM(CASE WHEN a.ReversedAt IS NULL THEN a.Amount ELSE 0 END),0) Allocated
            FROM accounting.BankStatementLines s LEFT JOIN accounting.BankReconciliationAllocations a ON a.StatementLineId=s.StatementLineId
            WHERE s.ReconciliationId=@Id GROUP BY s.StatementLineId,s.LineNumber,s.TransactionDate,s.Description,s.Reference,s.Amount,s.Balance
            ORDER BY s.LineNumber;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", reconciliationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            { var allocated=reader.GetDecimal(7); statement.Add(new(reader.GetGuid(0),reader.GetInt32(1),DateOnly.FromDateTime(reader.GetDateTime(2)),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetDecimal(5),reader.IsDBNull(6)?null:reader.GetDecimal(6),allocated,allocated==Math.Abs(reader.GetDecimal(5)))); }
        }
        var books = new List<BankBookLineView>();
        await using (var command = new SqlCommand("""
            SELECT e.EntryId,l.LineNumber,e.EntryNumber,e.OccurredAt,e.BusinessId,business.Name,e.SourceDocumentType,e.SourceDocumentId,l.Description,l.Debit-l.Credit,
                   COALESCE(SUM(CASE WHEN a.ReversedAt IS NULL THEN a.Amount ELSE 0 END),0)
            FROM accounting.BankReconciliations r JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId
            JOIN dbo.AccountingEntries e ON e.TenantId=r.TenantId AND CAST(e.OccurredAt AS date)<=r.PeriodTo
            JOIN dbo.Businesses business ON business.BusinessId=e.BusinessId AND business.TenantId=e.TenantId
            JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId AND l.AccountId=b.AccountingAccountId
            LEFT JOIN accounting.BankReconciliationAllocations a ON a.EntryId=l.EntryId AND a.EntryLineNumber=l.LineNumber
            WHERE r.ReconciliationId=@Id
            GROUP BY e.EntryId,l.LineNumber,e.EntryNumber,e.OccurredAt,e.BusinessId,business.Name,e.SourceDocumentType,e.SourceDocumentId,l.Description,l.Debit,l.Credit
            HAVING ABS(l.Debit-l.Credit)>COALESCE(SUM(CASE WHEN a.ReversedAt IS NULL THEN a.Amount ELSE 0 END),0)
                OR SUM(CASE WHEN a.ReconciliationId=@Id AND a.ReversedAt IS NULL THEN 1 ELSE 0 END)>0
            ORDER BY e.OccurredAt,e.EntryNumber,l.LineNumber;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", reconciliationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while(await reader.ReadAsync(cancellationToken)) { var amount=reader.GetDecimal(9);var allocated=reader.GetDecimal(10);books.Add(new(reader.GetGuid(0),reader.GetInt32(1),reader.GetString(2),reader.GetDateTimeOffset(3),reader.GetGuid(4),reader.GetString(5),reader.GetString(6),reader.GetGuid(7),reader.GetString(8),amount,allocated,Math.Max(0,Math.Abs(amount)-allocated))); }
        }
        var allocations = new List<BankReconciliationAllocationView>();
        await using (var command = new SqlCommand("SELECT MatchId,StatementLineId,EntryId,EntryLineNumber,Amount,CreatedAt FROM accounting.BankReconciliationAllocations WHERE ReconciliationId=@Id AND ReversedAt IS NULL ORDER BY CreatedAt,MatchId",connection,transaction))
        { command.Parameters.AddWithValue("@Id",reconciliationId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))allocations.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetInt32(3),reader.GetDecimal(4),reader.GetDateTimeOffset(5))); }
        var events = new List<BankReconciliationStatusEventView>();
        await using (var command = new SqlCommand("""
            SELECT ev.StatusEventId,ev.Status,ev.Reason,ev.ActorUserId,
              CONCAT(u.FirstName,N' ',u.LastName),ev.OccurredAt,ev.StatementClosingBalance,
              ev.BookClosingBalance,ev.AccountingCutoffPostedAt
            FROM accounting.BankReconciliationStatusEvents ev
            JOIN dbo.AppUsers u ON u.UserId=ev.ActorUserId
            WHERE ev.ReconciliationId=@Id ORDER BY ev.OccurredAt,ev.StatusEventId;
            """,connection,transaction))
        { command.Parameters.AddWithValue("@Id",reconciliationId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))events.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetDateTimeOffset(5),reader.IsDBNull(6)?null:reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetDecimal(7),reader.IsDBNull(8)?null:reader.GetDateTimeOffset(8))); }
        return new(summary,statement,books,allocations,events);
    }

    private static async Task<IReadOnlyList<BankReconciliationSummaryView>> ReadSummariesAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        CancellationToken cancellationToken, Guid? reconciliationId = null)
    {
        var result = new List<BankReconciliationSummaryView>();
        await using var command = new SqlCommand("""
            SELECT r.ReconciliationId,r.BankAccountId,b.DisplayName,a.Code,a.Name,r.PeriodFrom,r.PeriodTo,
              r.OpeningBalance,r.ClosingBalance,r.FileName,CONVERT(varchar(64),r.FileSha256,2),r.Status,
              COUNT(DISTINCT s.StatementLineId),COUNT(DISTINCT CASE WHEN ABS(s.Amount)=COALESCE(x.Allocated,0) THEN s.StatementLineId END),
              COALESCE(SUM(s.Amount),0),
              COALESCE((SELECT SUM(l.Debit-l.Credit) FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId WHERE e.TenantId=r.TenantId AND l.AccountId=b.AccountingAccountId AND CAST(e.OccurredAt AS date) BETWEEN r.PeriodFrom AND r.PeriodTo),0),
              r.ClosingBalance-COALESCE((SELECT SUM(l.Debit-l.Credit) FROM dbo.AccountingEntries e JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId WHERE e.TenantId=r.TenantId AND l.AccountId=b.AccountingAccountId AND CAST(e.OccurredAt AS date)<=r.PeriodTo),0),
              CONVERT(bit,CASE WHEN r.Status=N'Closed' AND EXISTS(
                SELECT 1 FROM dbo.AccountingEntries changed
                JOIN dbo.AccountingEntryLines changedLine ON changedLine.EntryId=changed.EntryId AND changedLine.AccountId=b.AccountingAccountId
                WHERE changed.TenantId=r.TenantId AND CAST(changed.OccurredAt AS date)<=r.PeriodTo
                  AND changed.PostedAt>(SELECT MAX(ev.OccurredAt) FROM accounting.BankReconciliationStatusEvents ev WHERE ev.ReconciliationId=r.ReconciliationId AND ev.Status=N'Closed')) THEN 1 ELSE 0 END),
              r.RowVersion,r.UpdatedAt
            FROM accounting.BankReconciliations r JOIN accounting.BankAccounts b ON b.BankAccountId=r.BankAccountId
            JOIN dbo.AccountingAccounts a ON a.AccountId=b.AccountingAccountId
            LEFT JOIN accounting.BankStatementLines s ON s.ReconciliationId=r.ReconciliationId
            OUTER APPLY(SELECT SUM(ra.Amount) Allocated FROM accounting.BankReconciliationAllocations ra WHERE ra.StatementLineId=s.StatementLineId AND ra.ReversedAt IS NULL)x
            WHERE r.TenantId=@TenantId AND (@Id IS NULL OR r.ReconciliationId=@Id)
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries scopedEntry
                JOIN dbo.AccountingEntryLines scopedLine ON scopedLine.EntryId=scopedEntry.EntryId AND scopedLine.AccountId=b.AccountingAccountId
                WHERE scopedEntry.TenantId=r.TenantId AND NOT EXISTS(
                  SELECT 1 FROM dbo.UserRoles ur JOIN dbo.AppRoles role ON role.RoleId=ur.RoleId AND role.IsActive=1
                  JOIN dbo.RolePermissions rp ON rp.RoleId=role.RoleId JOIN dbo.Permissions permission ON permission.PermissionId=rp.PermissionId
                  WHERE ur.UserId=@UserId AND (ur.BusinessId IS NULL OR ur.BusinessId=scopedEntry.BusinessId)
                    AND (role.TenantId IS NULL OR role.TenantId=@TenantId) AND permission.Resource=@ReadPermission))
            GROUP BY r.ReconciliationId,r.BankAccountId,b.DisplayName,b.AccountingAccountId,a.Code,a.Name,r.PeriodFrom,r.PeriodTo,r.OpeningBalance,r.ClosingBalance,r.FileName,r.FileSha256,r.Status,r.RowVersion,r.UpdatedAt,r.TenantId,r.BusinessId ORDER BY r.PeriodTo DESC,r.UpdatedAt DESC;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@ReadPermission",AccountingPermissionCodes.BankReconciliationRead);command.Parameters.AddWithValue("@Id",(object?)reconciliationId??DBNull.Value);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),DateOnly.FromDateTime(reader.GetDateTime(5)),DateOnly.FromDateTime(reader.GetDateTime(6)),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetInt32(12),reader.GetInt32(13),reader.GetDecimal(14),reader.GetDecimal(15),reader.GetDecimal(16),reader.GetBoolean(17),Convert.ToBase64String((byte[])reader[18]),reader.GetDateTimeOffset(19)));
        return result;
    }

    private static void AddHeaderParameters(SqlCommand command, AccountingUserIdentity user,
        ImportBankReconciliationRequest request, byte[] file, byte[] hash, DateTimeOffset now)
    { command.Parameters.AddWithValue("@ReconciliationId",request.ReconciliationId);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@BankAccountId",request.BankAccountId);command.Parameters.AddWithValue("@PeriodFrom",request.PeriodFrom.ToDateTime(TimeOnly.MinValue));command.Parameters.AddWithValue("@PeriodTo",request.PeriodTo.ToDateTime(TimeOnly.MinValue));AddMoney(command,"@OpeningBalance",request.OpeningBalance);AddMoney(command,"@ClosingBalance",request.ClosingBalance);command.Parameters.AddWithValue("@FileName",request.FileName.Trim());command.Parameters.Add("@FileSha256",SqlDbType.Binary,32).Value=hash;command.Parameters.Add("@OriginalFile",SqlDbType.VarBinary,-1).Value=file;command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",now); }
    private void AddMutationParameters(SqlCommand command, AccountingUserIdentity user, Guid id)
    {command.Parameters.AddWithValue("@ReconciliationId",id);command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());}
    private static void AddMoney(SqlCommand command,string name,decimal value){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=4;p.Value=value;}
    private static void AddNullableMoney(SqlCommand command,string name,decimal? value){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=4;p.Value=(object?)value??DBNull.Value;}

    private static async Task<bool> HasCompleteBusinessScopeAsync(SqlConnection connection,
        AccountingUserIdentity user, Guid? reconciliationId, string permission, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(
              SELECT 1 FROM accounting.BankReconciliations reconciliation
              JOIN accounting.BankAccounts bank ON bank.BankAccountId=reconciliation.BankAccountId
              JOIN dbo.AccountingEntryLines bankLine ON bankLine.AccountId=bank.AccountingAccountId
              JOIN dbo.AccountingEntries bankEntry ON bankEntry.EntryId=bankLine.EntryId AND bankEntry.TenantId=reconciliation.TenantId
              WHERE (@ReconciliationId IS NULL OR reconciliation.ReconciliationId=@ReconciliationId)
                AND reconciliation.TenantId=@TenantId AND NOT EXISTS(
                SELECT 1 FROM dbo.UserRoles ur JOIN dbo.AppRoles role ON role.RoleId=ur.RoleId AND role.IsActive=1
                JOIN dbo.RolePermissions rp ON rp.RoleId=role.RoleId JOIN dbo.Permissions granted ON granted.PermissionId=rp.PermissionId
                WHERE ur.UserId=@UserId AND (ur.BusinessId IS NULL OR ur.BusinessId=bankEntry.BusinessId)
                  AND (role.TenantId IS NULL OR role.TenantId=@TenantId) AND granted.Resource=@Permission)) THEN 0 ELSE 1 END);
            """, connection);
        command.Parameters.AddWithValue("@ReconciliationId", (object?)reconciliationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Permission", permission);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
