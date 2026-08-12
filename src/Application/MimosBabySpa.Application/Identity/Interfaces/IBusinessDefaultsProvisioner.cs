namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IBusinessDefaultsProvisioner
{
    Task ProvisionWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        string inventoryCostBasis,
        CancellationToken cancellationToken);
}
