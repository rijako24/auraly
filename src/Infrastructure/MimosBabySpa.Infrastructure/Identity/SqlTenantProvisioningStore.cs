using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Auraly.Contracts.Tenants;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Identity;

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
            var businessId = ids.NewId();
            var salesWarehouseId = ids.NewId();
            var ordersWarehouseId = ids.NewId();
            var consumerPartyId = ids.NewId();
            var customerId = ids.NewId();
            var adminPartyId = ids.NewId();
            var adminUserId = ids.NewId();
            var cashierRoleId = ids.NewId();
            var supervisorRoleId = ids.NewId();
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
                IF EXISTS (SELECT 1 FROM dbo.AppUsers WHERE NormalizedEmail=@NormalizedAdminEmail OR NormalizedUsername=@NormalizedAdminEmail)
                    THROW 51022,'Ya existe un usuario con el correo del administrador.',1;

                INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
                VALUES(@TenantId,@TradeName,@CompanyEmail,1,@Now);

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
                  (PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@ConsumerPartyId,@TenantId,N'Organization',N'Consumidor final',N'Consumidor final',N'Incomplete',1,@ActorUserId,@Now);
                INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerId,@ConsumerPartyId,@BusinessId,1,@ActorUserId,@Now);

                INSERT dbo.Parties
                  (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,Identification,NormalizedIdentification,DisplayName,FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@AdminPartyId,@TenantId,N'NaturalPerson',@CountryId,@AdminIdentificationType,@AdminIdentification,@NormalizedAdminIdentification,@AdminDisplayName,@AdminFirstName,@AdminLastName,N'Complete',1,@ActorUserId,@Now);

                INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
                VALUES
                  (@AdminEmailContactId,@AdminPartyId,N'Email',@AdminEmail,@NormalizedAdminEmail,1,1,@Now),
                  (@AdminPhoneContactId,@AdminPartyId,N'Phone',@AdminPhone,@NormalizedAdminPhone,1,1,@Now);

                INSERT dbo.AppUsers
                  (UserId,TenantId,PartyId,CreatedByUserId,Username,NormalizedUsername,Email,NormalizedEmail,PasswordHash,FirstName,LastName,PhoneNumber,EmailConfirmed,IsActive,CreatedAt)
                VALUES(@AdminUserId,@TenantId,@AdminPartyId,@ActorUserId,@AdminEmail,@NormalizedAdminEmail,@AdminEmail,@NormalizedAdminEmail,NULL,@AdminFirstName,@AdminLastName,@AdminPhone,0,0,@Now);

                INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
                VALUES
                  (@CashierRoleId,@TenantId,N'Cajero',N'CASHIER',N'Operación de venta cotidiana sin acciones sensibles.',1,0,@Now),
                  (@SupervisorRoleId,@TenantId,N'Supervisor',N'SUPERVISOR',N'Supervisión operativa y autorización de acciones sensibles.',1,0,@Now),
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
                SELECT NEWID(),@CashierRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'sales.create',N'sales.reprint',N'pos.customer.create',N'pos.orders',N'orders.read');
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt,AssignedByUserId)
                VALUES(@UserRoleId,@AdminUserId,@AdminRoleId,NULL,@Now,@ActorUserId);

                INSERT dbo.TenantUserInvitations
                  (InvitationId,TenantId,UserId,TokenHash,ExpiresAt,Status,CreatedAt)
                VALUES(@InvitationId,@TenantId,@AdminUserId,@ActivationHash,DATEADD(day,2,@Now),N'Pending',@Now);
                INSERT dbo.TenantProvisioningOutboxMessages
                  (MessageId,TenantId,Type,Payload,OccurredAt,AttemptCount)
                VALUES(@OutboxId,@TenantId,N'TenantAdministratorInvitation',@InvitationPayload,@Now,0);

                INSERT dbo.TenantProvisioningRequests
                  (ProvisioningRequestId,TenantId,BusinessId,SalesWarehouseId,OrdersWarehouseId,DefaultCustomerId,AdministratorUserId,Status,CreatedAt,CompletedAt)
                VALUES(@RequestId,@TenantId,@BusinessId,@SalesWarehouseId,@OrdersWarehouseId,@CustomerId,@AdminUserId,N'Completed',@Now,@Now);

                INSERT dbo.AuditLogs(AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,EntityId,NewValues,Timestamp)
                VALUES(@AuditLogId,@ActorUserId,@TenantId,@BusinessId,N'TenantProvisioned',N'Tenant',CONVERT(nvarchar(100),@TenantId),@AuditPayload,@Now);
                """;

            await using var command = new SqlCommand(sql, connection, transaction);
            void Add(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            Add("@RequestId", request.ProvisioningRequestId);
            Add("@TenantId", tenantId); Add("@BusinessId", businessId);
            Add("@SalesWarehouseId", salesWarehouseId); Add("@OrdersWarehouseId", ordersWarehouseId);
            Add("@ConsumerPartyId", consumerPartyId); Add("@CustomerId", customerId);
            Add("@AdminPartyId", adminPartyId); Add("@AdminUserId", adminUserId);
            Add("@CashierRoleId", cashierRoleId); Add("@SupervisorRoleId", supervisorRoleId);
            Add("@AdministrativeRoleId", administrativeRoleId); Add("@AdminRoleId", adminRoleId);
            Add("@InvitationId", invitationId); Add("@OutboxId", outboxId); Add("@AuditLogId", ids.NewId());
            Add("@AdminEmailContactId", ids.NewId()); Add("@AdminPhoneContactId", ids.NewId()); Add("@UserRoleId", ids.NewId());
            Add("@ActorUserId", actorUserId); Add("@Now", now);
            Add("@LegalName", request.LegalName); Add("@TradeName", request.TradeName);
            Add("@Nit", request.Nit); Add("@NormalizedNit", NormalizeDigits(request.Nit)); Add("@VerificationDigit", request.VerificationDigit);
            Add("@CountryId", request.CountryId); Add("@DivisionId", request.AdministrativeDivisionId); Add("@CityId", request.CityId);
            Add("@CompanyAddress", request.Address); Add("@CompanyPhone", request.Phone); Add("@CompanyEmail", request.Email.Trim()); Add("@TaxResponsibilities", request.TaxResponsibilities);
            Add("@BusinessName", request.BusinessName); Add("@BusinessAddress", request.BusinessAddress); Add("@BusinessPhone", request.BusinessPhone); Add("@BusinessEmail", request.BusinessEmail.Trim());
            Add("@TimeZone", request.TimeZone); Add("@CostBasis", request.InventoryCostBasis);
            Add("@AdminIdentificationType", request.AdministratorIdentificationType); Add("@AdminIdentification", request.AdministratorIdentification); Add("@NormalizedAdminIdentification", NormalizeDigits(request.AdministratorIdentification));
            Add("@AdminFirstName", request.AdministratorFirstName); Add("@AdminLastName", request.AdministratorLastName);
            Add("@AdminDisplayName", $"{request.AdministratorFirstName.Trim()} {request.AdministratorLastName.Trim()}".Trim());
            Add("@AdminEmail", request.AdministratorEmail.Trim()); Add("@NormalizedAdminEmail", request.AdministratorEmail.Trim().ToUpperInvariant());
            Add("@AdminPhone", request.AdministratorPhone); Add("@NormalizedAdminPhone", NormalizeDigits(request.AdministratorPhone)); Add("@ActivationHash", activationHash);
            Add("@InvitationPayload", JsonSerializer.Serialize(new { invitationId, tenantId, userId = adminUserId, email = request.AdministratorEmail.Trim(), activationToken }));
            Add("@AuditPayload", JsonSerializer.Serialize(new { request.ProvisioningRequestId, businessId, salesWarehouseId, ordersWarehouseId, administratorUserId = adminUserId }));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(request.ProvisioningRequestId, tenantId, businessId, salesWarehouseId, ordersWarehouseId, customerId, adminUserId, "Completed");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AcceptTenantInvitationResult> AcceptInvitationAsync(
        byte[] tokenHash,
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
                DECLARE @InvitationId uniqueidentifier,@TenantId uniqueidentifier,@UserId uniqueidentifier,
                        @Email nvarchar(256),@Status nvarchar(16),@ExpiresAt datetimeoffset(7);
                SELECT @InvitationId=i.InvitationId,@TenantId=i.TenantId,@UserId=i.UserId,
                       @Email=u.Email,@Status=i.Status,@ExpiresAt=i.ExpiresAt
                FROM dbo.TenantUserInvitations i WITH (UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.AppUsers u WITH (UPDLOCK,HOLDLOCK) ON u.UserId=i.UserId AND u.TenantId=i.TenantId
                WHERE i.TokenHash=@TokenHash;
                IF @InvitationId IS NULL THROW 51031,'La invitación no existe.',1;
                IF @Status<>N'Pending' THROW 51032,'La invitación ya no está disponible.',1;
                IF @ExpiresAt<=@Now
                BEGIN
                    UPDATE dbo.TenantUserInvitations SET Status=N'Expired' WHERE InvitationId=@InvitationId;
                    THROW 51033,'La invitación expiró.',1;
                END;
                UPDATE dbo.AppUsers
                SET PasswordHash=@PasswordHash,PosOfflinePasswordSalt=@OfflineSalt,
                    PosOfflinePasswordHash=@OfflineHash,PosOfflinePasswordIterations=@OfflineIterations,
                    PosOfflinePasswordChangedAt=@ChangedAt,EmailConfirmed=1,IsActive=1,
                    AccessFailedCount=0,LockoutEnd=NULL,UpdatedAt=@Now
                WHERE UserId=@UserId AND TenantId=@TenantId AND IsActive=0;
                IF @@ROWCOUNT<>1 THROW 51034,'El usuario invitado no puede activarse.',1;
                UPDATE dbo.TenantUserInvitations
                SET Status=N'Accepted',AcceptedAt=@Now WHERE InvitationId=@InvitationId AND Status=N'Pending';
                INSERT dbo.AuditLogs(AuditLogId,UserId,TenantId,Action,EntityType,EntityId,Timestamp)
                VALUES(@AuditLogId,@UserId,@TenantId,N'TenantInvitationAccepted',N'AppUser',CONVERT(nvarchar(100),@UserId),@Now);
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
            SELECT ProvisioningRequestId,TenantId,BusinessId,SalesWarehouseId,OrdersWarehouseId,DefaultCustomerId,AdministratorUserId,Status
            FROM dbo.TenantProvisioningRequests WHERE ProvisioningRequestId=@RequestId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@RequestId", requestId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetGuid(3),reader.GetGuid(4),reader.GetGuid(5),reader.GetGuid(6),reader.GetString(7))
            : null;
    }

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());
}

