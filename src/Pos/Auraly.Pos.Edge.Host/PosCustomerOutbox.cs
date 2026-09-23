using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Parties;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

public sealed record PosCustomerOutboxStatus(
    int PendingCount,
    DateTimeOffset? OldestPendingAt,
    string? LastError);

public sealed class PosCustomerOutboxStore(
    string connectionString,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    PosOperationalScope scope)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PosCustomerPricing> QueueAsync(
        PosCreateCustomerInput input,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString, cancellationToken);
        var customerId = ids.NewId();
        var partySiteId = ids.NewId();
        var request = new CreateCustomerRequest(
            customerId,
            scope.BusinessId,
            new PartyInput(
                input.PartyType,
                input.IdentificationCountryId,
                input.IdentificationTypeCode,
                input.Identification,
                input.VerificationDigit,
                input.DisplayName,
                input.LegalName,
                input.FirstName,
                input.LastName,
                input.Email,
                input.Phone),
            input.PrimarySite,
            null,
            RequestedCustomerId: customerId,
            RequestedPrimarySiteId: partySiteId);
        var now = timeProvider.GetUtcNow();
        var local = new PosCustomerPricing(
            customerId,
            input.Identification.Trim(),
            input.DisplayName.Trim(),
            null,
            true,
            false,
            Sites: [new PosCustomerSite(
                partySiteId, input.PrimarySite.Code.Trim(), input.PrimarySite.Name.Trim(),
                input.PrimarySite.AddressLine.Trim(), input.PrimarySite.Phone, true)],
            PartySiteId: partySiteId,
            SiteName: input.PrimarySite.Name.Trim(),
            SiteAddress: input.PrimarySite.AddressLine.Trim());

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = transaction;
            duplicate.CommandText = """
                SELECT COUNT(1) FROM PosPricingCustomers
                WHERE IsActive=1 AND Identification=$identification;
                """;
            duplicate.Parameters.AddWithValue("$identification", local.Identification);
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
                throw new InvalidOperationException(
                    "Ya existe un cliente local con esa identificación.");
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PosPricingCustomers(
                  CustomerId,Identification,Name,PriceChannelId,RequiresElectronicInvoice,
                  IsActive,AppliesWithholding,TaxResponsibilities,TaxJurisdictionCode,
                  SitesJson,IsPendingLocal)
                VALUES($customer,$identification,$name,NULL,$electronic,1,0,'[]',NULL,
                       $sites,1);
                INSERT INTO Outbox(
                  MessageId,DocumentId,WorkSessionId,Type,Payload,Status,AttemptCount,CreatedAt)
                VALUES($customer,$customer,$session,$type,$payload,'Pending',0,$now);
                """;
            insert.Parameters.AddWithValue("$customer", customerId.ToString("D"));
            insert.Parameters.AddWithValue("$identification", local.Identification);
            insert.Parameters.AddWithValue("$name", local.Name);
            insert.Parameters.AddWithValue("$electronic", local.RequiresElectronicInvoice ? 1 : 0);
            // SitesJson is queried locally with the CLR property names (PartySiteId, Name, etc.).
            // Keep the same durable shape used by the synchronized catalog rows.
            insert.Parameters.AddWithValue("$sites", JsonSerializer.Serialize(local.Sites));
            insert.Parameters.AddWithValue("$session", workSessionId.ToString("D"));
            insert.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CustomerCreated);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(request, Json));
            insert.Parameters.AddWithValue("$now", now.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return local;
    }

    public async Task<(Guid CustomerId, string Payload, int Attempts)?> ClaimAsync(
        CancellationToken cancellationToken = default, Guid? customerId = null)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = $$"""
            SELECT DocumentId,Payload,AttemptCount FROM Outbox
            AS current
            WHERE current.Type=$type
              AND ($customerId IS NULL OR current.DocumentId=$customerId)
              AND ((current.Status IN ('Pending','RetryScheduled') AND
                    (current.NextAttemptAt IS NULL OR current.NextAttemptAt<=$now))
                   OR (current.Status='Uploading' AND current.LastAttemptAt<$stale))
              AND {{PosOutboxOrdering.NoBlockingPriorRowSql}}
            ORDER BY current.LocalSequence LIMIT 1;
            """;
        read.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CustomerCreated);
        read.Parameters.AddWithValue("$customerId", (object?)customerId?.ToString("D") ?? DBNull.Value);
        read.Parameters.AddWithValue("$now", now.ToString("O"));
        read.Parameters.AddWithValue("$stale", now.AddMinutes(-2).ToString("O"));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var item = (Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2) + 1);
        await reader.DisposeAsync();
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Outbox SET Status='Uploading',AttemptCount=AttemptCount+1,LastAttemptAt=$now
            WHERE DocumentId=$id AND Type=$type;
            """;
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        update.Parameters.AddWithValue("$id", item.Item1.ToString("D"));
        update.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CustomerCreated);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    public Task MarkUploadedAsync(Guid customerId, CancellationToken ct = default) =>
        UpdateAsync(customerId, PosOutboxStatus.Uploaded, null, null, removeLocal: false, ct);

    public Task ScheduleRetryAsync(
        Guid customerId, int attempts, string error, CancellationToken ct = default)
    {
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 6)));
        return UpdateAsync(customerId, PosOutboxStatus.RetryScheduled,
            timeProvider.GetUtcNow().AddSeconds(seconds), error, removeLocal: false, ct);
    }

    public Task MarkFailedAsync(Guid customerId, string error, CancellationToken ct = default) =>
        UpdateAsync(customerId, PosOutboxStatus.FailedPermanent, null, error, removeLocal: true, ct);

    public async Task<(string Status, string? Error)?> DeliveryStatusAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status,LastError FROM Outbox WHERE DocumentId=$id AND Type=$type;";
        command.Parameters.AddWithValue("$id", customerId.ToString("D"));
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CustomerCreated);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : null;
    }

    public async Task<PosCustomerOutboxStatus> ReadStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await PosUnifiedOutboxSchema.EnsureCreatedAsync(connectionString, cancellationToken);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CreatedAt,LastError FROM Outbox
            WHERE Type=$type AND Status<>'Uploaded' ORDER BY CreatedAt;
            """;
        command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CustomerCreated);
        var count = 0;
        DateTimeOffset? oldest = null;
        string? error = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
            oldest ??= DateTimeOffset.Parse(reader.GetString(0));
            if (!reader.IsDBNull(1)) error = reader.GetString(1);
        }
        return new(count, oldest, error);
    }

    private async Task UpdateAsync(
        Guid customerId,
        string status,
        DateTimeOffset? next,
        string? error,
        bool removeLocal,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Outbox SET Status=$status,NextAttemptAt=$next,LastError=$error,
                    UploadedAt=CASE WHEN $status='Uploaded' THEN $now ELSE UploadedAt END
                WHERE DocumentId=$id AND Type=$type;
                """;
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$next", (object?)next?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
            command.Parameters.AddWithValue("$id", customerId.ToString("D"));
            command.Parameters.AddWithValue("$type", PosOutboxMessageTypes.CustomerCreated);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (removeLocal)
        {
            await using var remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM PosPricingCustomers WHERE CustomerId=$id;";
            remove.Parameters.AddWithValue("$id", customerId.ToString("D"));
            await remove.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class PosCustomerOutboxUploader(
    PosCustomerOutboxStore store,
    HttpClient http,
    PosDeviceCredentials credentials,
    PosCatalogSynchronizer synchronization,
    PosSynchronizationEventLog events)
{
    private readonly SemaphoreSlim uploadGate = new(1, 1);

    public async Task EnsureUploadedForOrderAsync(Guid customerId, CancellationToken cancellationToken)
    {
        await uploadGate.WaitAsync(cancellationToken);
        try
        {
            var status = await store.DeliveryStatusAsync(customerId, cancellationToken);
            if (status is null || status.Value.Status == PosOutboxStatus.Uploaded) return;
            await UploadNextCoreAsync(cancellationToken, customerId);
            status = await store.DeliveryStatusAsync(customerId, cancellationToken);
            if (status?.Status == PosOutboxStatus.Uploaded) return;
            throw new PosOrdersServerException(503, "CustomerSynchronizationPending",
                "El cliente aún no se ha sincronizado con Auraly. Reintenta guardar el pedido cuando termine la sincronización.");
        }
        finally { uploadGate.Release(); }
    }

    public async Task<bool> UploadNextAsync(CancellationToken cancellationToken = default)
    {
        await uploadGate.WaitAsync(cancellationToken);
        try { return await UploadNextCoreAsync(cancellationToken); }
        finally { uploadGate.Release(); }
    }

    private async Task<bool> UploadNextCoreAsync(CancellationToken cancellationToken, Guid? customerId = null)
    {
        var item = await store.ClaimAsync(cancellationToken, customerId);
        if (item is null) return false;
        CreateCustomerRequest request;
        try
        {
            request = JsonSerializer.Deserialize<CreateCustomerRequest>(
                item.Value.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException("The customer payload is empty.");
        }
        catch (JsonException exception)
        {
            await store.MarkFailedAsync(item.Value.CustomerId, exception.Message, cancellationToken);
            events.Record("Error", "Cliente", "Cliente local rechazado", exception.Message);
            return true;
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/pos/v1/customers");
            message.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
            message.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
            message.Content = JsonContent.Create(request);
            using var response = await http.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken)
                    ?? throw new InvalidDataException("Auraly Server returned an empty customer.");
                if (created.CustomerId != item.Value.CustomerId)
                    throw new InvalidDataException("Auraly Server returned a different customer identifier.");
                await store.MarkUploadedAsync(item.Value.CustomerId, cancellationToken);
                try { await synchronization.SynchronizeCustomersAsync(cancellationToken); }
                catch (Exception exception) when (exception is HttpRequestException ||
                    exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    events.Record("Warning", "Cliente",
                        "Cliente subido; el catálogo de clientes quedó pendiente de actualizar",
                        exception.Message);
                }
                events.Record("Success", "Cliente", $"Cliente local subido: {created.DisplayName}",
                    created.Identification);
                return true;
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                await store.ScheduleRetryAsync(item.Value.CustomerId, item.Value.Attempts,
                    $"HTTP {(int)response.StatusCode}: {detail}", cancellationToken);
                return true;
            }
            await store.MarkFailedAsync(item.Value.CustomerId,
                $"HTTP {(int)response.StatusCode}: {detail}", cancellationToken);
            events.Record("Error", "Cliente", "Cliente local rechazado por el servidor", detail);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await store.ScheduleRetryAsync(
                item.Value.CustomerId, item.Value.Attempts, exception.Message, cancellationToken);
            events.Record("Warning", "Cliente", "Cliente local pendiente de sincronización",
                request.Party.Identification);
        }
        return true;
    }
}
