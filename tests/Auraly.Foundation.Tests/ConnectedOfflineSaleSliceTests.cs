using Auraly.Application.Authorization;
using Auraly.Application.Catalog;
using Auraly.Application.Fiscal;
using Auraly.Application.Organization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Application.Outbox;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Domain.Authorization;
using Auraly.Domain.Catalog;
using Auraly.Domain.Fiscal;
using Auraly.Domain.Organization;
using Auraly.Domain.Sales;
using Auraly.Fiscal.Core;

namespace Auraly.Foundation.Tests;

public sealed class ConnectedOfflineSaleSliceTests
{
    [Fact]
    public void Product_to_register_to_fiscal_snapshot_to_outbox_is_connected()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var businessId = new BusinessId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var locationId = new LocationId(Guid.NewGuid());
        var warehouse = new Warehouse(
            new WarehouseId(Guid.NewGuid()),
            businessId,
            locationId,
            "B01",
            "Principal",
            allowNegativeStockSales: true);
        var register = new Register(
            new RegisterId(Guid.NewGuid()),
            businessId,
            warehouse.Id,
            "C01");
        var registerContext = new RegisterContextProjector().Project(tenantId, register, warehouse);

        var product = new Product(
            new ProductId(Guid.NewGuid()),
            tenantId,
            "PROD-001",
            "Producto conectado");
        product.AddBarcode("7701234567890");
        var posProduct = new PosCatalogProjector().Project(product, "01", 19m);

        var issueDate = new DateOnly(2026, 7, 27);
        var issuedAt = new DateTimeOffset(2026, 7, 27, 14, 35, 12, TimeSpan.FromHours(-5));
        var series = new DocumentSeries(
            Guid.NewGuid(),
            businessId,
            register.Id,
            "FV01",
            1,
            100,
            issueDate,
            issueDate.AddYears(1));
        series.Activate(issueDate);
        var fiscalNumber = new FiscalNumberAllocator().Allocate(
            series,
            register.Id,
            issueDate,
            "18760000001");

        var permissionSet = new UserPermissionSet(
            tenantId,
            userId,
            [CommercePermissionCodes.SalesCreate, CommercePermissionCodes.SalesDiscount]);
        var authorizer = new PermissionAuthorizer(new FixedPermissionProvider(permissionSet));
        var service = new ConfirmOfflineSaleService(authorizer);

        var result = service.Confirm(new ConfirmOfflineSaleCommand(
            userId,
            new DocumentId(Guid.NewGuid()),
            registerContext,
            fiscalNumber,
            issuedAt,
            "9001234567",
            "222222222",
            new FiscalTechnicalKey("CLAVE-TECNICA", "v1"),
            FiscalEnvironment.Test,
            "https://catalogo-vpfe.dian.gov.co/document/searchqr",
            [new OfflineSaleLine(posProduct, 2m, 10_000m, 1_000m, 3_610m)]));

        Assert.Equal(SalesInvoiceStatus.LocallyIssuedPendingSync, result.Invoice.Status);
        Assert.Equal("FV011", result.Contract.FiscalNumber);
        Assert.Equal(result.Contract.Cufe, result.Invoice.FiscalSnapshot!.Cufe);
        Assert.Equal(22_610m, result.Contract.Total);
        Assert.Equal(OutboxMessageStatus.Pending, result.OutboxMessage.Status);
        Assert.Contains(result.Contract.DocumentId.ToString(), result.OutboxMessage.Payload);
        Assert.True(registerContext.WarehouseAllowsNegativeStockSales);
    }

    [Fact]
    public void Discount_is_rejected_when_the_backend_permission_is_missing()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var permissionSet = new UserPermissionSet(
            tenantId,
            userId,
            [CommercePermissionCodes.SalesCreate]);
        var service = new ConfirmOfflineSaleService(
            new PermissionAuthorizer(new FixedPermissionProvider(permissionSet)));
        var product = new Auraly.Contracts.Catalog.PosCatalogProduct(
            new ProductId(Guid.NewGuid()),
            "P1",
            "Producto",
            ["7701"],
            true,
            false,
            "01",
            19m);
        var context = new Auraly.Contracts.Organization.RegisterContext(
            tenantId,
            new BusinessId(Guid.NewGuid()),
            new LocationId(Guid.NewGuid()),
            new WarehouseId(Guid.NewGuid()),
            new RegisterId(Guid.NewGuid()),
            true);

        Assert.Throws<UnauthorizedAccessException>(() =>
            service.Confirm(new ConfirmOfflineSaleCommand(
                userId,
                new DocumentId(Guid.NewGuid()),
                context,
                new Auraly.Contracts.Fiscal.FiscalNumberAssignment(
                    Guid.NewGuid(),
                    "FV01",
                    1,
                    "FV011",
                    "18760000001"),
                new DateTimeOffset(2026, 7, 27, 14, 35, 12, TimeSpan.FromHours(-5)),
                "9001234567",
                "222222222",
                new FiscalTechnicalKey("CLAVE-TECNICA", "v1"),
                FiscalEnvironment.Test,
                "https://catalogo-vpfe.dian.gov.co/document/searchqr",
                [new OfflineSaleLine(product, 1m, 10_000m, 1_000m, 1_710m)])));
    }

    private sealed class FixedPermissionProvider(UserPermissionSet permissionSet)
        : IUserPermissionSetProvider
    {
        public UserPermissionSet Get(TenantId tenantId, UserId userId) => permissionSet;
    }
}
