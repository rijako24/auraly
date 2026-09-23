using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlInvoiceChargeStore(SqlServerConnectionFactory connections, TimeProvider time)
    : IInvoiceChargeStore
{
    // One round trip returns a scoped page and all its child rows. The same
    // projection follows the save batch, so callers reuse the authoritative result.
    private const string ReadSql = """
        DECLARE @PageIds TABLE(ChargeId uniqueidentifier,Version bigint,
          PRIMARY KEY(ChargeId,Version));
        SELECT COUNT(*) FROM sales.InvoiceChargeDefinitions d
        JOIN sales.InvoiceChargeVersions v ON v.ChargeId=d.ChargeId AND (
          (@RequestedVersions IS NOT NULL AND EXISTS(
            SELECT 1 FROM OPENJSON(@RequestedVersions)
              WITH(ChargeId uniqueidentifier,Version bigint) r
            WHERE r.ChargeId=v.ChargeId AND r.Version=v.Version)) OR
          (@RequestedVersions IS NULL AND v.Version=
            CASE WHEN @ThroughCursor IS NULL THEN d.CurrentVersion ELSE
              (SELECT MAX(s.Version) FROM sales.InvoiceChargeVersions s
               WHERE s.ChargeId=d.ChargeId AND s.SynchronizationCursor<=@ThroughCursor) END))
        JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
        WHERE d.BusinessId=@BusinessId AND b.TenantId=@TenantId
          AND (@OnlyId IS NULL OR d.ChargeId=@OnlyId) AND (@IncludeInactive=1 OR v.IsActive=1)
          AND (@Search IS NULL OR d.Code LIKE N'%'+@Search+N'%' OR v.Name LIKE N'%'+@Search+N'%');
        INSERT @PageIds SELECT d.ChargeId,v.Version FROM sales.InvoiceChargeDefinitions d
        JOIN sales.InvoiceChargeVersions v ON v.ChargeId=d.ChargeId AND (
          (@RequestedVersions IS NOT NULL AND EXISTS(
            SELECT 1 FROM OPENJSON(@RequestedVersions)
              WITH(ChargeId uniqueidentifier,Version bigint) r
            WHERE r.ChargeId=v.ChargeId AND r.Version=v.Version)) OR
          (@RequestedVersions IS NULL AND v.Version=
            CASE WHEN @ThroughCursor IS NULL THEN d.CurrentVersion ELSE
              (SELECT MAX(s.Version) FROM sales.InvoiceChargeVersions s
               WHERE s.ChargeId=d.ChargeId AND s.SynchronizationCursor<=@ThroughCursor) END))
        JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
        WHERE d.BusinessId=@BusinessId AND b.TenantId=@TenantId
          AND (@OnlyId IS NULL OR d.ChargeId=@OnlyId) AND (@IncludeInactive=1 OR v.IsActive=1)
          AND (@Search IS NULL OR d.Code LIKE N'%'+@Search+N'%' OR v.Name LIKE N'%'+@Search+N'%')
        ORDER BY v.SortOrder,d.Code COLLATE Latin1_General_100_BIN2 OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        SELECT d.ChargeId,d.BusinessId,v.Version,d.Code,v.Name,v.IsActive,v.SortOrder,
          v.CalculationMode,v.Value,v.InclusionMode,v.InvoiceAmountLimit,
          c.ExpenseConceptId,c.Name,v.ExpenseAccountId,a.Code,a.Name,v.CostCenterId,cc.Name,
          t.TaxProfileId,t.Name,v.SalesTaxCode,v.SalesTaxRate,pt.TaxProfileId,pt.Name,v.PurchaseTaxRate,v.WithholdingConceptCode
        FROM @PageIds p JOIN sales.InvoiceChargeDefinitions d ON d.ChargeId=p.ChargeId
        JOIN sales.InvoiceChargeVersions v ON v.ChargeId=p.ChargeId AND v.Version=p.Version
        JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=v.ExpenseConceptId AND c.BusinessId=d.BusinessId
        JOIN dbo.AccountingAccounts a ON a.AccountId=v.ExpenseAccountId AND a.TenantId=@TenantId
        LEFT JOIN dbo.AccountingCostCenters cc ON cc.CostCenterId=v.CostCenterId AND cc.BusinessId=d.BusinessId
        JOIN dbo.TaxProfiles t ON t.TaxProfileId=v.SalesTaxProfileId AND t.BusinessId=d.BusinessId
        JOIN dbo.TaxProfiles pt ON pt.TaxProfileId=v.PurchaseTaxProfileId AND pt.BusinessId=d.BusinessId
        ORDER BY v.SortOrder,d.Code COLLATE Latin1_General_100_BIN2;
        SELECT r.ChargeId,r.FromInclusive,r.ToExclusive,r.CalculationMode,r.Value
        FROM @PageIds p JOIN sales.InvoiceChargeRanges r ON r.ChargeId=p.ChargeId AND r.Version=p.Version
        ORDER BY r.ChargeId,r.Position;
        SELECT x.ChargeId,s.SupplierId,x.Name,x.Identification,x.DefaultPaymentDueDays,
          CONVERT(bit,CASE WHEN @RequestedVersions IS NOT NULL THEN x.IsActive
            WHEN s.IsActive=1 AND party.IsActive=1 THEN 1 ELSE 0 END),
          x.AppliesWithholding,x.TaxResponsibilitiesJson,x.TaxJurisdictionCode,x.PurchaseEvidencePolicy
        FROM @PageIds p JOIN sales.InvoiceChargeSuppliers x ON x.ChargeId=p.ChargeId AND x.Version=p.Version
        JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId AND s.BusinessId=@BusinessId
        JOIN dbo.Parties party ON party.PartyId=s.PartyId AND party.TenantId=@TenantId
        ORDER BY x.ChargeId,party.DisplayName,s.SupplierId;
        """;

    public async Task<InvoiceChargePage> ListAsync(InvoiceChargeActor actor, int page, int pageSize,
        string? search, bool includeInactive, CancellationToken ct, long? throughCursor = null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(ReadSql, connection);
        AddScope(command, actor);
        AddPage(command, page, pageSize, search, includeInactive, null, throughCursor);
        return await ReadAsync(command, page, pageSize, ct);
    }

    public async Task<InvoiceChargeDefinition?> ReadAsync(InvoiceChargeActor actor,
        Guid chargeId, long version, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(ReadSql, connection);
        AddScope(command, actor);
        AddPage(command, 1, 1, null, true, chargeId,
            requestedVersions: JsonSerializer.Serialize(new[] { new { ChargeId = chargeId, Version = version } }));
        return (await ReadAsync(command, 1, 1, ct)).Items.SingleOrDefault();
    }

    public async Task<IReadOnlyList<InvoiceChargeDefinition>> ReadManyAsync(
        InvoiceChargeActor actor, IReadOnlyCollection<(Guid ChargeId, long Version)> versions,
        CancellationToken ct)
    {
        if (versions.Count is < 1 or > InvoiceChargeApplication.MaximumChargesPerInvoice)
            throw new ArgumentOutOfRangeException(nameof(versions));
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(ReadSql, connection);
        AddScope(command, actor);
        AddPage(command, 1, InvoiceChargeApplication.MaximumChargesPerInvoice, null, true, null,
            requestedVersions: JsonSerializer.Serialize(versions.Select(value => new
            {
                value.ChargeId,
                value.Version
            })));
        return (await ReadAsync(command, 1, InvoiceChargeApplication.MaximumChargesPerInvoice, ct)).Items;
    }

    internal static async Task<InvoiceChargeDefinition?> ReadOneAsync(SqlConnection connection,
        SqlTransaction transaction, Guid tenantId, Guid businessId, Guid chargeId, CancellationToken ct)
    {
        await using var command = new SqlCommand(ReadSql, connection, transaction);
        AddScope(command, new(tenantId, businessId, Guid.Empty, new HashSet<string>()));
        AddPage(command, 1, 1, null, false, chargeId);
        return (await ReadAsync(command, 1, 1, ct)).Items.SingleOrDefault();
    }

    internal static async Task ValidateIssuedAsync(SqlConnection connection, SqlTransaction transaction,
        PosSaleUploadRequest sale, CancellationToken ct)
    {
        if (sale.Charges is not { Count: > 0 } charges) return;
        var productTotal = sale.Lines.Sum(line => line.LineTotal);
        InvoiceChargeApplication.ValidateSnapshot(productTotal, charges);
        await using var command = new SqlCommand(ReadSql, connection, transaction);
        AddScope(command, new(sale.TenantId, sale.BusinessId, Guid.Empty, new HashSet<string>()));
        AddPage(command, 1, InvoiceChargeApplication.MaximumChargesPerInvoice, null, true, null,
            requestedVersions: JsonSerializer.Serialize(charges
                .Select(charge => new { charge.ChargeId, charge.Version })
                .Distinct()));
        var definitions = (await ReadAsync(command, 1, InvoiceChargeApplication.MaximumChargesPerInvoice, ct)).Items;
        try { InvoiceChargeApplication.ValidateSnapshotReferences(productTotal, charges, definitions); }
        catch (InvoiceChargeValidationException error) { throw new PosSaleInvalidException(error.Message); }
    }

    public async Task<InvoiceChargeDefinition> SaveAsync(InvoiceChargeActor actor,
        SaveInvoiceChargeRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51700,N'La sede no pertenece al tenant.',1;
                DECLARE @ExistingVersion bigint,@ExistingCode nvarchar(32);
                SELECT @ExistingVersion=CurrentVersion,@ExistingCode=Code
                  FROM sales.InvoiceChargeDefinitions WITH(UPDLOCK,HOLDLOCK)
                  WHERE ChargeId=@ChargeId AND BusinessId=@BusinessId;
                IF COALESCE(@ExistingVersion,0)<>@ExpectedVersion
                  THROW 51701,N'El cargo cambió; vuelve a abrirlo antes de guardar.',1;
                IF @ExistingCode IS NOT NULL AND @ExistingCode<>@Code
                  THROW 51702,N'El código del cargo es inmutable.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.ExpenseConcepts c
                  JOIN dbo.AccountingAccounts a ON a.AccountId=c.ExpenseAccountId AND a.TenantId=@TenantId
                  WHERE c.ExpenseConceptId=@ConceptId AND c.BusinessId=@BusinessId
                    AND (@Active=0 OR (c.IsActive=1 AND a.IsActive=1 AND a.AllowsPosting=1
                      AND (c.DefaultCostCenterId IS NULL OR EXISTS(SELECT 1 FROM dbo.AccountingCostCenters cc
                        WHERE cc.CostCenterId=c.DefaultCostCenterId AND cc.BusinessId=@BusinessId AND cc.IsActive=1)))))
                  THROW 51702,N'Selecciona un concepto de gasto activo de esta sede.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@TaxId AND BusinessId=@BusinessId
                  AND (@Active=0 OR IsActive=1))
                  THROW 51702,N'Selecciona un impuesto activo de esta sede.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles WHERE TaxProfileId=@PurchaseTaxId AND BusinessId=@BusinessId
                  AND (@Active=0 OR IsActive=1))
                  THROW 51702,N'Selecciona el impuesto del costo del proveedor.',1;
                IF EXISTS(SELECT 1 FROM OPENJSON(@SuppliersJson) WITH(SupplierId uniqueidentifier '$') x
                  LEFT JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId AND s.BusinessId=@BusinessId
                  LEFT JOIN dbo.Parties p ON p.PartyId=s.PartyId AND p.TenantId=@TenantId
                  WHERE s.SupplierId IS NULL OR p.PartyId IS NULL OR (@Active=1 AND (s.IsActive=0 OR p.IsActive=0)))
                  THROW 51702,N'Los proveedores deben estar activos y pertenecer a esta sede.',1;
                IF NOT EXISTS(SELECT 1 FROM reference.Options WHERE CatalogCode=N'invoice-charge-calculation' AND Code=@Mode AND IsActive=1)
                  OR NOT EXISTS(SELECT 1 FROM reference.Options WHERE CatalogCode=N'invoice-charge-inclusion' AND Code=@InclusionMode AND IsActive=1)
                  OR EXISTS(SELECT 1 FROM OPENJSON(@RangesJson) WITH(Mode nvarchar(64) '$.CalculationMode') x
                    WHERE NOT EXISTS(SELECT 1 FROM reference.Options o WHERE o.CatalogCode=N'invoice-charge-calculation' AND o.Code=x.Mode AND o.IsActive=1))
                  THROW 51702,N'La modalidad elegida no está activa.',1;
                DECLARE @NewVersion bigint=@ExpectedVersion+1;
                DECLARE @NewCursor bigint;
                SELECT @NewCursor=COALESCE(MAX(AvailableThroughCursor),0)+1
                  FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                  WHERE BusinessId=@BusinessId AND Stream=N'Configuration';
                IF @ExistingVersion IS NULL
                  INSERT sales.InvoiceChargeDefinitions(ChargeId,BusinessId,Code,CurrentVersion)
                    VALUES(@ChargeId,@BusinessId,@Code,@NewVersion);
                ELSE UPDATE sales.InvoiceChargeDefinitions SET CurrentVersion=@NewVersion WHERE ChargeId=@ChargeId AND BusinessId=@BusinessId;
                INSERT sales.InvoiceChargeVersions(ChargeId,Version,BusinessId,Name,IsActive,SortOrder,
                  CalculationMode,Value,InclusionMode,InvoiceAmountLimit,ExpenseConceptId,SalesTaxProfileId,PurchaseTaxProfileId,
                  ExpenseAccountId,CostCenterId,WithholdingConceptCode,SalesTaxCode,SalesTaxRate,PurchaseTaxRate,
                  CreatedAt,CreatedBy,SynchronizationCursor)
                SELECT @ChargeId,@NewVersion,@BusinessId,@Name,@Active,@SortOrder,@Mode,@Value,@InclusionMode,@InvoiceAmountLimit,
                  @ConceptId,@TaxId,@PurchaseTaxId,c.ExpenseAccountId,c.DefaultCostCenterId,c.WithholdingConceptCode,
                  t.DianTaxCode,t.Rate,pt.Rate,@Now,@UserId,@NewCursor
                FROM dbo.ExpenseConcepts c
                JOIN dbo.TaxProfiles t ON t.TaxProfileId=@TaxId AND t.BusinessId=c.BusinessId
                JOIN dbo.TaxProfiles pt ON pt.TaxProfileId=@PurchaseTaxId AND pt.BusinessId=c.BusinessId
                WHERE c.ExpenseConceptId=@ConceptId AND c.BusinessId=@BusinessId;
                INSERT sales.InvoiceChargeRanges(ChargeId,Version,Position,FromInclusive,ToExclusive,CalculationMode,Value)
                SELECT @ChargeId,@NewVersion,CONVERT(int,j.[key])+1,r.FromInclusive,r.ToExclusive,r.CalculationMode,r.Value
                FROM OPENJSON(@RangesJson) j CROSS APPLY OPENJSON(j.value) WITH
                  (FromInclusive decimal(19,2),ToExclusive decimal(19,2),CalculationMode nvarchar(64),Value decimal(19,6)) r;
                INSERT sales.InvoiceChargeSuppliers(ChargeId,Version,SupplierId,Name,Identification,
                  DefaultPaymentDueDays,IsActive,AppliesWithholding,TaxResponsibilitiesJson,TaxJurisdictionCode,PurchaseEvidencePolicy)
                SELECT @ChargeId,@NewVersion,s.SupplierId,p.DisplayName,p.Identification,s.DefaultPaymentDueDays,
                  CONVERT(bit,CASE WHEN s.IsActive=1 AND p.IsActive=1 THEN 1 ELSE 0 END),
                  COALESCE(t.AppliesWithholding,CONVERT(bit,0)),t.Responsibilities,t.JurisdictionCode,s.PurchaseEvidencePolicy
                FROM OPENJSON(@SuppliersJson) WITH(SupplierId uniqueidentifier '$') input
                JOIN dbo.Suppliers s ON s.SupplierId=input.SupplierId AND s.BusinessId=@BusinessId
                JOIN dbo.Parties p ON p.PartyId=s.PartyId AND p.TenantId=@TenantId
                LEFT JOIN dbo.CounterpartyTaxProfiles t ON t.CounterpartyId=s.SupplierId AND t.BusinessId=s.BusinessId;
                INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                VALUES(NEWID(),@BusinessId,N'Configuration',@NewCursor,@Now);
                """ + ReadSql, connection, transaction);
            AddScope(command, actor);
            AddPage(command, 1, 1, null, true, request.ChargeId);
            command.Parameters.AddWithValue("@ChargeId", request.ChargeId);
            command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedVersion);
            command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name);
            command.Parameters.AddWithValue("@Active", request.IsActive);
            command.Parameters.AddWithValue("@SortOrder", request.SortOrder);
            command.Parameters.AddWithValue("@Mode", request.CalculationMode);
            AddDecimal(command, "@Value", request.Value, 6);
            command.Parameters.AddWithValue("@InclusionMode", request.InclusionMode);
            AddDecimal(command, "@InvoiceAmountLimit", request.InvoiceAmountLimit, 2);
            command.Parameters.AddWithValue("@ConceptId", request.ExpenseConceptId);
            command.Parameters.AddWithValue("@TaxId", request.SalesTaxProfileId);
            command.Parameters.AddWithValue("@PurchaseTaxId", request.PurchaseTaxProfileId);
            command.Parameters.AddWithValue("@Now", time.GetUtcNow());
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.AddWithValue("@RangesJson", JsonSerializer.Serialize(request.Ranges));
            command.Parameters.AddWithValue("@SuppliersJson", JsonSerializer.Serialize(request.SupplierIds));
            var result = await ReadAsync(command, 1, 1, ct);
            var saved = result.Items.Single();
            await transaction.CommitAsync(ct);
            return saved;
        }
        catch (SqlException error) when (error.Number == 51700)
        { throw new InvoiceChargeForbiddenException(error.Message); }
        catch (SqlException error) when (error.Number is 51701 or 2601 or 2627)
        { throw new InvoiceChargeConflictException("El cargo cambió o su código ya existe. Vuelve a abrirlo antes de guardar."); }
        catch (SqlException error) when (error.Number == 51702)
        { throw new InvoiceChargeValidationException(error.Message); }
    }

    private static async Task<InvoiceChargePage> ReadAsync(SqlCommand command, int page, int pageSize, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<InvoiceChargeDefinition>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3),
                reader.GetString(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10), reader.GetGuid(11), reader.GetString(12),
                reader.GetGuid(13), reader.GetString(14), reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16), reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.GetGuid(18), reader.GetString(19), reader.GetString(20), reader.GetDecimal(21), [], [],
                reader.GetGuid(22), reader.GetString(23), reader.GetDecimal(24), reader.IsDBNull(25) ? null : reader.GetString(25)));
        var ranges = items.ToDictionary(x => x.ChargeId, _ => new List<InvoiceChargeTariff>());
        var suppliers = items.ToDictionary(x => x.ChargeId, _ => new List<InvoiceChargeSupplier>());
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            ranges[reader.GetGuid(0)].Add(new(reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.GetString(3), reader.GetDecimal(4)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            suppliers[reader.GetGuid(0)].Add(new(reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4), reader.GetBoolean(5),
                reader.GetBoolean(6), reader.IsDBNull(7) ? [] : JsonSerializer.Deserialize<string[]>(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return new(items.Select(x => x with { Ranges = ranges[x.ChargeId], Suppliers = suppliers[x.ChargeId] }).ToArray(),
            page, pageSize, count, (int)Math.Ceiling(count / (double)pageSize));
    }

    private static void AddScope(SqlCommand command, InvoiceChargeActor actor)
    {
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
    }

    private static void AddPage(SqlCommand command, int page, int pageSize, string? search, bool includeInactive, Guid? id, long? throughCursor = null, string? requestedVersions = null)
    {
        command.Parameters.Add("@RequestedVersions", SqlDbType.NVarChar, -1).Value = (object?)requestedVersions ?? DBNull.Value;
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 120).Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search;
        command.Parameters.AddWithValue("@IncludeInactive", includeInactive);
        command.Parameters.Add("@OnlyId", SqlDbType.UniqueIdentifier).Value = (object?)id ?? DBNull.Value;
        command.Parameters.Add("@ThroughCursor", SqlDbType.BigInt).Value = (object?)throughCursor ?? DBNull.Value;
    }

    private static void AddDecimal(SqlCommand command, string name, decimal? value, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19;
        parameter.Scale = scale;
        parameter.Value = (object?)value ?? DBNull.Value;
    }
}
