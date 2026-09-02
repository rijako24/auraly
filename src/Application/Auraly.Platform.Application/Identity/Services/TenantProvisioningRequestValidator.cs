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
        var required = new[] { request.LegalName, request.TradeName, request.EntityType,
            request.IdentificationTypeCode, request.Nit,
            request.Address, request.Phone, request.Email, request.BusinessName, request.BusinessAddress,
            request.BusinessPhone, request.BusinessEmail, request.InvitationEmail };
        if (required.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Completa todos los datos obligatorios de empresa, sede y administrador.");
        if (request.EntityType is not ("NaturalPerson" or "Organization")
            || request.EntityType == "Organization" && request.IdentificationTypeCode != "NIT"
            || request.EntityType == "NaturalPerson" && request.IdentificationTypeCode == "NIT")
            throw new ArgumentException("Selecciona una combinación válida de tipo de persona e identificación.");
        if (request.IdentificationTypeCode == "NIT" && string.IsNullOrWhiteSpace(request.VerificationDigit)
            || request.IdentificationTypeCode != "NIT" && request.VerificationDigit is not null)
            throw new ArgumentException("El dígito de verificación solo es obligatorio para el NIT.");
        ValidateIdentification(request.IdentificationTypeCode, request.Nit, request.VerificationDigit);
        if (request.InventoryCostBasis is not ("LatestReceiptCost" or "WeightedAverageCost"))
            throw new ArgumentException("La base de costo de inventario no es válida.");
        if (!request.Email.Contains('@') || !request.BusinessEmail.Contains('@') || !request.InvitationEmail.Contains('@'))
            throw new ArgumentException("Los correos de empresa, sede e invitación no son válidos.");
        if (request.MaximumUsers < 1)
            throw new ArgumentException("El límite de usuarios debe ser al menos 1.");
        if (request.MaximumEnrolledDevices < 0)
            throw new ArgumentException("El límite de cajas no puede ser negativo.");
    }

    public static void ValidateIdentification(string identificationTypeCode, string identification,
        string? verificationDigit)
    {
        var value = identification.Trim();
        if (value.Length is < 3 or > 32)
            throw new ArgumentException("El número de identificación debe tener entre 3 y 32 caracteres.");
        var numeric = identificationTypeCode is "NIT" or "CC" or "CE" or "PPT";
        if (numeric && !value.All(char.IsDigit))
            throw new ArgumentException("El tipo de identificación seleccionado solo admite números sin puntos ni guiones.");
        if (!numeric && !value.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("El documento solo admite letras y números sin espacios ni signos.");
        if (identificationTypeCode != "NIT") return;
        if (verificationDigit is null || verificationDigit.Length != 1 || !char.IsDigit(verificationDigit[0]))
            throw new ArgumentException("El dígito de verificación del NIT debe ser un solo número.");
        if (value.Length > NitWeights.Length || verificationDigit[0] - '0' != CalculateNitVerificationDigit(value))
            throw new ArgumentException("El dígito de verificación no corresponde al NIT ingresado.");
    }

    public static int CalculateNitVerificationDigit(string nit)
    {
        var offset = NitWeights.Length - nit.Length;
        var sum = nit.Select((value, index) => (value - '0') * NitWeights[offset + index]).Sum();
        var remainder = sum % 11;
        return remainder is 0 or 1 ? remainder : 11 - remainder;
    }

    private static readonly int[] NitWeights = [71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3];
}
