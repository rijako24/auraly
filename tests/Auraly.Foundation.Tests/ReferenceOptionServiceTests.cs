using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

namespace Auraly.Foundation.Tests;

public sealed class ReferenceOptionServiceTests
{
    [Theory]
    [InlineData("payment-method", "payment-method")]
    [InlineData(" Payment-Method ", "payment-method")]
    public async Task ListAsync_normalizes_valid_catalog_codes(string input, string expected)
    {
        var store = new CapturingStore();
        var service = new ReferenceOptionService(store);

        await service.ListAsync(input);

        Assert.Equal(expected, store.CatalogCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("catalog/escape")]
    [InlineData("áccent")]
    public async Task ListAsync_rejects_invalid_catalog_codes(string input)
    {
        var service = new ReferenceOptionService(new CapturingStore());

        await Assert.ThrowsAsync<CatalogValidationException>(() => service.ListAsync(input));
    }

    [Fact]
    public async Task CreateAsync_normalizes_values_and_requires_catalog_update_permission()
    {
        var store = new CapturingStore();
        var service = new ReferenceOptionService(store);
        var user = new CatalogUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { CatalogPermissionCodes.Update });

        await service.CreateAsync(user, " Tax-Responsibility ",
            new CreateReferenceOptionRequest(" O-99 ", " Nueva responsabilidad ", " Detalle "));

        Assert.Equal("tax-responsibility", store.CatalogCode);
        Assert.Equal("O-99", store.Created?.Code);
        Assert.Equal("Nueva responsabilidad", store.Created?.Label);
        Assert.Equal("Detalle", store.Created?.Description);
    }

    [Fact]
    public async Task CreateAsync_rejects_users_without_catalog_update_permission()
    {
        var service = new ReferenceOptionService(new CapturingStore());
        var user = new CatalogUserIdentity(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new HashSet<string>());

        await Assert.ThrowsAsync<CatalogForbiddenException>(() => service.CreateAsync(
            user, "tax-responsibility", new CreateReferenceOptionRequest("O-99", "Nueva")));
    }

    private sealed class CapturingStore : IReferenceOptionStore
    {
        public string? CatalogCode { get; private set; }
        public CreateReferenceOptionRequest? Created { get; private set; }

        public Task<IReadOnlyList<ReferenceOption>> ListAsync(
            string catalogCode,
            CancellationToken cancellationToken)
        {
            CatalogCode = catalogCode;
            return Task.FromResult<IReadOnlyList<ReferenceOption>>([]);
        }

        public Task<ReferenceOption> CreateAsync(
            string catalogCode,
            CreateReferenceOptionRequest request,
            CancellationToken cancellationToken)
        {
            CatalogCode = catalogCode;
            Created = request;
            return Task.FromResult(new ReferenceOption(
                Guid.NewGuid(), request.Code, request.Label, request.Description, 10));
        }
    }
}
