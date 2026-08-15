using System.Data;
using System.Text.Json;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlPasswordRecoveryStore(ApplicationDbContext db) : IPasswordRecoveryStore
{
    public async Task CreateAsync(
        RequestPasswordRecoveryRequest request,
        Guid requestId,
        string rawToken,
        byte[] tokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        const string sql = """
            DECLARE @TenantId uniqueidentifier,@UserId uniqueidentifier,@DeliveryEmail nvarchar(256);
            SELECT @TenantId=t.TenantId,@UserId=u.UserId,@DeliveryEmail=u.Email
            FROM dbo.Tenants t
            JOIN dbo.AppUsers u ON u.TenantId=t.TenantId
            WHERE t.TenantKey=@TenantKey AND t.IsActive=1 AND u.IsActive=1
              AND u.NormalizedUsername=@Username AND u.NormalizedEmail=@Email;
            IF @UserId IS NOT NULL
            BEGIN
              UPDATE dbo.PasswordResetRequests SET Status=N'Revoked'
              WHERE TenantId=@TenantId AND UserId=@UserId AND Status=N'Pending';
              INSERT dbo.PasswordResetRequests
                (PasswordResetRequestId,TenantId,UserId,TokenHash,Status,ExpiresAt,CreatedAt)
              VALUES(@RequestId,@TenantId,@UserId,@TokenHash,N'Pending',@ExpiresAt,@Now);
              INSERT dbo.TenantProvisioningOutboxMessages
                (MessageId,TenantId,Type,Payload,OccurredAt,AvailableAt,AttemptCount)
              VALUES(NEWID(),@TenantId,N'PasswordRecoveryEmail',@Payload,@Now,@Now,0);
            END;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@TenantKey", request.TenantKey.Trim());
        command.Parameters.AddWithValue("@Username", request.Username.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@Email", request.Email.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@RequestId", requestId);
        command.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        command.Parameters.AddWithValue("@ExpiresAt", expiresAt);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", JsonSerializer.Serialize(new
        {
            requestId,
            email = request.Email.Trim(),
            resetToken = rawToken
        }));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> ConsumeAsync(
        byte[] tokenHash,
        PasswordRecoveryMaterial material,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        const string sql = """
            DECLARE @RequestId uniqueidentifier,@UserId uniqueidentifier,@ExpiresAt datetimeoffset(7),@Status nvarchar(16),@IsActive bit;
            SELECT @RequestId=r.PasswordResetRequestId,@UserId=r.UserId,@ExpiresAt=r.ExpiresAt,@Status=r.Status,@IsActive=u.IsActive
            FROM dbo.PasswordResetRequests r WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.AppUsers u ON u.UserId=r.UserId AND u.TenantId=r.TenantId
            WHERE r.TokenHash=@TokenHash;
            IF @RequestId IS NULL OR @Status<>N'Pending' OR @IsActive=0
            BEGIN SELECT CAST(0 AS bit); RETURN; END;
            IF @ExpiresAt<=@Now
            BEGIN
              UPDATE dbo.PasswordResetRequests SET Status=N'Expired' WHERE PasswordResetRequestId=@RequestId;
              SELECT CAST(0 AS bit); RETURN;
            END;
            UPDATE dbo.AppUsers
            SET PasswordHash=@PasswordHash,PosOfflinePasswordSalt=@OfflineSalt,
                PosOfflinePasswordHash=@OfflineHash,PosOfflinePasswordIterations=@OfflineIterations,
                PosOfflinePasswordChangedAt=@ChangedAt,AccessFailedCount=0,LockoutEnd=NULL,UpdatedAt=@Now
            WHERE UserId=@UserId;
            UPDATE dbo.PasswordResetRequests SET Status=N'Used',UsedAt=@Now WHERE PasswordResetRequestId=@RequestId;
            UPDATE dbo.RefreshTokens SET RevokedAt=CONVERT(datetime2,@Now) WHERE UserId=@UserId AND RevokedAt IS NULL;
            UPDATE dbo.AuthenticationSessions
            SET Status=N'Revoked',RevokedAt=@Now,RevocationReason=N'PasswordReset',UpdatedAt=@Now
            WHERE UserId=@UserId AND Status=N'Active';
            SELECT CAST(1 AS bit);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
        command.Parameters.AddWithValue("@PasswordHash", material.PasswordHash);
        command.Parameters.Add("@OfflineSalt", SqlDbType.VarBinary, 16).Value = material.OfflineSalt;
        command.Parameters.Add("@OfflineHash", SqlDbType.VarBinary, 32).Value = material.OfflineHash;
        command.Parameters.AddWithValue("@OfflineIterations", material.OfflineIterations);
        command.Parameters.AddWithValue("@ChangedAt", material.ChangedAt);
        command.Parameters.AddWithValue("@Now", now);
        var consumed = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return consumed;
    }
}