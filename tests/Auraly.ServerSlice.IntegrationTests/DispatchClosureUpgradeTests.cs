using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class DispatchClosureUpgradeTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public async Task Upgrade_preserves_legacy_closure_and_money_without_replaying_finance(bool hasDeclarations,bool oldCommonShape = false)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        if(oldCommonShape)
        {
            // Reproduce the deployed common tables before their native dispatch
            // columns existed. Rollback restores the entire fixture schema/data.
            await using var oldSchema = new SqlCommand("""
                DROP INDEX UX_WorkSessions_Web_User_Open ON dbo.WorkSessions;
                DROP INDEX UX_WorkSessions_Dispatch ON dbo.WorkSessions;
                ALTER TABLE dbo.WorkSessions DROP CONSTRAINT CK_WorkSessions_Kind,FK_WorkSessions_Dispatch,DF_WorkSessions_Kind;
                ALTER TABLE dbo.WorkSessions DROP COLUMN Kind,DispatchId;
                ALTER TABLE dbo.WorkSessionClosures DROP CONSTRAINT CK_WorkSessionClosures_DispatchStatus,CK_WorkSessionClosures_DispatchAmounts;
                DECLARE @Drop nvarchar(max)=N'';
                SELECT @Drop=@Drop+N'ALTER TABLE dbo.WorkSessionClosures DROP CONSTRAINT '+QUOTENAME(fk.name)+N';'
                FROM sys.foreign_keys fk JOIN sys.foreign_key_columns part ON part.constraint_object_id=fk.object_id
                JOIN sys.columns columninfo ON columninfo.object_id=part.parent_object_id AND columninfo.column_id=part.parent_column_id
                WHERE part.parent_object_id=OBJECT_ID(N'dbo.WorkSessionClosures') AND columninfo.name=N'ReceivedByUserId';
                EXEC sys.sp_executesql @Drop;
                ALTER TABLE dbo.WorkSessionClosures DROP COLUMN DispatchStatus,DispatchExpectedCash,DispatchTransferTotal,
                  DispatchCreditDocumentTotal,DispatchCreditAdvanceTotal,DispatchReturnTotal,ReceivedCash,ReceivedByUserId,ReceivedAt,DispatchReviewNotes;
                ALTER TABLE dbo.DispatchDeliveryEvents DROP CONSTRAINT DF_DispatchDeliveryEvents_ReturnTotal;
                ALTER TABLE dbo.DispatchDeliveryEvents DROP COLUMN ReturnTotal;
                """,connection,transaction);
            await oldSchema.ExecuteNonQueryAsync();
        }
        var dispatch = Guid.NewGuid();
        var closure = Guid.NewGuid();
        // The pre-cutover table intentionally exists only inside this isolated
        // rollback transaction. Production has no compatibility table or writer.
        await using var seed = new SqlCommand("""
            EXEC(N'CREATE PROCEDURE dbo.DispatchSettlementOperationClaimMark
              @Id uniqueidentifier,@DispatchId uniqueidentifier AS BEGIN SET NOCOUNT ON; END;');
            CREATE TABLE dbo.DispatchSettlements(
              DispatchSettlementId uniqueidentifier NOT NULL PRIMARY KEY,BusinessId uniqueidentifier NOT NULL,
              DispatchId uniqueidentifier NOT NULL,ExpectedCash decimal(19,4) NOT NULL,DeclaredCash decimal(19,4) NOT NULL,
              CashDifference AS (COALESCE(CashReceived,DeclaredCash)-ExpectedCash) PERSISTED,
              DepositTotal decimal(19,4) NOT NULL,CreditDocumentTotal decimal(19,4) NOT NULL,
              CreditAdvanceTotal decimal(19,4) NOT NULL,ReturnTotal decimal(19,4) NOT NULL,
              DifferenceReason nvarchar(500) NULL,TransporterClosedBy uniqueidentifier NOT NULL,
              TransporterClosedAt datetimeoffset(7) NOT NULL,CashReceived decimal(19,4) NULL,
              ReceivedBy uniqueidentifier NULL,ReceivedAt datetimeoffset(7) NULL,Notes nvarchar(500) NULL,
              Status nvarchar(32) NOT NULL,IdempotencyKey nvarchar(128) NOT NULL);
            INSERT dbo.Dispatches(DispatchId,TenantId,BusinessId,DispatchNumber,ScheduledDate,DriverUserId,DriverName,
              Status,CreatedBy,CreatedAt,UpdatedAt)
              VALUES(@Dispatch,@Tenant,@Business,CONVERT(nvarchar(36),@Dispatch),'20261118',@User,N'Transportador migrado',
                N'Closed',@User,'20261118','20261118');
            INSERT dbo.DispatchSettlements VALUES(@Closure,@Business,@Dispatch,100000,99000,20000,30000,0,5000,
              N'Conteo original',@User,'2026-11-18T15:00:00Z',98000,@User,'2026-11-18T16:00:00Z',
              N'Recepción conservada',N'Completed',CONVERT(nvarchar(36),@Closure));
            """, connection, transaction);
        seed.Parameters.AddWithValue("@Dispatch", dispatch); seed.Parameters.AddWithValue("@Closure", closure);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId); seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
        seed.Parameters.AddWithValue("@User", fixture.UserId);
        await seed.ExecuteNonQueryAsync();
        if (hasDeclarations)
        {
            await using var add = new SqlCommand("""
                ALTER TABLE dbo.DispatchSettlements ADD DeclaredCard decimal(19,4) NULL,DeclaredTransfer decimal(19,4) NULL;
                EXEC(N'UPDATE dbo.DispatchSettlements SET DeclaredCard=12345,DeclaredTransfer=20001');
                """, connection, transaction);
            await add.ExecuteNonQueryAsync();
        }
        const string script = "20261118_UnifyDispatchWorkSessionClosures.sql";
        await fixture.ApplyMigrationAsync(script, connection, transaction);
        await fixture.ApplyMigrationAsync(script, connection, transaction);
        await using var verify = new SqlCommand("""
            SELECT closure.DispatchSettlementId,closure.ExpectedCash,closure.DeclaredCash,closure.CashReceived,
              closure.CashDifference,closure.DepositTotal,closure.CreditDocumentTotal,closure.ReturnTotal,
              closure.DifferenceReason,closure.Notes,closure.DeclaredCard,closure.DeclaredTransfer,
              (SELECT COUNT(*) FROM dbo.WorkSessions WHERE DispatchId=@Dispatch AND Kind=N'Dispatch'),
              (SELECT COUNT(*) FROM dbo.WorkSessionClosures header WHERE header.WorkSessionClosureId=@Closure),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Closure),
              (SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE Reference=CONVERT(nvarchar(36),@Dispatch)),
              OBJECT_ID(N'dbo.DispatchSettlements',N'U'),
              JSON_VALUE(header.ReceiptSnapshotJson,'$.legacySettlement.Notes'),
              OBJECT_ID(N'dbo.DispatchSettlementOperationClaimMark',N'P')
            FROM dispatch.Closures closure JOIN dbo.WorkSessionClosures header ON header.WorkSessionClosureId=closure.DispatchSettlementId
            WHERE closure.DispatchId=@Dispatch;
            """, connection, transaction);
        verify.Parameters.AddWithValue("@Dispatch", dispatch); verify.Parameters.AddWithValue("@Closure", closure);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(closure, reader.GetGuid(0));
        Assert.Equal(100000m, reader.GetDecimal(1)); Assert.Equal(99000m, reader.GetDecimal(2));
        Assert.Equal(98000m, reader.GetDecimal(3)); Assert.Equal(-2000m, reader.GetDecimal(4));
        Assert.Equal(20000m, reader.GetDecimal(5)); Assert.Equal(30000m, reader.GetDecimal(6));
        Assert.Equal(5000m, reader.GetDecimal(7)); Assert.Equal("Conteo original", reader.GetString(8));
        Assert.Equal("Recepción conservada", reader.GetString(9));
        if (hasDeclarations) { Assert.Equal(12345m, reader.GetDecimal(10)); Assert.Equal(20001m, reader.GetDecimal(11)); }
        else { Assert.True(reader.IsDBNull(10)); Assert.True(reader.IsDBNull(11)); }
        Assert.Equal(1, reader.GetInt32(12)); Assert.Equal(1, reader.GetInt32(13));
        Assert.Equal(0, reader.GetInt32(14)); Assert.Equal(0, reader.GetInt32(15)); Assert.True(reader.IsDBNull(16));
        Assert.Equal("Recepción conservada", reader.GetString(17)); Assert.True(reader.IsDBNull(18));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.RollbackAsync();
    }
}
