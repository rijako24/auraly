using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<RestaurantAccountDraft?> GetDraftAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await LoadDraftAsync(connection, null, actor, accountId, ct);
    }

    public async Task<RestaurantAccountDraft> AddAsync(
        RestaurantAccountActor actor, Guid accountId,
        AddRestaurantAccountItemRequest request,
        IRestaurantProductCatalog products, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var prior = await FindAdditionAsync(connection, null, actor,
            request.AdditionId, ct);
        if (prior is not null)
            return await ReplayAsync(connection, null, actor, accountId,
                request, prior, ct);

        var account = await FindAsync(connection, null, actor, accountId,
            false, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open")
            throw new RestaurantAccountConflictException(
                "La cuenta ya no acepta productos.");
        var page = await products.SearchAsync(actor, accountId,
            account.WarehouseId, null, 0, 1, request.ProductId, null, ct);
        var product = page.Items.SingleOrDefault(item =>
            item.ProductId == request.ProductId)
            ?? throw new RestaurantAccountValidationException(
                "El producto no está disponible para esta mesa.");
        if (!product.AllowsFractionalSale &&
            request.Quantity != decimal.Truncate(request.Quantity))
            throw new RestaurantAccountValidationException(
                "Este producto solo admite cantidades enteras.");
        if (product.UnitPrice < 0 || product.PromotionDiscount < 0 ||
            product.UnitPrice + product.PromotionDiscount > 999999999999999m ||
            product.ProductCode.Length > 64 || product.Name.Length > 250 ||
            product.BaseUnitCode.Length > 24 || product.TaxCode.Length > 16 ||
            product.CurrencyCode.Length != 3 || product.PriceSource.Length > 24)
            throw new RestaurantAccountValidationException(
                "El producto no tiene un precio o presentación válida.");

        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var locked = await FindAsync(connection, transaction, actor,
            accountId, true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        prior = await FindAdditionAsync(connection, transaction, actor,
            request.AdditionId, ct);
        if (prior is not null)
        {
            var replay = await ReplayAsync(connection, transaction,
                actor, accountId, request, prior, ct);
            await transaction.CommitAsync(ct);
            return replay;
        }
        if (locked.Status != "Open" || locked.Version != request.ExpectedVersion)
            throw new RestaurantAccountConflictException(
                "La cuenta cambió. Revisa los productos antes de continuar.");

        const string addSql = """
            INSERT sales.RestaurantAccountAdditions
                (AdditionId,TenantId,BusinessId,AccountId,ProductId,
                 ReplacementOfCancellationId,
                 ProductCode,Description,UnitCode,TaxCode,TaxRate,Quantity,
                 OriginalQuantity,
                 AllowsFractionalSale,
                 PublicUnitPrice,PromotionDiscountPerUnit,CurrencyCode,
                 PriceSource,AddedByUserId,AddedByName,AddedAt,UpdatedAt,
                 Status,ManageStockAtSend,ProductRecipeVersionId)
            SELECT @AdditionId,@TenantId,@BusinessId,@AccountId,@ProductId,
                   @ReplacementOfCancellationId,
                   COALESCE(original.ProductCode,@ProductCode),
                   COALESCE(original.Description,@Description),
                   COALESCE(original.UnitCode,@UnitCode),
                   COALESCE(original.TaxCode,@TaxCode),
                   COALESCE(original.TaxRate,@TaxRate),@Quantity,
                   @Quantity,
                   COALESCE(original.AllowsFractionalSale,@AllowsFractionalSale),
                   COALESCE(original.PublicUnitPrice,@PublicUnitPrice),
                   COALESCE(original.PromotionDiscountPerUnit,@PromotionDiscount),
                   COALESCE(original.CurrencyCode,@CurrencyCode),
                   COALESCE(original.PriceSource,@PriceSource),[user].UserId,
                   LEFT(CONCAT([user].FirstName,N' ',[user].LastName),201),
                   @Now,@Now,
                   CASE WHEN EXISTS (
                     SELECT 1 FROM sales.RestaurantProductRoutes route
                     WHERE route.BusinessId=@BusinessId
                       AND route.ProductId=@ProductId)
                     THEN N'Pending' ELSE N'Direct' END,
                   product.ManageStock,
                   CASE WHEN product.ManageStock=0 THEN (
                     SELECT TOP(1) versionRow.ProductRecipeVersionId
                     FROM dbo.ProductRecipeVersions versionRow
                     WHERE versionRow.TenantId=@TenantId
                       AND versionRow.ProductId=@ProductId
                       AND versionRow.IsEnabled=1
                     ORDER BY versionRow.[Version] DESC
                   ) ELSE NULL END
            FROM dbo.AppUsers [user]
            LEFT JOIN parties.Waiters waiter
              ON waiter.TenantId=[user].TenantId
             AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
            JOIN dbo.Products product WITH (UPDLOCK,HOLDLOCK)
              ON product.TenantId=@TenantId AND product.ProductId=@ProductId
             AND product.IsActive=1 AND product.IsIngredient=0
            LEFT JOIN sales.RestaurantCancellationRequests replacement
              ON replacement.CancellationId=@ReplacementOfCancellationId
             AND replacement.TenantId=@TenantId
             AND replacement.BusinessId=@BusinessId
             AND replacement.AccountId=@AccountId
             AND replacement.Status=N'Approved'
             AND replacement.PreparationState IN (N'Prepared',N'Partial')
             AND replacement.Quantity=@Quantity
            LEFT JOIN sales.RestaurantAccountAdditions original
              ON original.AdditionId=replacement.AdditionId
             AND original.AccountId=@AccountId
             AND original.ProductId=@ProductId
             AND original.UnitCode=@UnitCode
             AND original.CurrencyCode=@CurrencyCode
            WHERE [user].UserId=@UserId AND [user].TenantId=@TenantId
              AND [user].IsActive=1
              AND (@CanOpenWithoutWaiter=1 OR waiter.PartyId IS NOT NULL)
              AND (@ReplacementOfCancellationId IS NULL
                   OR original.AdditionId IS NOT NULL);
            """;
        await using (var command = new SqlCommand(addSql, connection, transaction))
        {
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AdditionId", request.AdditionId);
            command.Parameters.AddWithValue("@AccountId", accountId);
            command.Parameters.AddWithValue("@ProductId", request.ProductId);
            command.Parameters.Add("@ReplacementOfCancellationId", SqlDbType.UniqueIdentifier)
                .Value = (object?)request.ReplacementOfCancellationId ?? DBNull.Value;
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.Add("@ProductCode", SqlDbType.NVarChar, 64).Value = product.ProductCode;
            command.Parameters.Add("@Description", SqlDbType.NVarChar, 250).Value = product.Name;
            command.Parameters.Add("@UnitCode", SqlDbType.NVarChar, 24).Value = product.BaseUnitCode;
            command.Parameters.Add("@TaxCode", SqlDbType.NVarChar, 16).Value = product.TaxCode;
            command.Parameters.Add("@TaxRate", SqlDbType.Decimal).Value = product.TaxRate;
            command.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = request.Quantity;
            command.Parameters.Add("@AllowsFractionalSale", SqlDbType.Bit).Value =
                product.AllowsFractionalSale;
            command.Parameters.Add("@PublicUnitPrice", SqlDbType.Decimal).Value =
                product.UnitPrice + product.PromotionDiscount;
            command.Parameters.Add("@PromotionDiscount", SqlDbType.Decimal).Value = product.PromotionDiscount;
            command.Parameters.Add("@CurrencyCode", SqlDbType.Char, 3).Value = product.CurrencyCode;
            command.Parameters.Add("@PriceSource", SqlDbType.NVarChar, 24).Value = product.PriceSource;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            int inserted;
            try { inserted = await command.ExecuteNonQueryAsync(ct); }
            catch (SqlException error) when ((error.Number is 2601 or 2627) &&
                request.ReplacementOfCancellationId is not null)
            { throw new RestaurantAccountConflictException(
                "Este plato ya tiene un reemplazo registrado."); }
            if (inserted != 1)
            {
                if (request.ReplacementOfCancellationId is not null)
                    throw new RestaurantAccountValidationException(
                        "La cancelación ya no permite rehacer este producto o cambió su presentación.");
                throw new RestaurantAccountForbiddenException(
                    "El mesero ya no está habilitado para atender.");
            }
        }
        const string advanceSql = """
            UPDATE sales.RestaurantAccounts
            SET Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Status=N'Open'
              AND Version=@Version;
            """;
        await using (var command = new SqlCommand(advanceSql, connection, transaction))
        {
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AccountId", accountId);
            command.Parameters.AddWithValue("@Version", request.ExpectedVersion);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            if (await command.ExecuteNonQueryAsync(ct) != 1)
                throw new RestaurantAccountConflictException(
                    "La cuenta cambió antes de guardar el producto.");
        }
        var draft = await LoadDraftAsync(connection, transaction,
            actor, accountId, ct) ?? throw new InvalidDataException(
                "La cuenta desapareció durante la captura.");
        await AuditAdditionAsync(connection, transaction, actor,
            draft.Additions.Single(item => item.AdditionId == request.AdditionId),
            draft.Account.Version, now, ct);
        await transaction.CommitAsync(ct);
        return draft;
    }

    private static async Task<RestaurantAccountDraft> ReplayAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid accountId,
        AddRestaurantAccountItemRequest request,
        RestaurantAccountAddition prior, CancellationToken ct)
    {
        const string originalSql = """
            SELECT OriginalQuantity,ReplacementOfCancellationId
            FROM sales.RestaurantAccountAdditions
            WHERE AdditionId=@AdditionId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND AccountId=@AccountId;
            """;
        await using var original = new SqlCommand(originalSql, connection, transaction);
        AddScope(original, actor);
        original.Parameters.AddWithValue("@AdditionId", request.AdditionId);
        original.Parameters.AddWithValue("@AccountId", accountId);
        await using var reader = await original.ExecuteReaderAsync(ct);
        var found = await reader.ReadAsync(ct);
        var originalQuantity = found ? reader.GetDecimal(0) : (decimal?)null;
        var replacementId = found && !reader.IsDBNull(1) ? reader.GetGuid(1) : (Guid?)null;
        if (prior.AccountId != accountId || prior.ProductId != request.ProductId ||
            originalQuantity != request.Quantity ||
            replacementId != request.ReplacementOfCancellationId ||
            prior.AddedByUserId != actor.UserId)
            throw new RestaurantAccountConflictException(
                "La identidad de incorporación ya pertenece a otro producto.");
        await reader.DisposeAsync();
        return await LoadDraftAsync(connection, transaction,
            actor, accountId, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
    }

    private static async Task<RestaurantAccountDraft?> LoadDraftAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        var account = await FindAsync(connection, transaction, actor,
            accountId, false, ct);
        if (account is null) return null;
        const string sql = """
            SELECT addition.AdditionId,addition.AccountId,addition.ProductId,
                   addition.ProductCode,addition.Description,addition.UnitCode,
                   addition.TaxCode,addition.TaxRate,addition.Quantity,
                   addition.AllowsFractionalSale,addition.PublicUnitPrice,
                   addition.PromotionDiscountPerUnit,addition.CurrencyCode,
                   addition.PriceSource,addition.AddedByUserId,
                   addition.AddedByName,addition.AddedAt,addition.Status,
                   CASE WHEN addition.Status=N'Sent' THEN card.StationId
                        ELSE station.StationId END,
                   CASE WHEN addition.Status=N'Sent' THEN card.StationName
                        ELSE station.Name END,
                   addition.PreparationNote,
                   addition.ReplacementOfCancellationId
            FROM sales.RestaurantAccountAdditions addition
            LEFT JOIN sales.RestaurantProductRoutes route
              ON route.BusinessId=addition.BusinessId
             AND route.ProductId=addition.ProductId
            LEFT JOIN sales.RestaurantStations station
              ON station.BusinessId=route.BusinessId
             AND station.StationId=route.StationId AND station.IsActive=1
            LEFT JOIN sales.RestaurantCommandLines line
              ON line.AdditionId=addition.AdditionId
            LEFT JOIN sales.RestaurantCommandCards card
              ON card.CardId=line.CardId
            WHERE addition.TenantId=@TenantId
              AND addition.BusinessId=@BusinessId
              AND addition.AccountId=@AccountId
            ORDER BY addition.AddedAt,addition.AdditionId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", accountId);
        var additions = new List<RestaurantAccountAddition>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) additions.Add(ReadAddition(reader));
        return new(account, additions);
    }

    private static async Task<RestaurantAccountAddition?> FindAdditionAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid additionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT addition.AdditionId,addition.AccountId,addition.ProductId,
                   addition.ProductCode,addition.Description,addition.UnitCode,
                   addition.TaxCode,addition.TaxRate,addition.Quantity,
                   addition.AllowsFractionalSale,
                   addition.PublicUnitPrice,addition.PromotionDiscountPerUnit,
                   addition.CurrencyCode,addition.PriceSource,
                   addition.AddedByUserId,addition.AddedByName,
                   addition.AddedAt,addition.Status,
                   NULL, NULL, addition.PreparationNote,
                   addition.ReplacementOfCancellationId
            FROM sales.RestaurantAccountAdditions addition
            WHERE addition.AdditionId=@AdditionId
              AND addition.TenantId=@TenantId
              AND addition.BusinessId=@BusinessId
              AND EXISTS(
                SELECT 1 FROM dbo.AppUsers [user]
                LEFT JOIN parties.Waiters waiter
                  ON waiter.TenantId=[user].TenantId
                 AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
                WHERE [user].UserId=@UserId AND [user].TenantId=@TenantId
                  AND [user].IsActive=1
                  AND (@CanOpenWithoutWaiter=1 OR waiter.PartyId IS NOT NULL));
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AdditionId", additionId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAddition(reader) : null;
    }

    private static RestaurantAccountAddition ReadAddition(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.GetString(6), reader.GetDecimal(7), reader.GetDecimal(8),
        reader.GetBoolean(9), reader.GetDecimal(10), reader.GetDecimal(11),
        reader.GetString(12), reader.GetString(13), reader.GetGuid(14),
        reader.GetString(15), reader.GetDateTimeOffset(16),
        reader.GetString(17),
        reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetGuid(18) : null,
        reader.FieldCount > 19 && !reader.IsDBNull(19) ? reader.GetString(19) : null,
        reader.FieldCount > 20 && !reader.IsDBNull(20) ? reader.GetString(20) : null,
        reader.FieldCount > 21 && !reader.IsDBNull(21) ? reader.GetGuid(21) : null);

    private static async Task AuditAdditionAsync(SqlConnection connection,
        SqlTransaction transaction, RestaurantAccountActor actor,
        RestaurantAccountAddition addition, long version,
        DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            INSERT dbo.AuditLogs
                (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
                 EntityId,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantAccountAdditionCreated',N'RestaurantAccount',
                   @EntityId,@NewValues,@CorrelationId,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100)
            .Value = addition.AccountId.ToString();
        command.Parameters.Add("@NewValues", SqlDbType.NVarChar, -1)
            .Value = JsonSerializer.Serialize(new { Addition = addition, Version = version,
                actor.ConnectedUserId, actor.CredentialVerified });
        command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100)
            .Value = addition.AdditionId.ToString();
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(ct);
    }
}
