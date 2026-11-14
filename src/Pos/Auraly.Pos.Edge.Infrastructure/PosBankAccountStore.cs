using System.Text.Json;
using Auraly.Contracts.Catalog;
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
    IReadOnlyList<PosBankAccount> BankAccounts,
    PaymentMethodCatalogSnapshot? PaymentMethods = null);

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
                INSERT INTO PosSettlementConfiguration(ConfigurationId,IsAccountingEnabled,PaymentMethodsJson)
                VALUES(1,$enabled,$forms)
                ON CONFLICT(ConfigurationId) DO UPDATE SET IsAccountingEnabled=excluded.IsAccountingEnabled,PaymentMethodsJson=excluded.PaymentMethodsJson;
                """;
            state.Parameters.AddWithValue("$enabled", configuration.IsAccountingEnabled);
            state.Parameters.AddWithValue("$forms",configuration.PaymentMethods is null ? DBNull.Value : JsonSerializer.Serialize(configuration.PaymentMethods));
            await state.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM PosBankAccounts;";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var insert=connection.CreateCommand())
        {
            insert.Transaction=(SqliteTransaction)transaction;
            insert.CommandText="""
                INSERT INTO PosBankAccounts(BankAccountId,DisplayName,BankName,AccountNumber,AccountTypeName,IsPrimary,RowVersion)
                SELECT json_extract(value,'$.BankAccountId'),json_extract(value,'$.DisplayName'),
                  json_extract(value,'$.BankName'),json_extract(value,'$.AccountNumber'),
                  json_extract(value,'$.AccountTypeName'),json_extract(value,'$.IsPrimary'),json_extract(value,'$.RowVersion')
                FROM json_each($accounts);
                """;
            insert.Parameters.AddWithValue("$accounts",JsonSerializer.Serialize(configuration.BankAccounts));
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
        bool enabled;
        PaymentMethodCatalogSnapshot? forms;
        state.CommandText="SELECT IsAccountingEnabled,PaymentMethodsJson FROM PosSettlementConfiguration WHERE ConfigurationId=1;";
        await using(var configuration=await state.ExecuteReaderAsync(cancellationToken))
        {
            if(!await configuration.ReadAsync(cancellationToken)) throw new InvalidOperationException("Falta la configuración de caja.");
            enabled=configuration.GetInt64(0)==1;
            forms=configuration.IsDBNull(1)?null:JsonSerializer.Deserialize<PaymentMethodCatalogSnapshot>(configuration.GetString(1));
        }
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
        return new(enabled, values, forms);
    }
    private static async Task EnsureSettlementSchemaAsync(SqliteConnection connection,CancellationToken cancellationToken)
    {
        await using var info=connection.CreateCommand();
        info.CommandText="PRAGMA table_info(PosSettlementConfiguration);";
        bool exists=false;
        await using(var reader=await info.ExecuteReaderAsync(cancellationToken))
            while(await reader.ReadAsync(cancellationToken)) exists|=reader.GetString(1)=="PaymentMethodsJson";
        if(exists)return;
        await using var alter=connection.CreateCommand();
        alter.CommandText="ALTER TABLE PosSettlementConfiguration ADD COLUMN PaymentMethodsJson TEXT NULL;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

}
