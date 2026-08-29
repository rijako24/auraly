using System.Data;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore
{
    public async Task<IReadOnlyList<TaxProfileSummary>> ListTaxProfilesAsync(
        CatalogUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await EnsureDefaultTaxProfilesAsync(connection, user, ct);
        await using var command = new SqlCommand("""
            SELECT t.TaxProfileId,t.BusinessId,t.Code,t.DianTaxCode,t.Name,t.Rate,t.IsActive
            FROM dbo.TaxProfiles t
            INNER JOIN dbo.Businesses b ON b.BusinessId=t.BusinessId
            WHERE t.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND (@IncludeInactive=1 OR t.IsActive=1)
            ORDER BY t.Rate,t.Name,t.Code;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@IncludeInactive", includeInactive);
        var result = new List<TaxProfileSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetBoolean(6)));
        return result;
    }

    public async Task<TaxProfileSummary> SaveTaxProfileAsync(
        CatalogUserIdentity user, Guid? taxProfileId, SaveTaxProfileRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var id = taxProfileId ?? ids.NewId();
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses
                  WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51021,'The business is outside the authenticated tenant.',1;

                IF @Create=1
                  INSERT dbo.TaxProfiles
                    (TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                  VALUES(@TaxProfileId,@BusinessId,@Code,@DianTaxCode,@Name,@Rate,@IsActive,@Now);
                ELSE
                BEGIN
                  UPDATE dbo.TaxProfiles
                  SET Code=@Code,DianTaxCode=@DianTaxCode,Name=@Name,Rate=@Rate,IsActive=@IsActive
                  WHERE TaxProfileId=@TaxProfileId AND BusinessId=@BusinessId;
                  IF @@ROWCOUNT=0 THROW 51010,'The VAT master was not found.',1;
                END;

                SELECT TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive
                FROM dbo.TaxProfiles WHERE TaxProfileId=@TaxProfileId;
                """, connection, transaction);
            command.Parameters.AddWithValue("@Create", !taxProfileId.HasValue);
            command.Parameters.AddWithValue("@TaxProfileId", id);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@DianTaxCode", request.DianTaxCode);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@Rate", request.Rate);
            command.Parameters.AddWithValue("@IsActive", request.IsActive);
            command.Parameters.AddWithValue("@Now", now);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var result = new TaxProfileSummary(reader.GetGuid(0),reader.GetGuid(1),
                reader.GetString(2),reader.GetString(3),reader.GetString(4),
                reader.GetDecimal(5),reader.GetBoolean(6));
            await reader.CloseAsync();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogConflictException("Ya existe un IVA con este código en el negocio.");
        }
    }

    private async Task EnsureDefaultTaxProfilesAsync(
        SqlConnection connection, CatalogUserIdentity user, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
            BEGIN
              IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles WHERE BusinessId=@BusinessId AND Rate=0 AND DianTaxCode=N'01')
                INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                VALUES(@ZeroId,@BusinessId,N'IVA-0',N'01',N'IVA 0%',0,1,@Now);
              IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles WHERE BusinessId=@BusinessId AND Rate=5 AND DianTaxCode=N'01')
                INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                VALUES(@FiveId,@BusinessId,N'IVA-5',N'01',N'IVA 5%',5,1,@Now);
              IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles WHERE BusinessId=@BusinessId AND Rate=19 AND DianTaxCode=N'01')
                INSERT dbo.TaxProfiles(TaxProfileId,BusinessId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                VALUES(@NineteenId,@BusinessId,N'IVA-19',N'01',N'IVA 19%',19,1,@Now);
            END
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@ZeroId", ids.NewId());
        command.Parameters.AddWithValue("@FiveId", ids.NewId());
        command.Parameters.AddWithValue("@NineteenId", ids.NewId());
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ProductTaxConfiguration?> GetProductTaxConfigurationAsync(
        CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT p.ProductId,p.TaxProfileId,COALESCE(p.PurchaseTaxProfileId,p.TaxProfileId),
                   p.PurchaseTaxTreatment
            FROM dbo.Products p
            WHERE p.ProductId=@ProductId AND p.TenantId=@TenantId
              AND EXISTS(SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId);
            """, connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(1) || reader.IsDBNull(2)) return null;
        return new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
            reader.IsDBNull(3) ? "DeductibleInputVat" : reader.GetString(3));
    }

    public async Task<ProductTaxConfiguration> SaveProductTaxConfigurationAsync(
        CatalogUserIdentity user, Guid productId, SaveProductTaxConfigurationRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles
                  WHERE TaxProfileId=@SalesTaxProfileId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51021,'The sales VAT profile is invalid.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles
                  WHERE TaxProfileId=@PurchaseTaxProfileId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51021,'The purchase VAT profile is invalid.',1;

                IF EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxProfileId AND BusinessId=@BusinessId AND Rate=0) AND @PurchaseTaxTreatment<>N'NotApplicable'
                  THROW 51024,'A zero-rated purchase VAT profile must use NotApplicable treatment.',1;
                IF EXISTS (SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxProfileId AND BusinessId=@BusinessId AND Rate>0) AND @PurchaseTaxTreatment=N'NotApplicable'
                  THROW 51024,'A positive purchase VAT profile must use DeductibleInputVat or CapitalizedCost treatment.',1;

                UPDATE p SET TaxProfileId=@SalesTaxProfileId,
                  PurchaseTaxProfileId=@PurchaseTaxProfileId,
                  PurchaseTaxTreatment=@PurchaseTaxTreatment,
                  UpdatedAt=@Now,UpdatedByUserId=@UserId
                FROM dbo.Products p
                WHERE p.ProductId=@ProductId AND p.TenantId=@TenantId
                  AND EXISTS(SELECT 1 FROM dbo.Businesses b WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId);
                IF @@ROWCOUNT=0 THROW 51010,'The product was not found.',1;

                DECLARE @Change TABLE(CatalogChangeId BIGINT NOT NULL);
                INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                  OUTPUT inserted.CatalogChangeId INTO @Change
                  VALUES(@BusinessId,@ProductId,N'Upsert',@Now);
                INSERT dbo.PosSynchronizationOutboxMessages
                  (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                  SELECT @NotificationId,@BusinessId,N'Catalog',CatalogChangeId,@Now FROM @Change;
                """, connection, transaction);
            command.Parameters.AddWithValue("@SalesTaxProfileId", request.SalesTaxProfileId);
            command.Parameters.AddWithValue("@PurchaseTaxProfileId", request.PurchaseTaxProfileId);
            command.Parameters.AddWithValue("@PurchaseTaxTreatment", request.PurchaseTaxTreatment);
            command.Parameters.AddWithValue("@ProductId", productId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@NotificationId", ids.NewId());
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return new(productId,request.SalesTaxProfileId,request.PurchaseTaxProfileId,
                request.PurchaseTaxTreatment);
        }
        catch (SqlException exception) when (exception.Number == 51024)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogValidationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 51010 or 51021)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new CatalogConflictException(exception.Message);
        }
    }}
