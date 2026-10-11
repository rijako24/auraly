using Auraly.Contracts.Catalog;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class BusinessPointOfSaleModeTests
{
    private readonly Mock<IUnitOfWork> _work = new();
    private readonly Mock<IBusinessRepository> _businesses = new();
    private readonly Mock<IReferenceOptionStore> _options = new(MockBehavior.Strict);
    private readonly Mock<IBusinessDefaultsProvisioner> _defaults = new();
    private readonly Business _business = new()
    {
        BusinessId = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Sede",
        PointOfSaleMode = PointOfSaleModes.RestaurantBar
    };
    private BusinessAdminService Service => new(
        _work.Object, _defaults.Object, Mock.Of<ICorrelationIdProvider>(),
        NullLogger<BusinessAdminService>.Instance, _options.Object, TimeProvider.System);

    public BusinessPointOfSaleModeTests()
    {
        _work.SetupGet(work => work.Businesses).Returns(_businesses.Object);
        _work.Setup(work => work.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task> action, CancellationToken _) => action());
        _businesses.Setup(repo => repo.GetByIdAsync(_business.BusinessId)).ReturnsAsync(_business);
    }

    [Fact]
    public async Task Omitted_mode_preserves_restaurant_and_does_not_read_the_catalog_or_reload_the_business()
    {
        var result = await Service.UpdateAsync(_business.TenantId, false, _business.BusinessId,
            new("Nombre actualizado", null, null, null, null, null, null), default);

        Assert.Equal(PointOfSaleModes.RestaurantBar, result.PointOfSaleMode);
        Assert.Equal("Nombre actualizado", result.Name);
        _businesses.Verify(repo => repo.GetByIdAsync(_business.BusinessId), Times.Once);
        _work.Verify(work => work.SaveChangesAsync(default), Times.Once);
        _defaults.Verify(value => value.ReconcileRestaurantRolePermissionsAsync(
            _business.TenantId, default), Times.Never);
        _options.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Waiter_policy_is_saved_without_reloading_the_business_or_reading_mode_options()
    {
        var result = await Service.UpdateAsync(_business.TenantId, false, _business.BusinessId,
            new(null, null, null, null, null, null, null,
                RestaurantRequireWaiterCredential: false), default);

        Assert.False(result.RestaurantRequireWaiterCredential);
        Assert.False(result.RestaurantRestrictTableToWaiter);
        _businesses.Verify(repo => repo.GetByIdAsync(_business.BusinessId), Times.Once);
        _work.Verify(work => work.SaveChangesAsync(default), Times.Once);
        _options.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Exclusive_waiter_policy_cannot_disable_credential()
    {
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.UpdateAsync(
            _business.TenantId, false, _business.BusinessId,
            new(null, null, null, null, null, null, null,
                RestaurantRequireWaiterCredential: false,
                RestaurantRestrictTableToWaiter: true), default));

        _work.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Mode_change_validates_once_and_returns_the_saved_value_without_refetching()
    {
        SetActiveModes(PointOfSaleModes.DirectSale);
        var result = await Service.UpdateAsync(_business.TenantId, false, _business.BusinessId,
            ChangeMode(PointOfSaleModes.DirectSale), default);

        Assert.Equal(PointOfSaleModes.DirectSale, result.PointOfSaleMode);
        _options.Verify(store => store.ListAsync(PointOfSaleModes.CatalogCode, default), Times.Once);
        _businesses.Verify(repo => repo.GetByIdAsync(_business.BusinessId), Times.Once);
        _work.Verify(work => work.SaveChangesAsync(default), Times.Once);
        _defaults.Verify(value => value.ReconcileRestaurantRolePermissionsAsync(
            _business.TenantId, default), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("directsale")]
    [InlineData("Restaurant")]
    [InlineData("RestaurantBar ")]
    public async Task Unknown_modes_are_rejected_before_any_catalog_read_or_write(string mode)
    {
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.UpdateAsync(
            _business.TenantId, false, _business.BusinessId, ChangeMode(mode), default));
        _options.VerifyNoOtherCalls();
        _work.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Inactive_mode_cannot_be_selected_and_rejection_does_not_change_other_fields()
    {
        SetActiveModes();
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.UpdateAsync(
            _business.TenantId, false, _business.BusinessId,
            ChangeMode(PointOfSaleModes.DirectSale) with { Name = "No guardar" }, default));
        Assert.Equal("Sede", _business.Name);
        Assert.Equal(PointOfSaleModes.RestaurantBar, _business.PointOfSaleMode);
        _work.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Another_tenant_cannot_read_or_change_the_mode()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service.UpdateAsync(
            Guid.NewGuid(), false, _business.BusinessId, ChangeMode(PointOfSaleModes.DirectSale), default));
        _options.VerifyNoOtherCalls();
        _work.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    public async Task Paged_business_reads_do_not_query_modes_per_item(int size)
    {
        var items = Enumerable.Range(0, size).Select(_ => new Business
        {
            BusinessId = Guid.NewGuid(), TenantId = _business.TenantId
        }).ToList();
        _businesses.Setup(repo => repo.GetPagedByTenantIdAsync(_business.TenantId, 2, size, null, default))
            .ReturnsAsync((items, size * 3));
        await Service.GetPagedByTenantAsync(_business.TenantId, new PagedRequest { Page = 2, PageSize = size }, default);
        _businesses.Verify(repo => repo.GetPagedByTenantIdAsync(_business.TenantId, 2, size, null, default), Times.Once);
        _options.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Legacy_create_defaults_to_direct_sale_and_provisions_the_same_warehouses()
    {
        SetActiveModes(PointOfSaleModes.DirectSale);
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(repo => repo.GetByIdAsync(_business.TenantId, default))
            .ReturnsAsync(new Tenant { TenantId = _business.TenantId });
        _work.SetupGet(work => work.Tenants).Returns(tenants.Object);
        _work.Setup(work => work.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), default))
            .Returns((Func<Task> action, CancellationToken _) => action());
        var sourceId = Guid.NewGuid();

        var result = await Service.CreateAsync(_business.TenantId,
            new("Nueva sede", null, null, null, null, null, PriceSourceBusinessId: sourceId), default);

        Assert.Equal(PointOfSaleModes.DirectSale, result.PointOfSaleMode);
        _defaults.Verify(provisioner => provisioner.ProvisionWarehousesAsync(
            _business.TenantId, result.BusinessId, sourceId, It.IsAny<string>(), default), Times.Once);
        _businesses.Verify(repo => repo.CreateAsync(It.Is<Business>(business =>
            business.PointOfSaleMode == PointOfSaleModes.DirectSale)), Times.Once);
    }

    [Fact]
    public async Task Restaurant_create_can_choose_selection_without_credential()
    {
        SetActiveModes(PointOfSaleModes.RestaurantBar);
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(repo => repo.GetByIdAsync(_business.TenantId, default))
            .ReturnsAsync(new Tenant { TenantId = _business.TenantId });
        _work.SetupGet(work => work.Tenants).Returns(tenants.Object);
        _work.Setup(work => work.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), default))
            .Returns((Func<Task> action, CancellationToken _) => action());

        var result = await Service.CreateAsync(_business.TenantId,
            new("Restaurante", null, null, null, null, null,
                PointOfSaleMode: PointOfSaleModes.RestaurantBar,
                RestaurantRequireWaiterCredential: false), default);

        Assert.False(result.RestaurantRequireWaiterCredential);
        Assert.False(result.RestaurantRestrictTableToWaiter);
        _businesses.Verify(repo => repo.CreateAsync(It.Is<Business>(business =>
            business.PointOfSaleMode == PointOfSaleModes.RestaurantBar &&
            !business.RestaurantRequireWaiterCredential)), Times.Once);
        _defaults.Verify(value => value.ReconcileRestaurantRolePermissionsAsync(
            _business.TenantId, default), Times.Once);
    }

    private void SetActiveModes(params string[] codes) => _options
        .Setup(store => store.ListAsync(PointOfSaleModes.CatalogCode, default))
        .ReturnsAsync(codes.Select(code => new ReferenceOption(Guid.NewGuid(), code, code, null, 0)).ToList());

    private static UpdateBusinessRequest ChangeMode(string code) =>
        new(null, null, null, null, null, null, null, PointOfSaleMode: code);
}
