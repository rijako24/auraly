using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;
using Auraly.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Auraly.Contracts.Tenants;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantProvisioningStore(
    ApplicationDbContext db,
    IAuralyIdGenerator ids) : ITenantProvisioningStore
{
    public async Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var previous = await FindReceiptAsync(connection, transaction, request.ProvisioningRequestId, cancellationToken);
            if (previous is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return previous;
            }

            var tenantId = ids.NewId();
            var tenantKey = await ResolveTenantKeyAsync(
                connection, transaction, request.TradeName, request.Nit,
                tenantId, cancellationToken);
            var businessId = ids.NewId();
            var salesWarehouseId = ids.NewId();
            var ordersWarehouseId = ids.NewId();
            var consumerPartyId = ids.NewId();
            var customerId = ids.NewId();
            var cashierRoleId = ids.NewId();
            var supervisorRoleId = ids.NewId();
            var sellerRoleId = ids.NewId();
            var administrativeRoleId = ids.NewId();
            var adminRoleId = ids.NewId();
            var invitationId = ids.NewId();
            var outboxId = ids.NewId();
            var now = DateTimeOffset.UtcNow;
            var activationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var activationHash = SHA256.HashData(Encoding.UTF8.GetBytes(activationToken));

            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE CountryId=@CountryId AND IsActive=1)
                    THROW 51021,'El país seleccionado no existe o está inactivo.',1;
                IF NOT EXISTS (SELECT 1 FROM dbo.AdministrativeDivisions WHERE AdministrativeDivisionId=@DivisionId AND CountryId=@CountryId AND IsActive=1)
                    THROW 51021,'El departamento no pertenece al país seleccionado.',1;
                IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE CityId=@CityId AND AdministrativeDivisionId=@DivisionId AND IsActive=1)
                    THROW 51021,'La ciudad no pertenece al departamento seleccionado.',1;
                IF EXISTS (SELECT 1 FROM dbo.TenantLegalProfiles WHERE NormalizedNit=@NormalizedNit)
                    THROW 51022,'Ya existe una empresa con este NIT.',1;
                INSERT dbo.Tenants(TenantId,TenantKey,Name,Email,IsActive,MaximumUsers,MaximumEnrolledDevices,CreatedAt)
                VALUES(@TenantId,@TenantKey,@TradeName,@CompanyEmail,1,@MaximumUsers,@MaximumEnrolledDevices,@Now);

                INSERT dbo.Businesses
                  (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone,IsActive,CreatedAt)
                VALUES(@BusinessId,@TenantId,@BusinessName,N'Sede principal',@BusinessAddress,@BusinessPhone,@BusinessEmail,N'',@TimeZone,1,@Now);

                INSERT dbo.TenantLegalProfiles
                  (TenantId,LegalName,TradeName,Nit,NormalizedNit,VerificationDigit,CountryId,AdministrativeDivisionId,CityId,Address,Phone,Email,TaxResponsibilities,PrimaryBusinessId,CreatedAt)
                VALUES
                  (@TenantId,@LegalName,@TradeName,@Nit,@NormalizedNit,@VerificationDigit,@CountryId,@DivisionId,@CityId,@CompanyAddress,@CompanyPhone,@CompanyEmail,@TaxResponsibilities,@BusinessId,@Now);

                INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsActive,CreatedAt)
                VALUES
                  (@SalesWarehouseId,@BusinessId,N'VEN',N'Bodega de venta',0,@CostBasis,1,@Now),
                  (@OrdersWarehouseId,@BusinessId,N'PED',N'Bodega de pedidos',0,@CostBasis,1,@Now);

                DECLARE @DocumentSeries TABLE(DocumentType nvarchar(64),Prefix nvarchar(8));
                INSERT @DocumentSeries VALUES
                  (N'GoodsReceipt',N'EMC'),(N'StockCount',N'CTI'),
                  (N'InventoryAdjustment',N'AJI'),(N'WarehouseTransfer',N'TRB'),
                  (N'ProductConversion',N'CNV'),(N'Damage',N'AVE');
                INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                SELECT NEWID(),@BusinessId,NULL,DocumentType,Prefix,N'00',8,1,99999999,0,1,@Now FROM @DocumentSeries;

                DECLARE @Reasons TABLE(
                    OperationType nvarchar(64),Code nvarchar(40),Name nvarchar(120),DisplayOrder int);
                INSERT @Reasons VALUES
                  (N'StockCount',N'PHYSICAL_COUNT',N'Conteo físico programado',10),
                  (N'StockCount',N'INVENTORY_VERIFICATION',N'Verificación de existencias',20),
                  (N'InventoryAdjustment',N'MANUAL_ADJUSTMENT',N'Corrección de saldo',10),
                  (N'InventoryAdjustment',N'INITIAL_BALANCE',N'Saldo inicial',20),
                  (N'InventoryAdjustment',N'FOUND_SURPLUS',N'Sobrante identificado',30),
                  (N'InventoryAdjustment',N'FOUND_SHORTAGE',N'Faltante identificado',40),
                  (N'WarehouseTransfer',N'WAREHOUSE_TRANSFER',N'Reabastecimiento entre bodegas',10),
                  (N'WarehouseTransfer',N'STOCK_REDISTRIBUTION',N'Redistribución de existencias',20),
                  (N'ProductConversion',N'PRESENTATION_CHANGE',N'Cambio de presentación',10),
                  (N'Damage',N'DAMAGE',N'Producto averiado',10),
                  (N'Damage',N'EXPIRED',N'Producto vencido',20),
                  (N'Damage',N'NOT_SALEABLE',N'Producto no vendible',30);
                INSERT dbo.InventoryReasons(
                    InventoryReasonId,BusinessId,OperationType,Code,Name,
                    IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
                SELECT NEWID(),@BusinessId,OperationType,Code,Name,1,1,DisplayOrder,@Now,@Now
                FROM @Reasons;

                INSERT dbo.ProductUnits(
                    ProductUnitId,BusinessId,Code,Name,Symbol,
                    AllowsFractionalQuantity,DecimalPlaces,IsActive,CreatedAt)
                VALUES
                  (NEWID(),@BusinessId,N'EA',N'Unidad',N'und',0,0,1,@Now),
                  (NEWID(),@BusinessId,N'KG',N'Kilogramo',N'kg',1,3,1,@Now),
                  (NEWID(),@BusinessId,N'M',N'Metro',N'm',1,3,1,@Now),
                  (NEWID(),@BusinessId,N'L',N'Litro',N'L',1,3,1,@Now);

                INSERT dbo.Parties
                  (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,Identification,NormalizedIdentification,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@ConsumerPartyId,@TenantId,N'Organization',@CountryId,N'CC',N'222222222222',N'222222222222',N'Consumidor final',N'Consumidor final',N'Complete',1,@ActorUserId,@Now);
                INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@ConsumerPartyId,@BusinessId,1,@ActorUserId,@Now);


                INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
                VALUES
                  (@CashierRoleId,@TenantId,N'Cajero',N'CASHIER',N'Operación de venta cotidiana sin acciones sensibles.',1,0,@Now),
                  (@SupervisorRoleId,@TenantId,N'Supervisor',N'SUPERVISOR',N'Supervisión operativa y autorización de acciones sensibles.',1,0,@Now),
                  (@SellerRoleId,@TenantId,N'Vendedor',N'SELLER',N'Toma de pedidos y ejecución de rutas comerciales.',1,0,@Now),
                  (@AdministrativeRoleId,@TenantId,N'Administrativo',N'ADMINISTRATIVE',N'Administración comercial y operativa del tenant.',1,0,@Now),
                  (@AdminRoleId,@TenantId,N'Administrador',N'ADMINISTRATOR',N'Administración completa de la empresa y todas sus sedes.',1,1,@Now);
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@AdminRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource NOT LIKE N'tenants.%';
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@AdministrativeRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource NOT LIKE N'tenants.%'
                  AND Resource NOT LIKE N'roles.%'
                  AND Resource NOT LIKE N'users.%'
                  AND Resource NOT LIKE N'audit[_]logs.%';
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@SupervisorRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'sales.create',N'sales.discount',N'sales.reprint',N'sales.lines.remove',N'sales.drafts.restart',
                  N'pos.approvals.authorize',N'pos.approvals.read',N'pos.approvals.manage_credential',
                  N'pos.customer.create',N'pos.orders',N'orders.read',N'orders.invoice',
                  N'sales.returns.read',N'sales.returns.create',N'sales.returns.confirm',
                  N'work_sessions.read',N'work_sessions.close',N'inventory.read');
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@SellerRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'orders.read',N'orders.create',N'orders.update',N'routes.read',N'routes.visits.record',
                  N'customers.read',N'parties.read',N'inventory.read');
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@CashierRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'sales.create',N'sales.reprint',N'pos.customer.create',N'pos.orders',N'orders.read');

                INSERT dbo.TenantUserInvitations
                  (InvitationId,TenantId,UserId,DeliveryEmail,TokenHash,ExpiresAt,Status,CreatedAt)
                VALUES(@InvitationId,@TenantId,NULL,@InvitationEmail,@ActivationHash,DATEADD(day,2,@Now),N'Pending',@Now);
                INSERT dbo.TenantProvisioningOutboxMessages
                  (MessageId,TenantId,Type,Payload,OccurredAt,AttemptCount)
                VALUES(@OutboxId,@TenantId,N'TenantAdministratorInvitation',@InvitationPayload,@Now,0);

                INSERT dbo.TenantProvisioningRequests
                  (ProvisioningRequestId,TenantId,BusinessId,SalesWarehouseId,OrdersWarehouseId,DefaultCustomerId,AdministratorUserId,Status,CreatedAt,CompletedAt)
                VALUES(@RequestId,@TenantId,@BusinessId,@SalesWarehouseId,@OrdersWarehouseId,@CustomerId,NULL,N'Completed',@Now,@Now);

                INSERT dbo.AuditLogs(AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,EntityId,NewValues,Timestamp)
                VALUES(@AuditLogId,@ActorUserId,@TenantId,@BusinessId,N'TenantProvisioned',N'Tenant',CONVERT(nvarchar(100),@TenantId),@AuditPayload,@Now);
                """;

            await using var command = new SqlCommand(sql, connection, transaction);
            Add("@TenantKey", tenantKey);
            void Add(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            Add("@RequestId", request.ProvisioningRequestId);
            Add("@TenantId", tenantId); Add("@BusinessId", businessId);
            Add("@SalesWarehouseId", salesWarehouseId); Add("@OrdersWarehouseId", ordersWarehouseId);
            Add("@ConsumerPartyId", consumerPartyId); Add("@CustomerId", customerId);
            Add("@CashierRoleId", cashierRoleId); Add("@SupervisorRoleId", supervisorRoleId); Add("@SellerRoleId", sellerRoleId);
            Add("@AdministrativeRoleId", administrativeRoleId); Add("@AdminRoleId", adminRoleId);
            Add("@InvitationId", invitationId); Add("@OutboxId", outboxId); Add("@AuditLogId", ids.NewId());
            Add("@ActorUserId", actorUserId); Add("@Now", now);
            Add("@LegalName", request.LegalName); Add("@TradeName", request.TradeName);
            Add("@Nit", request.Nit); Add("@NormalizedNit", NormalizeDigits(request.Nit)); Add("@VerificationDigit", request.VerificationDigit);
            Add("@CountryId", request.CountryId); Add("@DivisionId", request.AdministrativeDivisionId); Add("@CityId", request.CityId);
            Add("@CompanyAddress", request.Address); Add("@CompanyPhone", request.Phone); Add("@CompanyEmail", request.Email.Trim()); Add("@TaxResponsibilities", request.TaxResponsibilities);
            Add("@BusinessName", request.BusinessName); Add("@BusinessAddress", request.BusinessAddress); Add("@BusinessPhone", request.BusinessPhone); Add("@BusinessEmail", request.BusinessEmail.Trim());
            Add("@TimeZone", request.TimeZone); Add("@CostBasis", request.InventoryCostBasis);
            Add("@InvitationEmail", request.InvitationEmail.Trim()); Add("@ActivationHash", activationHash);
            Add("@MaximumUsers", request.MaximumUsers); Add("@MaximumEnrolledDevices", request.MaximumEnrolledDevices);
            Add("@InvitationPayload", JsonSerializer.Serialize(new { invitationId, tenantId, email = request.InvitationEmail.Trim(), activationToken }));
            Add("@AuditPayload", JsonSerializer.Serialize(new { request.ProvisioningRequestId, businessId, salesWarehouseId, ordersWarehouseId, request.MaximumUsers, request.MaximumEnrolledDevices }));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(request.ProvisioningRequestId, tenantId, businessId, tenantKey,
                salesWarehouseId, ordersWarehouseId, customerId, null, "Completed");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AcceptTenantInvitationResult> AcceptInvitationAsync(
        byte[] tokenHash,
        TenantInvitationAdministratorProfile profile,
        TenantInvitationPasswordMaterial password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @InvitationId uniqueidentifier,@TenantId uniqueidentifier,
                        @Status nvarchar(16),@ExpiresAt datetimeoffset(7),
                        @CountryId uniqueidentifier,@DivisionId uniqueidentifier,
                        @CityId uniqueidentifier,@BusinessId uniqueidentifier,
                        @AdminRoleId uniqueidentifier;
                SELECT @InvitationId=i.InvitationId,@TenantId=i.TenantId,
                       @Status=i.Status,@ExpiresAt=i.ExpiresAt,
                       @CountryId=p.CountryId,@DivisionId=p.AdministrativeDivisionId,
                       @CityId=p.CityId,@BusinessId=p.PrimaryBusinessId
                FROM dbo.TenantUserInvitations i WITH (UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.TenantLegalProfiles p ON p.TenantId=i.TenantId
                WHERE i.TokenHash=@TokenHash;
                IF @InvitationId IS NULL THROW 51031,'La invitación no existe.',1;
                IF @Status<>N'Pending' THROW 51032,'La invitación ya no está disponible.',1;
                IF @ExpiresAt<=@Now
                BEGIN
                    UPDATE dbo.TenantUserInvitations SET Status=N'Expired' WHERE InvitationId=@InvitationId;
                    THROW 51033,'La invitación expiró.',1;
                END;
                IF EXISTS(SELECT 1 FROM dbo.AppUsers WITH(UPDLOCK,HOLDLOCK)
                          WHERE NormalizedEmail=@NormalizedEmail OR NormalizedUsername=@NormalizedEmail)
                    THROW 51034,'Ya existe un usuario con este correo.',1;
                IF EXISTS(SELECT 1 FROM dbo.Parties WITH(UPDLOCK,HOLDLOCK)
                          WHERE TenantId=@TenantId AND IdentificationCountryId=@CountryId
                            AND IdentificationTypeCode=@IdentificationType
                            AND NormalizedIdentification=@NormalizedIdentification)
                    THROW 51034,'Ya existe un tercero con esta identificación en la organización.',1;
                SELECT @AdminRoleId=RoleId FROM dbo.AppRoles WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND NormalizedName=N'ADMINISTRATOR' AND IsActive=1;
                IF @AdminRoleId IS NULL THROW 51034,'El rol Administrador no está configurado.',1;

                INSERT dbo.Parties
                  (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                   Identification,NormalizedIdentification,DisplayName,FirstName,LastName,
                   CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,N'NaturalPerson',@CountryId,@IdentificationType,
                       @Identification,@NormalizedIdentification,@DisplayName,@FirstName,@LastName,
                       N'Complete',1,@UserId,@Now);
                INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
                VALUES
                  (@EmailContactId,@PartyId,N'Email',@Email,@NormalizedEmail,1,1,@Now),
                  (@PhoneContactId,@PartyId,N'Phone',@Phone,@NormalizedPhone,1,1,@Now);
                INSERT dbo.AppUsers
                  (UserId,TenantId,PartyId,Username,NormalizedUsername,Email,NormalizedEmail,
                   PasswordHash,PosOfflinePasswordSalt,PosOfflinePasswordHash,PosOfflinePasswordIterations,
                   PosOfflinePasswordChangedAt,FirstName,LastName,PhoneNumber,
                   EmailConfirmed,IsActive,CreatedAt)
                VALUES(@UserId,@TenantId,@PartyId,@Email,@NormalizedEmail,@Email,@NormalizedEmail,
                       @PasswordHash,@OfflineSalt,@OfflineHash,@OfflineIterations,@ChangedAt,
                       @FirstName,@LastName,@Phone,1,1,@Now);
                INSERT dbo.PartySites
                  (PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
                   AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                VALUES(@SiteId,@PartyId,N'PRINCIPAL',N'Principal',@CountryId,@DivisionId,@CityId,
                       @Address,1,1,@UserId,@Now);
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt,AssignedByUserId)
                VALUES(@UserRoleId,@UserId,@AdminRoleId,NULL,@Now,@UserId);
                UPDATE dbo.TenantUserInvitations
                SET UserId=@UserId,Status=N'Accepted',AcceptedAt=@Now
                WHERE InvitationId=@InvitationId AND Status=N'Pending';
                UPDATE dbo.TenantProvisioningRequests
                SET AdministratorUserId=@UserId
                WHERE TenantId=@TenantId AND AdministratorUserId IS NULL;
                INSERT dbo.AuditLogs(AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,EntityId,Timestamp)
                VALUES(@AuditLogId,@UserId,@TenantId,@BusinessId,N'TenantInvitationAccepted',N'AppUser',CONVERT(nvarchar(100),@UserId),@Now);
                SELECT @TenantId,@UserId,@Email,N'Accepted';
                """, connection, transaction);
            command.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            command.Parameters.AddWithValue("@PasswordHash", password.PasswordHash);
            command.Parameters.Add("@OfflineSalt", SqlDbType.VarBinary, 16).Value = password.OfflineSalt;
            command.Parameters.Add("@OfflineHash", SqlDbType.VarBinary, 32).Value = password.OfflineHash;
            command.Parameters.AddWithValue("@OfflineIterations", password.OfflineIterations);
            command.Parameters.AddWithValue("@ChangedAt", password.ChangedAt);
            command.Parameters.AddWithValue("@Now", now);
            command.Parameters.AddWithValue("@AuditLogId", ids.NewId());
            command.Parameters.AddWithValue("@UserId", ids.NewId());
            command.Parameters.AddWithValue("@PartyId", ids.NewId());
            command.Parameters.AddWithValue("@EmailContactId", ids.NewId());
            command.Parameters.AddWithValue("@PhoneContactId", ids.NewId());
            command.Parameters.AddWithValue("@SiteId", ids.NewId());
            command.Parameters.AddWithValue("@UserRoleId", ids.NewId());
            command.Parameters.AddWithValue("@IdentificationType", profile.IdentificationType.ToUpperInvariant());
            command.Parameters.AddWithValue("@Identification", profile.Identification);
            command.Parameters.AddWithValue("@NormalizedIdentification", NormalizeIdentity(profile.Identification));
            command.Parameters.AddWithValue("@FirstName", profile.FirstName);
            command.Parameters.AddWithValue("@LastName", profile.LastName);
            command.Parameters.AddWithValue("@DisplayName", $"{profile.FirstName} {profile.LastName}".Trim());
            command.Parameters.AddWithValue("@Email", profile.Email);
            command.Parameters.AddWithValue("@NormalizedEmail", profile.Email.ToUpperInvariant());
            command.Parameters.AddWithValue("@Phone", profile.Phone);
            command.Parameters.AddWithValue("@NormalizedPhone", NormalizeDigits(profile.Phone));
            command.Parameters.AddWithValue("@Address", profile.Address);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("La activación no devolvió un recibo.");
            var result = new AcceptTenantInvitationResult(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ProvisionTenantResult?> FindReceiptAsync(SqlConnection connection, SqlTransaction transaction, Guid requestId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT r.ProvisioningRequestId,r.TenantId,t.TenantKey,r.BusinessId,
                   r.SalesWarehouseId,r.OrdersWarehouseId,r.DefaultCustomerId,
                   r.AdministratorUserId,r.Status
            FROM dbo.TenantProvisioningRequests r
            INNER JOIN dbo.Tenants t ON t.TenantId=r.TenantId
            WHERE r.ProvisioningRequestId=@RequestId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@RequestId", requestId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(3), reader.GetString(2),
                reader.GetGuid(4), reader.GetGuid(5), reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetString(8))
            : null;
    }
    private static async Task<string> ResolveTenantKeyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tradeName,
        string nit,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var baseKey = TenantKey.FromName(tradeName).Value;
        var suffix = NormalizeDigits(nit);
        suffix = suffix.Length > 4 ? suffix[^4..] : suffix;
        var candidates = new[]
        {
            baseKey,
            TenantKey.Parse($"{baseKey}-{suffix}").Value,
            TenantKey.Parse($"{baseKey}-{tenantId:N}"[..Math.Min(
                TenantKey.MaximumLength, baseKey.Length + 9)]).Value
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            await using var command = new SqlCommand("""
                SELECT COUNT_BIG(1)
                FROM dbo.Tenants WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantKey=@TenantKey;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantKey", candidate);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0)
                return candidate;
        }
        throw new InvalidOperationException(
            "No fue posible generar una clave Ãºnica para la empresa.");
    }


    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string NormalizeIdentity(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

