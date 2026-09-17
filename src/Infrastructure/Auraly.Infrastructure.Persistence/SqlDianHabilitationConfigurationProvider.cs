using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDianHabilitationConfigurationProvider(
    SqlServerConnectionFactory connections) : IDianHabilitationConfigurationProvider
{
    private static readonly Uri HabilitationEndpoint = new(
        "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
        UriKind.Absolute);

    public async Task<DianHabilitationConfiguration> ResolveAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CertificateProvider, CertificateKeyReference,
                   CertificateThumbprint
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId AND IsActive=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "No active DIAN issuer certificate exists for the business.");

        var certificate = new FiscalCertificateReference(
            businessId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "More than one active DIAN issuer configuration exists for the business.");

        return new DianHabilitationConfiguration(
            HabilitationEndpoint,
            certificate,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60),
            10 * 1024 * 1024);
    }
}
