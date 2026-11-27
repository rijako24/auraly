using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Ubl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.Infrastructure.Fiscal;

public static class FiscalProcessingComposition
{
    public static void AddFiscalProcessingExecution<TProtectedVault>(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useProtectedDatabaseByDefault)
        where TProtectedVault : class, IFiscalCredentialVault
    {
        var fiscalCredentialStore = configuration["Auraly:Fiscal:CredentialStore"]
            ?? (useProtectedDatabaseByDefault
                ? "ProtectedDatabase"
                : "AzureKeyVault");
        if (string.Equals(fiscalCredentialStore, "AzureKeyVault", StringComparison.OrdinalIgnoreCase))
        {
            var keyVaultUri = configuration["Auraly:Fiscal:KeyVaultUri"];
            if (!Uri.TryCreate(keyVaultUri, UriKind.Absolute, out var vaultUri) ||
                vaultUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException(
                    "Auraly:Fiscal:KeyVaultUri must be an HTTPS Azure Key Vault URI when CredentialStore is AzureKeyVault.");
            services.AddSingleton<IFiscalCredentialVault>(services =>
                new RoutedFiscalCredentialVault(
                    AzureKeyVaultFiscalCredentialVault.Create(vaultUri),
                    services.GetRequiredService<TProtectedVault>()));
        }
        else if (string.Equals(fiscalCredentialStore, "ProtectedDatabase", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFiscalCredentialVault>(provider => provider.GetRequiredService<TProtectedVault>());
        }
        else
        {
            throw new InvalidOperationException(
                "Auraly:Fiscal:CredentialStore must be AzureKeyVault or ProtectedDatabase.");
        }
        services.AddSingleton<EnvironmentFiscalSoftwarePinProvider>();
        services.AddSingleton<WindowsFiscalSigningCertificateProvider>();
        services.AddSingleton<IFiscalSoftwarePinProvider, ManagedFiscalSoftwarePinProvider>();
        services.AddSingleton<IFiscalSigningCertificateProvider, ManagedFiscalSigningCertificateProvider>();
        services.AddSingleton<IFiscalXmlSigner, DianXadesSigner>();
        services.AddSingleton<IDianWcfClientFactory, DianWcfClientFactory>();
        services.AddScoped<IDianHabilitationTransport, DianHabilitationTransport>();
        services.AddScoped<IDianProductionTransport, DianProductionTransport>();
        services.AddSingleton<DianInvoiceUblBuilder>();
        services.AddSingleton<DianSupportDocumentUblBuilder>();
        services.AddSingleton<DianCreditNoteUblBuilder>();
        services.AddSingleton<DianDebitNoteUblBuilder>();
        services.AddSingleton<DianAttachedDocumentBuilder>();
        services.AddSingleton<DianSchemaValidator>();
        services.AddSingleton<DianPayrollXmlBuilder>();
        services.AddSingleton<DianPayrollSchemaValidator>();
        services.AddSingleton<FiscalSubmissionPackageBuilder>();
        services.AddScoped<FiscalGenerationWorker>();
        services.AddScoped<FiscalSubmissionWorker>();
        services.AddScoped<FiscalProcessingMessageHandler>();
    }
}
