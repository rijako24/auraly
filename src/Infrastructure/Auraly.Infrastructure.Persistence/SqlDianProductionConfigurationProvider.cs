using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDianProductionConfigurationProvider(
    SqlServerConnectionFactory connections) : IDianProductionConfigurationProvider
{
    public async Task<DianHabilitationConfiguration> ResolveAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DianEndpoint,CertificateProvider,CertificateKeyReference,CertificateThumbprint
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId AND IsActive=1 AND Environment=1;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "No active DIAN production issuer configuration exists for the business.");
        var endpoint = new Uri(reader.GetString(0), UriKind.Absolute);
        var certificate = new FiscalCertificateReference(
            businessId, reader.GetString(1), reader.GetString(2), reader.GetString(3));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException(
                "More than one active DIAN production issuer configuration exists.");
        return new DianHabilitationConfiguration(
            endpoint, certificate, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60), 10 * 1024 * 1024);
    }
}
