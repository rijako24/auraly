using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.SqlClient;
using Auraly.BuildingBlocks.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Auraly.Contracts.Tenants;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantProvisioningStore(
    ApplicationDbContext db,
    IAuralyIdGenerator ids) : ITenantProvisioningStore
{
    public async Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid? actorUserId,
        TenantQuoteDto commercialQuote,
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
            var damagedWarehouseId = ids.NewId();
            var consumerPartyId = ids.NewId();
            var customerId = ids.NewId();
            var cashierRoleId = ids.NewId();
            var supervisorRoleId = ids.NewId();
            var sellerRoleId = ids.NewId();
            var administrativeRoleId = ids.NewId();
            var accountantRoleId = ids.NewId();
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
                INSERT dbo.Tenants(TenantId,TenantKey,Name,Email,IsActive,MaximumUsers,MaximumEnrolledDevices,InventoryCostBasis,CreatedAt)
                VALUES(@TenantId,@TenantKey,@TradeName,@CompanyEmail,1,@MaximumUsers,@MaximumEnrolledDevices,@CostBasis,@Now);

                INSERT dbo.Businesses
                  (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,TimeZone,IsActive,CreatedAt)
                VALUES(@BusinessId,@TenantId,@BusinessName,N'Sede principal',@BusinessAddress,@BusinessPhone,@BusinessEmail,N'',@TimeZone,1,@Now);

                INSERT dbo.TenantLegalProfiles
                  (TenantId,LegalName,TradeName,Nit,NormalizedNit,VerificationDigit,CountryId,AdministrativeDivisionId,CityId,Address,Phone,Email,TaxResponsibilities,PrimaryBusinessId,CreatedAt)
                VALUES
                  (@TenantId,@LegalName,@TradeName,@Nit,@NormalizedNit,@VerificationDigit,@CountryId,@DivisionId,@CityId,@CompanyAddress,@CompanyPhone,@CompanyEmail,@TaxResponsibilities,@BusinessId,@Now);

                INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,PriceFormationCostBasis,IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
                VALUES
                  (@SalesWarehouseId,@BusinessId,N'VEN',N'Bodega de venta',0,@CostBasis,0,1,1,1,1,@Now),
                  (@OrdersWarehouseId,@BusinessId,N'PED',N'Bodega de pedidos',0,@CostBasis,1,0,0,0,1,@Now),
                  (@DamagedWarehouseId,@BusinessId,N'AVE',N'Bodega de averías',0,@CostBasis,1,0,0,0,1,@Now),
                  (NEWID(),@BusinessId,N'TRA',N'Mercancía en tránsito',0,@CostBasis,1,0,0,0,1,@Now);

                DECLARE @DocumentSeries TABLE(DocumentType nvarchar(64),Prefix nvarchar(8));
                INSERT @DocumentSeries VALUES
                  (N'SalesInvoice',N'VTA'),(N'SalesReceipt',N'CVI'),(N'SalesDebitNote',N'NDB'),
                  (N'GoodsReceipt',N'EMC'),(N'StockCount',N'CTI'),
                  (N'InventoryAdjustment',N'AJI'),(N'WarehouseTransfer',N'TRB'),
                  (N'ProductConversion',N'CNV'),(N'Damage',N'AVE');
                INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                SELECT NEWID(),@BusinessId,NULL,DocumentType,Prefix,N'00',8,1,99999999,0,1,@Now FROM @DocumentSeries;

                INSERT dbo.BusinessReasons(
                    ReasonId,BusinessId,ReasonType,Code,Name,Direction,
                    CounterpartAccountingCategory,DefaultCostCenterId,RequiresReference,
                    IsSystem,IsActive,DisplayOrder,CreatedAt,UpdatedAt)
                SELECT NEWID(),@BusinessId,t.ReasonType,t.Code,t.Name,t.Direction,
                       t.CounterpartAccountingCategory,NULL,t.RequiresReference,
                       1,1,t.DisplayOrder,@Now,@Now
                FROM dbo.AccountingConfigurationProfiles p
                INNER JOIN dbo.ReasonTemplates t ON t.ProfileCode=p.ProfileCode
                WHERE p.IsDefault=1 AND p.IsActive=1 AND t.IsActive=1;

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
                  (@AccountantRoleId,@TenantId,N'Contador',N'ACCOUNTANT',N'Gestión contable, tributaria, de cartera, proveedores y nómina.',1,0,@Now),
                  (@AdminRoleId,@TenantId,N'Administrador',N'ADMINISTRATOR',N'Administración completa de la empresa y todas sus sedes.',1,1,@Now);
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@AdminRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource NOT LIKE N'tenants.%'
                  AND Resource NOT LIKE N'platform.%';
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@AdministrativeRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource NOT LIKE N'tenants.%'
                  AND Resource NOT LIKE N'roles.%'
                  AND Resource NOT LIKE N'users.%'
                  AND Resource NOT LIKE N'audit[_]logs.%';
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@AccountantRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource LIKE N'accounting.%'
                   OR Resource LIKE N'payroll.%'
                   OR Resource LIKE N'payables.%'
                   OR Resource LIKE N'receivables.%'
                   OR Resource LIKE N'expenses.%'
                   OR Resource LIKE N'commerce.taxation.%'
                   OR Resource LIKE N'fiscal.configuration.%'
                   OR Resource IN(
                     N'businesses.read',N'dashboard.read',N'audit_logs.read',N'payments.read',N'payments.confirm_manual',
                     N'parties.read',N'customers.read',N'suppliers.read',N'catalog.read',N'catalog.costs.read',N'products.read',
                     N'inventory.read',N'inventory.costs.read',N'inventory.reasons.manage',
                     N'work-sessions.read',N'work-sessions.differences.read',N'work-sessions.cash-reasons.configure',
                     N'dispatches.read-all',N'dispatches.reports.view',N'dispatches.reports.export',
                     N'sales.reports.read',N'sales.reports.read-all',N'sales.returns.read',N'sales.debit-notes.read',
                     N'service-invoices.read',
                     N'purchasing.goods-receipts.read',N'purchasing.purchase-returns.read');
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@SupervisorRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'sales.create',N'sales.discount',N'sales.reprint',N'sales.lines.remove',N'sales.drafts.restart',
                  N'pos.approvals.authorize',N'pos.approvals.read',N'pos.approvals.receive_notifications',N'pos.approvals.manage_credential',
                  N'pos.customer.create',N'pos.orders',N'orders.read',N'orders.invoice',
                  N'sales.returns.read',N'sales.returns.create',N'sales.returns.confirm',
                  N'service-invoices.read',N'service-invoices.create',N'service-invoices.price.override',
                  N'service-invoices.discount',N'service-invoices.issue',N'service-invoices.print',
                  N'work-sessions.read',N'work-sessions.open',N'work-sessions.close',N'work-sessions.cash.manage',N'work-sessions.cash.drawer.open',N'inventory.read');
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@SellerRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'orders.read',N'orders.create',N'orders.update',N'routes.read',N'routes.visits.record',
                  N'customers.read',N'parties.read');
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
                SELECT NEWID(),@CashierRoleId,PermissionId,@Now
                FROM dbo.Permissions
                WHERE Resource IN(
                  N'sales.create',N'sales.reprint',N'pos.customer.create',N'pos.orders',N'orders.read',
                  N'fiscal.configuration.read',N'pos.synchronization.events.read');

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
            Add("@SalesWarehouseId", salesWarehouseId); Add("@OrdersWarehouseId", ordersWarehouseId); Add("@DamagedWarehouseId", damagedWarehouseId);
            Add("@ConsumerPartyId", consumerPartyId); Add("@CustomerId", customerId);
            Add("@CashierRoleId", cashierRoleId); Add("@SupervisorRoleId", supervisorRoleId); Add("@SellerRoleId", sellerRoleId);
            Add("@AdministrativeRoleId", administrativeRoleId); Add("@AccountantRoleId", accountantRoleId); Add("@AdminRoleId", adminRoleId);
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
            Add("@AuditPayload", JsonSerializer.Serialize(new { request.ProvisioningRequestId, businessId, salesWarehouseId, ordersWarehouseId, damagedWarehouseId, request.MaximumUsers, request.MaximumEnrolledDevices }));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await using (var accounting = new SqlCommand("dbo.AccountingDefaultsProvision", connection, transaction)
            {
                CommandType = CommandType.StoredProcedure
            })
            {
                accounting.Parameters.AddWithValue("@TenantId", tenantId);
                accounting.Parameters.AddWithValue("@BusinessId", businessId);
                accounting.Parameters.AddWithValue("@Now", now);
                await accounting.ExecuteNonQueryAsync(cancellationToken);
            }
            await ProvisionCommercialSubscriptionAsync(connection, transaction, tenantId,
                request, commercialQuote, now, cancellationToken);
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

    private async Task ProvisionCommercialSubscriptionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        ProvisionTenantRequest request,
        TenantQuoteDto quote,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var partyId = ids.NewId();
        var customerId = ids.NewId();
        var subscriptionId = ids.NewId();
        await using var command = new SqlCommand("""
            DECLARE @BillingBusinessId uniqueidentifier,@PlatformTenantId uniqueidentifier,
                    @BillingActorUserId uniqueidentifier,@PlanId uniqueidentifier,
                    @ExistingPartyId uniqueidentifier,@ExistingCustomerId uniqueidentifier;

            SELECT @BillingBusinessId=settings.BillingBusinessId,
                   @PlatformTenantId=businessValue.TenantId,
                   @BillingActorUserId=settings.UpdatedByUserId
            FROM billing.PlatformBillingSettings settings WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses businessValue
              ON businessValue.BusinessId=settings.BillingBusinessId
            INNER JOIN dbo.Tenants platformTenant ON platformTenant.TenantId=businessValue.TenantId
            WHERE settings.PlatformBillingSettingId=1
              AND businessValue.IsActive=1 AND platformTenant.IsActive=1;
            IF @BillingBusinessId IS NULL
              THROW 51051,'Auraly no está configurada como empresa facturadora de plataforma.',1;

            SELECT @PlanId=planValue.TenantCommercialPlanId
            FROM billing.TenantCommercialPlans planValue WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN billing.BillableServices serviceValue
              ON serviceValue.BillableServiceId=planValue.BillableServiceId
            WHERE serviceValue.Code=@PlanCode AND planValue.IsActive=1 AND serviceValue.IsActive=1;
            IF @PlanId IS NULL
              THROW 51052,'El plan comercial aprobado ya no está disponible.',1;

            SELECT @ExistingPartyId=PartyId
            FROM dbo.Parties WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@PlatformTenantId AND IdentificationTypeCode=N'NIT'
              AND NormalizedIdentification=@NormalizedNit;
            IF @ExistingPartyId IS NULL
            BEGIN
              SET @ExistingPartyId=@PartyId;
              INSERT dbo.Parties
                (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                 Identification,NormalizedIdentification,VerificationDigit,DisplayName,LegalName,
                 CompletionStatus,IsActive,CreatedBy,CreatedAt)
              VALUES(@ExistingPartyId,@PlatformTenantId,N'Organization',@CountryId,N'NIT',@Nit,
                 @NormalizedNit,@VerificationDigit,@TradeName,@LegalName,N'Complete',1,
                 @BillingActorUserId,@Now);
            END;

            IF NOT EXISTS(SELECT 1 FROM dbo.PartyContacts
                          WHERE PartyId=@ExistingPartyId AND ContactType=N'Email'
                            AND NormalizedValue=UPPER(@Email))
              INSERT dbo.PartyContacts
                (PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
              VALUES(NEWID(),@ExistingPartyId,N'Email',@Email,UPPER(@Email),
                CASE WHEN EXISTS(SELECT 1 FROM dbo.PartyContacts
                                 WHERE PartyId=@ExistingPartyId AND ContactType=N'Email'
                                   AND IsPrimary=1 AND IsActive=1) THEN 0 ELSE 1 END,1,@Now);

            IF NOT EXISTS(SELECT 1 FROM dbo.PartySites
                          WHERE PartyId=@ExistingPartyId AND Code=N'MAIN')
              INSERT dbo.PartySites
                (PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
                 AddressLine,Email,Phone,IsPrimary,IsActive,CreatedBy,CreatedAt)
              VALUES(NEWID(),@ExistingPartyId,N'MAIN',N'Sede principal',@CountryId,@DivisionId,
                 @CityId,@Address,@Email,@Phone,
                 CASE WHEN EXISTS(SELECT 1 FROM dbo.PartySites
                                  WHERE PartyId=@ExistingPartyId AND IsPrimary=1 AND IsActive=1)
                      THEN 0 ELSE 1 END,1,@BillingActorUserId,@Now);

            SELECT @ExistingCustomerId=CustomerId
            FROM dbo.Customers WITH (UPDLOCK,HOLDLOCK)
            WHERE PartyId=@ExistingPartyId AND BusinessId=@BillingBusinessId;
            IF @ExistingCustomerId IS NULL
            BEGIN
              SET @ExistingCustomerId=@CustomerId;
              INSERT dbo.Customers
                (CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
              VALUES(@ExistingCustomerId,@ExistingPartyId,@BillingBusinessId,1,1,
                     @BillingActorUserId,@Now);
            END;

            IF EXISTS(SELECT 1 FROM billing.TenantSubscriptions WITH (UPDLOCK,HOLDLOCK)
                      WHERE TenantId=@TenantId)
              THROW 51053,'El tenant ya tiene una suscripción comercial.',1;

            INSERT billing.TenantSubscriptions
              (TenantSubscriptionId,TenantId,TenantCommercialPlanId,BillingCustomerId,
               BillingPeriod,Status,CurrentPeriodStart,CurrentPeriodEnd,BillingAnchorDay,
               FullUserLimit,SellerUserLimit,PosDeviceLimit,DianDocumentMonthlyLimit,
               PayrollEmployeeLimit,CreatedAt,UpdatedAt)
            VALUES(@SubscriptionId,@TenantId,@PlanId,@ExistingCustomerId,@BillingPeriod,N'Active',
               @Now,@SubscriptionEnd,DAY(@Now),@FullUsers,@SellerUsers,@PosDevices,
               @DianDocuments,@PayrollEmployees,@Now,@Now);
            INSERT billing.TenantSubscriptionUsagePeriods
              (TenantSubscriptionUsagePeriodId,TenantSubscriptionId,PeriodStart,PeriodEnd,
               DianDocumentsUsed,CreatedAt,UpdatedAt)
            VALUES(NEWID(),@SubscriptionId,@Now,DATEADD(month,1,@Now),0,@Now,@Now);
            INSERT dbo.ScheduledAutomationJobs
              (ScheduledAutomationJobId,BusinessId,ReservationId,AgentId,TenantSubscriptionId,
               JobType,ScheduledAtUtc,Status,DeduplicationKey,Attempts,PayloadJson,CreatedAt)
            SELECT NEWID(),NULL,NULL,NULL,@SubscriptionId,2,
                   CONVERT(datetime2,SWITCHOFFSET(
                     DATEADD(day,-settings.PreDueReminderDays,@SubscriptionEnd),'+00:00')),
                   0,CONCAT(N'tenant-subscription-lifecycle:',
                     LOWER(CONVERT(nvarchar(36),@SubscriptionId))),0,N'{}',@Now
            FROM billing.PlatformBillingSettings settings
            WHERE settings.PlatformBillingSettingId=1;
            IF @@ROWCOUNT<>1 THROW 51054,'La política global de cobranza no está configurada.',1;
            """, connection, transaction);
        void Add(string name, object? value) =>
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        Add("@PartyId", partyId);
        Add("@CustomerId", customerId);
        Add("@SubscriptionId", subscriptionId);
        Add("@TenantId", tenantId);
        Add("@PlanCode", quote.PlanCode);
        Add("@CountryId", request.CountryId);
        Add("@DivisionId", request.AdministrativeDivisionId);
        Add("@CityId", request.CityId);
        Add("@Nit", request.Nit.Trim());
        Add("@NormalizedNit", NormalizeDigits(request.Nit));
        Add("@VerificationDigit", request.VerificationDigit.Trim());
        Add("@TradeName", request.TradeName.Trim());
        Add("@LegalName", request.LegalName.Trim());
        Add("@Email", request.Email.Trim());
        Add("@Address", request.Address.Trim());
        Add("@Phone", request.Phone.Trim());
        Add("@Now", now);
        Add("@BillingPeriod", quote.BillingPeriod);
        Add("@SubscriptionEnd", quote.BillingPeriod == "Annual" ? now.AddYears(1) : now.AddMonths(1));
        Add("@FullUsers", quote.FullUserLimit);
        Add("@SellerUsers", quote.SellerUserLimit);
        Add("@PosDevices", quote.PosDeviceLimit);
        Add("@DianDocuments", quote.DianDocumentMonthlyLimit);
        Add("@PayrollEmployees", quote.PayrollEmployeeLimit);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                          WHERE TenantId=@TenantId AND (NormalizedEmail=@NormalizedEmail OR NormalizedUsername=@NormalizedEmail))
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

