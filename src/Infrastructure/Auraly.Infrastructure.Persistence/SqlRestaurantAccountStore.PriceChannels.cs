using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<IReadOnlyList<RestaurantPriceChannelOption>> PriceChannelsAsync(RestaurantAccountActor actor, CancellationToken ct)
    {
        const string sql = """
            SELECT channel.PriceChannelId,channel.Name FROM dbo.PriceChannels channel
            JOIN dbo.Businesses business ON business.TenantId=channel.TenantId
            WHERE business.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND business.PointOfSaleMode=N'RestaurantBar' AND channel.IsActive=1
            ORDER BY channel.Name,channel.PriceChannelId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        AddScope(command, actor);
        var result = new List<RestaurantPriceChannelOption>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }

    public async Task<RestaurantAccountDraft> ChangePriceChannelAsync(RestaurantAccountActor actor, Guid accountId,
        ChangeRestaurantPriceChannelRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await LockChangeableAccountAsync(connection, transaction, actor, accountId, request.ExpectedVersion, ct);
        if (account.CustomerPriceChannelLocked)
            throw new RestaurantAccountConflictException("El canal pertenece al cliente seleccionado. Cambia o quita el cliente para elegir otro canal.");
        if (request.PriceChannelId is { } channelId && !await IsActiveChannelAsync(connection, transaction, actor, channelId, ct))
            throw new RestaurantAccountValidationException("El canal de precios ya no está activo.");
        if (account.PriceChannelId != request.PriceChannelId)
        {
            await UpdateAccountPricingAsync(connection, transaction, actor, accountId, request.ExpectedVersion,
                request.PriceChannelId, account.CustomerId, account.CustomerPartySiteId,
                false, now, ct);
            await RepriceAccountAsync(connection, transaction, actor, accountId, account.WarehouseId, request.PriceChannelId, now, ct);
        }
        var draft = await LoadDraftAsync(connection, transaction, actor, accountId, ct)
            ?? throw new RestaurantAccountConflictException("La cuenta ya no está disponible.");
        await transaction.CommitAsync(ct);
        return draft;
    }

    public async Task<RestaurantAccountDraft> ChangeCustomerAsync(RestaurantAccountActor actor, Guid accountId,
        ChangeRestaurantCustomerRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = await LockChangeableAccountAsync(connection, transaction, actor, accountId, request.ExpectedVersion, ct);
        Guid? customerChannelId = null;
        Guid? selectedSiteId = null;
        if (request.CustomerId is { } customerId)
        {
            const string customerSql = """
                SELECT CASE WHEN channel.IsActive=1
                    AND (setting.ValidFrom IS NULL OR setting.ValidFrom<=@Now)
                    AND (setting.ValidUntil IS NULL OR setting.ValidUntil>@Now)
                    THEN setting.PriceChannelId END,
                    site.PartySiteId
                FROM dbo.Customers customer
                JOIN dbo.Parties party ON party.PartyId=customer.PartyId
                  AND party.TenantId=customer.TenantId AND party.IsActive=1
                LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=customer.CustomerId
                LEFT JOIN dbo.PriceChannels channel ON channel.PriceChannelId=setting.PriceChannelId
                  AND channel.TenantId=customer.TenantId
                OUTER APPLY (SELECT TOP(1) candidate.PartySiteId FROM dbo.PartySites candidate
                  WHERE candidate.PartyId=customer.PartyId AND candidate.IsActive=1
                    AND (@CustomerPartySiteId IS NULL OR candidate.PartySiteId=@CustomerPartySiteId)
                  ORDER BY candidate.IsPrimary DESC,candidate.Name,candidate.PartySiteId) site
                WHERE customer.CustomerId=@CustomerId AND customer.TenantId=@TenantId
                  AND customer.IsActive=1
                  AND (@CustomerPartySiteId IS NULL OR site.PartySiteId IS NOT NULL);
                """;
            await using var command = new SqlCommand(customerSql, connection, transaction);
            command.Parameters.AddWithValue("@CustomerId", customerId);
            command.Parameters.AddWithValue("@TenantId", actor.TenantId);
            command.Parameters.Add("@CustomerPartySiteId", SqlDbType.UniqueIdentifier).Value =
                (object?)request.CustomerPartySiteId ?? DBNull.Value;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new RestaurantAccountValidationException("El cliente o su sede no está disponible.");
            customerChannelId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            selectedSiteId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            if (selectedSiteId is null)
                throw new RestaurantAccountValidationException(
                    "El cliente necesita una sede activa para facturar esta mesa.");
        }
        var selectedChannelId = request.CustomerId is null
            ? account.CustomerPriceChannelLocked ? null : account.PriceChannelId
            : customerChannelId ?? (account.CustomerPriceChannelLocked ? null : account.PriceChannelId);
        var selectedFromCustomer = request.CustomerId is not null && customerChannelId is not null;
        if (account.CustomerId != request.CustomerId || account.CustomerPartySiteId != selectedSiteId ||
            account.PriceChannelId != selectedChannelId ||
            account.CustomerPriceChannelLocked != selectedFromCustomer)
        {
            await UpdateAccountPricingAsync(connection, transaction, actor, accountId, request.ExpectedVersion,
                selectedChannelId, request.CustomerId, selectedSiteId,
                selectedFromCustomer, now, ct);
            await RepriceAccountAsync(connection, transaction, actor, accountId, account.WarehouseId, selectedChannelId, now, ct);
        }
        var draft = await LoadDraftAsync(connection, transaction, actor, accountId, ct)
            ?? throw new RestaurantAccountConflictException("La cuenta ya no está disponible.");
        await transaction.CommitAsync(ct);
        return draft;
    }

    private async Task<RestaurantAccount> LockChangeableAccountAsync(SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, long expectedVersion, CancellationToken ct)
    {
        var account = await FindAsync(connection, transaction, actor, accountId, true, ct)
            ?? throw new RestaurantAccountConflictException("La cuenta ya no está disponible.");
        if (account.Status != "Open" || account.Version != expectedVersion)
            throw new RestaurantAccountConflictException("La cuenta cambió. Vuelve a abrirla antes de modificar sus precios.");
        const string paidSql = """
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1 FROM sales.RestaurantAccountCollections collectionValue
                WHERE collectionValue.AccountId=@AccountId AND collectionValue.BusinessId=@BusinessId
                  AND collectionValue.TenantId=@TenantId AND collectionValue.Status=N'Captured')
                THEN 1 ELSE 0 END AS bit);
            """;
        await using var command = new SqlCommand(paidSql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", accountId);
        if ((bool)(await command.ExecuteScalarAsync(ct) ?? false))
            throw new RestaurantAccountConflictException("Esta mesa ya tiene pagos registrados. Revierte los pagos antes de cambiar cliente o canal.");
        return account;
    }

    private static async Task<bool> IsActiveChannelAsync(SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid channelId, CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.PriceChannels
                WHERE PriceChannelId=@PriceChannelId AND TenantId=@TenantId AND IsActive=1)
                THEN 1 ELSE 0 END AS bit);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@PriceChannelId", channelId);
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        return (bool)(await command.ExecuteScalarAsync(ct) ?? false);
    }

    private static async Task UpdateAccountPricingAsync(SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, long expectedVersion, Guid? channelId,
        Guid? customerId, Guid? siteId, bool isCustomerPriceChannel,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE sales.RestaurantAccounts
            SET PriceChannelId=@PriceChannelId,CustomerId=@CustomerId,CustomerPartySiteId=@CustomerPartySiteId,
                IsCustomerPriceChannel=@IsCustomerPriceChannel,
                Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND Status=N'Open' AND Version=@ExpectedVersion;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", accountId);
        command.Parameters.AddWithValue("@ExpectedVersion", expectedVersion);
        command.Parameters.Add("@PriceChannelId", SqlDbType.UniqueIdentifier).Value = (object?)channelId ?? DBNull.Value;
        command.Parameters.Add("@CustomerId", SqlDbType.UniqueIdentifier).Value = (object?)customerId ?? DBNull.Value;
        command.Parameters.Add("@CustomerPartySiteId", SqlDbType.UniqueIdentifier).Value = (object?)siteId ?? DBNull.Value;
        command.Parameters.Add("@IsCustomerPriceChannel", SqlDbType.Bit).Value = isCustomerPriceChannel;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new RestaurantAccountConflictException("La cuenta cambió antes de actualizar sus precios.");
    }

    private static async Task RepriceAccountAsync(SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, Guid warehouseId, Guid? channelId,
        DateTimeOffset now, CancellationToken ct)
    {
        const string additionsSql = """
            SELECT AdditionId,ProductId,Quantity
            FROM sales.RestaurantAccountAdditions WITH (UPDLOCK,HOLDLOCK)
            WHERE AccountId=@AccountId AND BusinessId=@BusinessId AND TenantId=@TenantId
              AND Status<>N'Cancelled';
            """;
        var requests = new List<CommercePriceRequest>();
        await using (var command = new SqlCommand(additionsSql, connection, transaction))
        {
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AccountId", accountId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                requests.Add(new(reader.GetGuid(0).ToString("N"), reader.GetGuid(1), reader.GetDecimal(2)));
        }
        if (requests.Count == 0) return;
        var prices = await SqlOnlineSalesDraftStore.ResolveCommercePricesAsync(
            connection, transaction, actor.BusinessId, warehouseId, null, requests, ct,
            independentLines: true, selectedChannelId: channelId);
        var updates = requests.Select(request =>
        {
            var price = prices[request.Key];
            var discountPerUnit = decimal.Round(price.PromotionDiscount / request.Quantity, 4,
                MidpointRounding.AwayFromZero);
            return new { additionId = Guid.ParseExact(request.Key, "N"),
                publicUnitPrice = price.ReferenceUnitPrice,
                promotionDiscountPerUnit = discountPerUnit, priceSource = price.PriceSource };
        }).ToArray();
        const string updateSql = """
            UPDATE addition SET PublicUnitPrice=price.PublicUnitPrice,
                PromotionDiscountPerUnit=price.PromotionDiscountPerUnit,
                PriceSource=price.PriceSource,UpdatedAt=@Now
            FROM sales.RestaurantAccountAdditions addition
            JOIN OPENJSON(@PricesJson) WITH (
                AdditionId UNIQUEIDENTIFIER '$.additionId',
                PublicUnitPrice DECIMAL(19,4) '$.publicUnitPrice',
                PromotionDiscountPerUnit DECIMAL(19,4) '$.promotionDiscountPerUnit',
                PriceSource NVARCHAR(24) '$.priceSource') price
              ON price.AdditionId=addition.AdditionId
            WHERE addition.AccountId=@AccountId AND addition.BusinessId=@BusinessId
              AND addition.TenantId=@TenantId AND addition.Status<>N'Cancelled';
            """;
        await using var update = new SqlCommand(updateSql, connection, transaction);
        AddScope(update, actor);
        update.Parameters.AddWithValue("@AccountId", accountId);
        update.Parameters.Add("@PricesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(updates);
        update.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        if (await update.ExecuteNonQueryAsync(ct) != updates.Length)
            throw new RestaurantAccountConflictException("La cuenta cambió durante el recálculo de precios.");
    }
}
