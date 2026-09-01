namespace Auraly.Infrastructure.Persistence;

/// <summary>
/// Verifies that the canonical SQL connection can be opened. This is a
/// readiness probe, not a second persistence path.
/// </summary>
public sealed class SqlDatabaseConnectivityProbe(
    SqlServerConnectionFactory connections)
{
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
    }
}
