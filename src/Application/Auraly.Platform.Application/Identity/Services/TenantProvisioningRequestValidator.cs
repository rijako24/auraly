using Auraly.Contracts.Tenants;

namespace Auraly.Platform.Application.Identity.Services;

public static class TenantProvisioningRequestValidator
{
    public static void Validate(ProvisionTenantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProvisioningRequestId == Guid.Empty || request.CountryId == Guid.Empty
            || request.AdministrativeDivisionId == Guid.Empty || request.CityId == Guid.Empty)
            throw new ArgumentException("La solicitud, país, departamento y ciudad son obligatorios.");
        var required = new[] { request.LegalName, request.TradeName, request.Nit, request.VerificationDigit,
            request.Address, request.Phone, request.Email, request.BusinessName, request.BusinessAddress,
            request.BusinessPhone, request.BusinessEmail, request.InvitationEmail };
        if (required.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Completa todos los datos obligatorios de empresa, sede y administrador.");
        if (request.InventoryCostBasis is not ("LatestReceiptCost" or "WeightedAverageCost"))
            throw new ArgumentException("La base de costo de inventario no es válida.");
        if (!request.Email.Contains('@') || !request.BusinessEmail.Contains('@') || !request.InvitationEmail.Contains('@'))
            throw new ArgumentException("Los correos de empresa, sede e invitación no son válidos.");
        if (request.MaximumUsers < 1)
            throw new ArgumentException("El límite de usuarios debe ser al menos 1.");
        if (request.MaximumEnrolledDevices < 0)
            throw new ArgumentException("El límite de cajas no puede ser negativo.");
    }
}
