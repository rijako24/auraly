using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Parties;
using Auraly.Pos.Edge.Host;
using Auraly.Pos.Edge.Infrastructure;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosCustomerServerClientTests
{
    [Fact]
    public async Task Connected_creation_is_downloaded_to_sqlite_before_it_is_returned()
    {
        var database = Path.Combine(Path.GetTempPath(), $"auraly-pos-customer-{Guid.NewGuid():N}.db");
        try
        {
            var businessId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var deviceId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var countryId = Guid.NewGuid();
            var divisionId = Guid.NewGuid();
            var cityId = Guid.NewGuid();
            var store = new PosCatalogStore($"Data Source={database}");
            await store.InitializeAsync();
            var sessionId = Guid.NewGuid();
            var items = Array.Empty<PosCatalogItem>();
            var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items)))).ToLowerInvariant();
            await store.BeginBootstrapAsync(new CatalogSyncSessionResponse(
                sessionId, 0, 0, DateTimeOffset.UtcNow.AddHours(1)));
            await store.ApplyBootstrapPageAsync(new CatalogBootstrapPage(
                sessionId, 0, null, false, hash, items));
            await store.PromoteBootstrapAsync();

            var handler = new CustomerServerHandler(
                businessId, customerId, countryId, divisionId, cityId);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://auraly.test") };
            var credentials = new PosDeviceCredentials(deviceId, "device-secret");
            var scope = new PosOperationalScope(businessId, warehouseId);
            var warehousePolicy = new RecordingWarehousePolicySink();
            var events = new PosSynchronizationEventLog(TimeProvider.System);
            var synchronization = new PosCatalogSynchronizer(
                http, store, credentials, scope, events, warehousePolicy);
            var client = new PosCustomerServerClient(http, credentials, scope, synchronization, store);

            var countries = await client.CountriesAsync(default);
            Assert.Equal(countryId, Assert.Single(countries).CountryId);
            var divisions = await client.DivisionsAsync(countryId, default);
            Assert.Equal(divisionId, Assert.Single(divisions).AdministrativeDivisionId);
            var cities = await client.CitiesAsync(divisionId, default);
            Assert.Equal(cityId, Assert.Single(cities).CityId);
            var created = await client.CreateAsync(new PosCreateCustomerInput(
                PartyTypes.NaturalPerson, countryId, "CC", "1.234.567", null,
                "Cliente POS nuevo", null, "Cliente", "Nuevo",
                "cliente@auraly.test", "3001234567",
                new PartySiteInput(
                    "PRINCIPAL", "Principal", countryId, divisionId, cityId,
                    "Calle 1", "Barrio libre", null, null, "3001234567")), default);

            Assert.Equal(customerId, created.CustomerId);
            Assert.Equal("Cliente POS nuevo", created.Name);
            Assert.Equal(created, await store.GetCustomerAsync(customerId));
            Assert.True(handler.CustomerCreated);
            Assert.True(warehousePolicy.Applied);
            var receivedEvents = events.Read();
            Assert.Single(receivedEvents, item => item.Category == "Cliente");
            await synchronization.SynchronizeAsync();
            Assert.Single(events.Read(), item => item.Category == "Cliente");
            Assert.All(handler.DeviceRequests, request =>
            {
                Assert.Equal(deviceId.ToString("D"), request.DeviceId);
                Assert.Equal("device-secret", request.Secret);
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { database, database + "-wal", database + "-shm" })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class CustomerServerHandler(
        Guid businessId,
        Guid customerId,
        Guid countryId,
        Guid divisionId,
        Guid cityId) : HttpMessageHandler
    {
        public bool CustomerCreated { get; private set; }
        public List<(string? DeviceId, string? Secret)> DeviceRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            DeviceRequests.Add((
                request.Headers.TryGetValues("X-Auraly-Device-Id", out var ids) ? ids.Single() : null,
                request.Headers.TryGetValues("X-Auraly-Device-Secret", out var secrets) ? secrets.Single() : null));
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/pos/v1/customers/geography/countries?", StringComparison.Ordinal))
                return Ok<IReadOnlyCollection<CountryItem>>([
                    new CountryItem(countryId, "CO", "Colombia", true)
                ]);
            if (path.StartsWith($"/api/pos/v1/customers/geography/countries/{countryId:D}/divisions?", StringComparison.Ordinal))
                return Ok<IReadOnlyCollection<AdministrativeDivisionItem>>([
                    new AdministrativeDivisionItem(divisionId, countryId, "ANT", "Antioquia", "Department", true)
                ]);
            if (path.StartsWith($"/api/pos/v1/customers/geography/divisions/{divisionId:D}/cities?", StringComparison.Ordinal))
                return Ok<IReadOnlyCollection<CityItem>>([
                    new CityItem(cityId, divisionId, "MED", "Medell�n", true)
                ]);            if (path == "/api/pos/v1/customers" && request.Method == HttpMethod.Post)
            {
                var input = await request.Content!.ReadFromJsonAsync<CreateCustomerRequest>(cancellationToken);
                Assert.NotNull(input);
                Assert.Equal(businessId, input.BusinessId);
                CustomerCreated = true;
                return Ok(new CustomerDetail(
                    customerId, Guid.NewGuid(), businessId, PartyTypes.NaturalPerson,
                    "CC", "1.234.567", "1234567", null, "Cliente POS nuevo",
                    null, "Cliente", "Nuevo", "cliente@auraly.test", "3001234567",
                    null, true, []));
            }
            if (path.StartsWith("/api/pos/v1/pricing/snapshot?", StringComparison.Ordinal))
                return Ok(new PosPricingSnapshot([], [
                    new PosCustomerPricing(customerId, "1.234.567", "Cliente POS nuevo", null, true)
                ], WarehouseAllowsNegativeStock: true));
            if (path.StartsWith("/api/commerce/v1/reference-options/", StringComparison.Ordinal))
                return Ok<IReadOnlyList<ReferenceOption>>([]);
            if (path.StartsWith("/api/pos/v1/catalog/changes?", StringComparison.Ordinal))
                return Ok(new CatalogDeltaPage(0, 0, false, []));
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { path })
            };
        }

        private static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };
    }

    private sealed class RecordingWarehousePolicySink : IPosWarehousePolicySink
    {
        public bool Applied { get; private set; }

        public Task ApplyAsync(
            bool allowsNegativeStock,
            CancellationToken cancellationToken = default)
        {
            Applied = allowsNegativeStock;
            return Task.CompletedTask;
        }
    }
}


