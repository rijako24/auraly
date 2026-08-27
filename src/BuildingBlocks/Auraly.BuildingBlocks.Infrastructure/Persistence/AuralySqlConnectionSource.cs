namespace Auraly.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Owns the SQL connection selected for the current runtime process.
/// Replacing this source is the only supported extension point for a future
/// database-routing policy; repositories and module factories must not read
/// connection strings from configuration independently.
/// </summary>
public sealed class AuralySqlConnectionSource
{
    public AuralySqlConnectionSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A SQL Server connection string is required.",
                nameof(connectionString));
        }

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
