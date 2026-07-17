using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.API.Functions;

public sealed class ProductIdentityNightlySyncFunction
{
    private readonly IProductCatalogSyncService _sync;
    private readonly ILogger<ProductIdentityNightlySyncFunction> _logger;

    public ProductIdentityNightlySyncFunction(
        IProductCatalogSyncService sync,
        ILogger<ProductIdentityNightlySyncFunction> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    [Function("ProductIdentityNightlySync")]
    public async Task Run(
        [TimerTrigger("0 0 5 * * *")] TimerInfo timerInfo,
        CancellationToken ct)
    {
        var results = await _sync.SyncAllEnabledAsync(CommerceProvider.Mantis, ct);
        _logger.LogInformation(
            "Nightly Mantis identity sync completed for {BusinessCount} businesses. " +
            "Products processed: {ProductsProcessed}; products changed: {ProductsChanged}; " +
            "customers processed: {CustomersProcessed}; customers changed: {CustomersChanged}.",
            results.Count,
            results.Sum(result => result.ProductsProcessed),
            results.Sum(result => result.ProductsChanged),
            results.Sum(result => result.CustomersProcessed),
            results.Sum(result => result.CustomersChanged));
    }
}
