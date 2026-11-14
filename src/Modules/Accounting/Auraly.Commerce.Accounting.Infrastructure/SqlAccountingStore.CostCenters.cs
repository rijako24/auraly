using System.Data;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    public async Task<AccountingCostCenterPage> ListCostCentersAsync(
        AccountingUserIdentity user, AccountingCostCenterQuery query, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        // Application validates the closed order contract; identifiers never come
        // from SQL parameters or unvalidated client text.
        var order = query.SortBy == "name" ? "Name" : "Code";
        var direction = query.SortDirection == "desc" ? "DESC" : "ASC";
        await using var command = new SqlCommand($"""
            SELECT COUNT(*) FROM dbo.AccountingCostCenters
            WHERE TenantId=@TenantId AND (@Active IS NULL OR IsActive=@Active)
              AND (@Default IS NULL OR IsDefault=@Default) AND (@Id IS NULL OR CostCenterId=@Id)
              AND (@Ids IS NULL OR CostCenterId IN(SELECT CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@Ids)))
              AND (@Search IS NULL OR Code LIKE @Search OR Name LIKE @Search);
            SELECT CostCenterId,TenantId,Code,Name,ParentCostCenterId,IsDefault,IsActive,RowVersion
            FROM dbo.AccountingCostCenters
            WHERE TenantId=@TenantId AND (@Active IS NULL OR IsActive=@Active)
              AND (@Default IS NULL OR IsDefault=@Default) AND (@Id IS NULL OR CostCenterId=@Id)
              AND (@Ids IS NULL OR CostCenterId IN(SELECT CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@Ids)))
              AND (@Search IS NULL OR Code LIKE @Search OR Name LIKE @Search)
            ORDER BY {order} {direction},CostCenterId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY OPTION(RECOMPILE);
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.Add("@Active", SqlDbType.Bit).Value = (object?)query.IsActive ?? DBNull.Value;
        command.Parameters.Add("@Default", SqlDbType.Bit).Value = (object?)query.IsDefault ?? DBNull.Value;
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = (object?)query.CostCenterId ?? DBNull.Value;
        command.Parameters.Add("@Ids", SqlDbType.NVarChar, -1).Value = query.Ids is null ? DBNull.Value : JsonSerializer.Serialize(query.Ids);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 322).Value = string.IsNullOrWhiteSpace(query.Search)
            ? DBNull.Value : "%" + query.Search + "%";
        command.Parameters.AddWithValue("@Skip", checked((query.Page - 1) * query.PageSize));
        command.Parameters.AddWithValue("@Take", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var rows = new List<AccountingCostCenterView>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadCostCenter(reader));
        return new(rows, query.Page, query.PageSize, count);
    }

    public async Task<AccountingCostCenterView> CreateCostCenterAsync(
        AccountingUserIdentity user, CreateCostCenterRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF @Parent IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE CostCenterId=@Parent AND TenantId=@Tenant AND IsActive=1)
                  THROW 51411,N'El centro superior debe estar activo en esta empresa.',1;
                INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,ParentCostCenterId,IsDefault,IsActive,CreatedAt)
                OUTPUT inserted.CostCenterId,inserted.TenantId,inserted.Code,inserted.Name,
                  inserted.ParentCostCenterId,inserted.IsDefault,inserted.IsActive,inserted.RowVersion
                VALUES(@Id,@Tenant,@Code,@Name,@Parent,@Default,1,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@Id", request.CostCenterId);
            command.Parameters.AddWithValue("@Tenant", user.TenantId);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@Parent", (object?)request.ParentCostCenterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Default", request.IsDefault);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var saved = ReadCostCenter(reader);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return saved;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new AccountingConflictException("El código o el centro predeterminado ya existe en esta empresa.");
        }
        catch (SqlException error) when (error.Number == 51411)
        {
            throw new AccountingValidationException(error.Message);
        }
    }

    public async Task<AccountingCostCenterView> SetCostCenterStatusAsync(
        AccountingUserIdentity user, Guid costCenterId, SetAccountingCostCenterStatusRequest request,
        CancellationToken cancellationToken)
    {
        // The status contract carries no editable metadata. Read only this ID;
        // the caller's version is still checked atomically by the same writer.
        var page = await ListCostCentersAsync(user, new(CostCenterId: costCenterId, PageSize: 1), cancellationToken);
        var current = page.Items.SingleOrDefault()
            ?? throw new AccountingValidationException("El centro no pertenece a esta empresa.");
        return await UpdateCostCenterAsync(user, costCenterId, new(current.Code, current.Name,
            current.ParentCostCenterId, current.IsDefault, request.IsActive, request.RowVersion ?? ""), cancellationToken);
    }

    public async Task<AccountingCostCenterView> UpdateCostCenterAsync(
        AccountingUserIdentity user, Guid costCenterId, UpdateCostCenterRequest request, CancellationToken cancellationToken)
    {
        var version = RequiredRecordVersion(request.RowVersion);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@Tenant AND CostCenterId=@Id AND RowVersion=@Version)
                  THROW 51415,N'El centro cambió o no pertenece a esta empresa. Actualiza la lista.',1;
                IF @Parent IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@Tenant AND CostCenterId=@Parent AND IsActive=1)
                  THROW 51411,N'El centro superior debe estar activo en esta empresa.',1;
                DECLARE @Cycle bit=0;
                ;WITH Ancestors AS(
                  SELECT CostCenterId,ParentCostCenterId FROM dbo.AccountingCostCenters
                  WHERE TenantId=@Tenant AND CostCenterId=@Parent
                  UNION ALL
                  SELECT parent.CostCenterId,parent.ParentCostCenterId FROM dbo.AccountingCostCenters parent
                  JOIN Ancestors child ON child.ParentCostCenterId=parent.CostCenterId
                  WHERE parent.TenantId=@Tenant AND child.CostCenterId<>@Id)
                SELECT @Cycle=1 FROM Ancestors WHERE CostCenterId=@Id OPTION(MAXRECURSION 32767);
                IF @Cycle=1 THROW 51411,N'El centro superior genera un ciclo.',1;
                IF @Default=0 AND EXISTS(SELECT 1 FROM dbo.AccountingCostCenters
                  WHERE TenantId=@Tenant AND CostCenterId=@Id AND IsDefault=1)
                  THROW 51412,N'Selecciona otro centro como predeterminado antes de sustituir este.',1;
                IF @Active=0
                BEGIN
                  IF @Default=1 THROW 51412,N'El centro predeterminado debe permanecer activo.',1;
                  DECLARE @Dependencies nvarchar(2048);
                  SELECT @Dependencies=STRING_AGG(CONVERT(nvarchar(max),Label),N'; ') FROM(
                    SELECT TOP(10) Label FROM(
                      SELECT N'Centro hijo: '+Code+N' · '+Name Label FROM dbo.AccountingCostCenters
                        WHERE TenantId=@Tenant AND ParentCostCenterId=@Id AND IsActive=1
                      UNION ALL SELECT N'Regla: '+assignment.OperationKind+N' / '+business.Name
                        FROM dbo.AccountingCostCenterAssignments assignment JOIN dbo.Businesses business ON business.BusinessId=assignment.BusinessId
                        WHERE assignment.TenantId=@Tenant AND assignment.CostCenterId=@Id AND assignment.IsActive=1
                      UNION ALL SELECT N'Concepto de gasto: '+concept.Code+N' · '+concept.Name
                        FROM dbo.ExpenseConcepts concept JOIN dbo.Businesses business ON business.BusinessId=concept.BusinessId
                        WHERE business.TenantId=@Tenant AND concept.DefaultCostCenterId=@Id AND concept.IsActive=1
                      UNION ALL SELECT N'Motivo: '+reason.Code+N' · '+reason.Name
                        FROM dbo.BusinessReasons reason JOIN dbo.Businesses business ON business.BusinessId=reason.BusinessId
                        WHERE business.TenantId=@Tenant AND reason.DefaultCostCenterId=@Id AND reason.IsActive=1
                    ) dependencies ORDER BY Label
                  ) selected;
                  IF @Dependencies IS NOT NULL THROW 51413,@Dependencies,1;
                END;
                IF @Default=1 UPDATE dbo.AccountingCostCenters SET IsDefault=0
                  WHERE TenantId=@Tenant AND IsDefault=1 AND CostCenterId<>@Id;
                UPDATE dbo.AccountingCostCenters SET Code=@Code,Name=@Name,ParentCostCenterId=@Parent,
                  IsDefault=@Default,IsActive=@Active WHERE TenantId=@Tenant AND CostCenterId=@Id;
                SELECT CostCenterId,TenantId,Code,Name,ParentCostCenterId,IsDefault,IsActive,RowVersion
                FROM dbo.AccountingCostCenters WHERE TenantId=@Tenant AND CostCenterId=@Id;
                """, connection, transaction);
            command.Parameters.AddWithValue("@Tenant", user.TenantId);
            command.Parameters.AddWithValue("@Id", costCenterId);
            command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = version;
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@Parent", (object?)request.ParentCostCenterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Default", request.IsDefault);
            command.Parameters.AddWithValue("@Active", request.IsActive);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var saved = ReadCostCenter(reader);
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return saved;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 51415)
        {
            throw new AccountingConflictException(error.Number == 51415 ? error.Message : "El código ya existe en esta empresa.");
        }
        catch (SqlException error) when (error.Number is 51411 or 51412 or 51413)
        {
            throw new AccountingValidationException(error.Message);
        }
    }

    private static AccountingCostCenterView ReadCostCenter(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetBoolean(5), reader.GetBoolean(6),
        Convert.ToBase64String((byte[])reader[7]));
}
