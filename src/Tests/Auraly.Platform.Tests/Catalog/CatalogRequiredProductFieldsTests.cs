using Auraly.Application.Catalog;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Application.Pricing;
using Auraly.Contracts.Pricing;
using FluentAssertions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Catalog;

public sealed class CatalogRequiredProductFieldsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _businessId = Guid.NewGuid();

    [Theory]
    [InlineData("create", "purchase-vat")]
    [InlineData("create", "sales-vat")]
    [InlineData("create", "cost")]
    [InlineData("create", "sale-price")]
    [InlineData("create", "supplier")]
    [InlineData("update", "purchase-vat")]
    [InlineData("update", "sales-vat")]
    [InlineData("update", "cost")]
    [InlineData("update", "sale-price")]
    [InlineData("update", "supplier")]
    public async Task Create_and_update_reject_incomplete_commercial_configuration(
        string operation, string missingField)
    {
        var (service, store, user) = CreateService();
        var request = Without(ValidRequest(), missingField);

        Func<Task> action = operation == "create"
            ? () => service.CreateAsync(user, request, CancellationToken.None)
            : () => service.UpdateAsync(user, Guid.NewGuid(), request, CancellationToken.None);

        await action.Should().ThrowAsync<CatalogValidationException>();
        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task Create_and_update_accept_zero_margin(string operation)
    {
        var (service, store, user) = CreateService();
        var productId = Guid.NewGuid();
        var request = ValidRequest() with
        {
            Prices = [new ProductPriceInput(100m, "COP", 100m, 0m)]
        };
        var saved = Detail(productId, request);
        store.Setup(value => value.CreateAsync(user, It.IsAny<Guid>(), request, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);
        store.Setup(value => value.UpdateAsync(user, productId, request, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var result = operation == "create"
            ? await service.CreateAsync(user, request, CancellationToken.None)
            : await service.UpdateAsync(user, productId, request, CancellationToken.None);

        result.Should().Be(saved);
    }

    [Fact]
    public void Pricing_engine_accepts_zero_margin_with_positive_cost_and_sale_price()
    {
        var store = new Mock<IPricingStore>(MockBehavior.Strict);
        var synchronization = new Mock<IPosSynchronizationOutboxDispatcher>();
        var service = new PricingService(store.Object, TimeProvider.System, synchronization.Object);
        var user = new PricingUserIdentity(Guid.NewGuid(), _tenantId, _businessId,
            new HashSet<string> { PricingPermissionCodes.Read, PricingPermissionCodes.ReadCostBasis });

        var result = service.Calculate(user, new PriceCalculationRequest(
            100m, PriceInputModes.Margin, 0m, null, 1m, PricingRoundingModes.Nearest, 19m));

        result.TargetMarginPercent.Should().Be(0m);
        result.RoundedSalePrice.Should().Be(119m);
    }

    private (CatalogService Service, Mock<ICatalogStore> Store, CatalogUserIdentity User) CreateService()
    {
        var store = new Mock<ICatalogStore>(MockBehavior.Strict);
        var ids = new Mock<IAuralyIdGenerator>();
        ids.Setup(value => value.NewId()).Returns(Guid.NewGuid());
        var synchronization = new Mock<IPosSynchronizationOutboxDispatcher>();
        synchronization.Setup(value => value.DispatchPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var permissions = new HashSet<string>
        {
            CatalogPermissionCodes.Create,
            CatalogPermissionCodes.Update,
            CatalogPermissionCodes.ManagePrices,
            CatalogPermissionCodes.ManageCosts
        };
        var user = new CatalogUserIdentity(Guid.NewGuid(), _tenantId, _businessId, permissions);
        return (new CatalogService(store.Object, ids.Object, TimeProvider.System, synchronization.Object), store, user);
    }

    private SaveProductRequest ValidRequest()
    {
        var supplierId = Guid.NewGuid();
        return new SaveProductRequest(
            _businessId, "PRD-1", null, "Producto", null, "EA", Guid.NewGuid(),
            true, false, [], [], [new ProductPriceInput(119m, "COP", 100m, 0m)],
            [new SupplierCostInput(supplierId, "900123456", "Proveedor", null, 100m)],
            null, Guid.NewGuid(), "DeductibleInputVat");
    }

    private static SaveProductRequest Without(SaveProductRequest request, string field) => field switch
    {
        "purchase-vat" => request with { PurchaseTaxProfileId = Guid.Empty },
        "sales-vat" => request with { TaxProfileId = Guid.Empty },
        "cost" => request with
        {
            Prices = [request.Prices.Single() with { CostBasisAmount = 0m }]
        },
        "sale-price" => request with
        {
            Prices = [request.Prices.Single() with { Amount = 0m }]
        },
        "supplier" => request with { Suppliers = [] },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static ProductDetail Detail(Guid productId, SaveProductRequest request) => new(
        productId, request.BusinessId, request.ProductCode, request.Reference, request.Name, true,
        [], request.Prices, request.Suppliers, request.TaxProfileId, request.PurchaseTaxProfileId,
        request.PurchaseTaxTreatment, request.Description, request.BaseUnitCode,
        request.ManageInventory, request.IsWeighable);
}
