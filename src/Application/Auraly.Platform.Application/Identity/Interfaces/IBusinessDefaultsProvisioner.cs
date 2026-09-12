namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IBusinessDefaultsProvisioner
{
    Task ProvisionWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        Guid priceSourceBusinessId,
        string inventoryCostBasis,
        CancellationToken cancellationToken);
}
