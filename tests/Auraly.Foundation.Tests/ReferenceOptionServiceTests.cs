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

    private sealed class CapturingStore : IReferenceOptionStore
    {
        public string? CatalogCode { get; private set; }

        public Task<IReadOnlyList<ReferenceOption>> ListAsync(
            string catalogCode,
            CancellationToken cancellationToken)
        {
            CatalogCode = catalogCode;
            return Task.FromResult<IReadOnlyList<ReferenceOption>>([]);
        }
    }
}
