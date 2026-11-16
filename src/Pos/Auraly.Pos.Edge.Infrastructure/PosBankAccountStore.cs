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
    public async Task<PaymentMethodPage> PaymentMethodsPageAsync(PaymentMethodQuery query, CancellationToken ct = default)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            ((long)query.Page - 1) * query.PageSize > int.MaxValue || query.Search?.Length > 160 ||
            query.MethodCode?.Length > 32 || query.Usage != "Sales")
            throw new ArgumentException("Los filtros de formas de pago no son válidos.");
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(deferred: true);
        const string filtered = """
            FROM PosSettlementConfiguration state,json_each(state.PaymentMethodsJson,'$.Forms') entry
            WHERE state.ConfigurationId=1 AND json_extract(entry.value,'$.IsActive')=1
              AND json_extract(entry.value,'$.UseForSales')=1
              AND ($method IS NULL OR json_extract(entry.value,'$.MethodCode')=$method)
              AND ($search IS NULL OR json_extract(entry.value,'$.Code') LIKE $search
                OR json_extract(entry.value,'$.Name') LIKE $search)
            """;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT json_extract(PaymentMethodsJson,'$.ContractVersion'),(SELECT COUNT(*) {filtered})
            FROM PosSettlementConfiguration WHERE ConfigurationId=1;
            SELECT entry.value {filtered}
            ORDER BY json_extract(entry.value,'$.SortOrder'),json_extract(entry.value,'$.Code'),json_extract(entry.value,'$.PaymentMethodId')
            LIMIT $take OFFSET $skip;
            """;
        command.Parameters.AddWithValue("$method", (object?)query.MethodCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", string.IsNullOrWhiteSpace(query.Search) ? DBNull.Value : $"%{query.Search.Trim()}%");
        command.Parameters.AddWithValue("$take", query.PageSize);
        command.Parameters.AddWithValue("$skip", checked((query.Page - 1) * query.PageSize));
        var values = new List<PaymentMethodView>();
        int total;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) ||
                reader.GetInt32(0) != PaymentMethodCatalogSnapshot.CurrentContractVersion)
                throw new InvalidDataException("Actualiza la preparación del catálogo de formas de pago.");
            total = reader.GetInt32(1);
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
                values.Add(JsonSerializer.Deserialize<PaymentMethodView>(reader.GetString(0))
                    ?? throw new InvalidDataException("La forma de pago local no es válida."));
        }
        await transaction.CommitAsync(ct);
        return new(values,query.Page,query.PageSize,total);
    }

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
