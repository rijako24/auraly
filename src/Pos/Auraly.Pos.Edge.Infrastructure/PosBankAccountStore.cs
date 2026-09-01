using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosBankAccount(
    Guid BankAccountId,
    string DisplayName,
    string BankName,
    string AccountNumber,
    string AccountTypeName,
    bool IsPrimary,
    string RowVersion);

public sealed record PosAccountingSettlementConfiguration(
    bool IsAccountingEnabled,
    IReadOnlyList<PosBankAccount> BankAccounts);

public sealed partial class PosCatalogStore
{
    public async Task ApplySettlementConfigurationAsync(
        PosAccountingSettlementConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var state = connection.CreateCommand())
        {
            state.Transaction = (SqliteTransaction)transaction;
            state.CommandText = """
                INSERT INTO PosSettlementConfiguration(ConfigurationId,IsAccountingEnabled)
                VALUES(1,$enabled)
                ON CONFLICT(ConfigurationId) DO UPDATE SET IsAccountingEnabled=excluded.IsAccountingEnabled;
                """;
            state.Parameters.AddWithValue("$enabled", configuration.IsAccountingEnabled);
            await state.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM PosBankAccounts;";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var account in configuration.BankAccounts)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO PosBankAccounts(BankAccountId,DisplayName,BankName,AccountNumber,
                    AccountTypeName,IsPrimary,RowVersion)
                VALUES($id,$display,$bank,$number,$type,$primary,$version);
                """;
            insert.Parameters.AddWithValue("$id", account.BankAccountId.ToString("D"));
            insert.Parameters.AddWithValue("$display", account.DisplayName);
            insert.Parameters.AddWithValue("$bank", account.BankName);
            insert.Parameters.AddWithValue("$number", account.AccountNumber);
            insert.Parameters.AddWithValue("$type", account.AccountTypeName);
            insert.Parameters.AddWithValue("$primary", account.IsPrimary);
            insert.Parameters.AddWithValue("$version", account.RowVersion);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PosAccountingSettlementConfiguration> SettlementConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var state = connection.CreateCommand();
        state.CommandText = "SELECT IsAccountingEnabled FROM PosSettlementConfiguration WHERE ConfigurationId=1;";
        var enabled = Convert.ToInt64(await state.ExecuteScalarAsync(cancellationToken)) == 1;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BankAccountId,DisplayName,BankName,AccountNumber,AccountTypeName,
                   IsPrimary,RowVersion
            FROM PosBankAccounts ORDER BY IsPrimary DESC,DisplayName,BankAccountId;
            """;
        var values = new List<PosBankAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetInt64(5) == 1, reader.GetString(6)));
        return new(enabled, values);
    }
}
