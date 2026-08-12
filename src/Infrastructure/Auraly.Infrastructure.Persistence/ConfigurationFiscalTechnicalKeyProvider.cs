using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Core;
using Microsoft.Extensions.Configuration;

namespace Auraly.Infrastructure.Persistence;

public sealed class ConfigurationFiscalTechnicalKeyProvider(IConfiguration configuration)
    : IFiscalTechnicalKeyProvider
{
    public Task<FiscalVerificationMaterial?> ResolveAsync(
        FiscalKeyReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = configuration
            .GetSection("Auraly:Fiscal:TechnicalKeys")
            .GetChildren();
        foreach (var entry in entries)
        {
            if (!Guid.TryParse(entry["TenantId"], out var tenantId) ||
                !Guid.TryParse(entry["BusinessId"], out var businessId) ||
                !int.TryParse(entry["Environment"], out var environment) ||
                tenantId != reference.TenantId ||
                businessId != reference.BusinessId ||
                environment != (int)reference.Environment ||
                !string.Equals(entry["AuthorizationNumber"], reference.AuthorizationNumber, StringComparison.Ordinal) ||
                !string.Equals(entry["Version"], reference.TechnicalKeyVersion, StringComparison.Ordinal))
            {
                continue;
            }

            var value = entry["Value"];
            var supplierTaxId = entry["SupplierTaxId"];
            var qrValidationUrl = entry["QrValidationUrl"];
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(supplierTaxId) ||
                string.IsNullOrWhiteSpace(qrValidationUrl))
            {
                return Task.FromResult<FiscalVerificationMaterial?>(null);
            }

            return Task.FromResult<FiscalVerificationMaterial?>(
                new FiscalVerificationMaterial(
                    new FiscalTechnicalKey(value, reference.TechnicalKeyVersion),
                    supplierTaxId,
                    reference.Environment,
                    qrValidationUrl));
        }

        return Task.FromResult<FiscalVerificationMaterial?>(null);
    }
}

