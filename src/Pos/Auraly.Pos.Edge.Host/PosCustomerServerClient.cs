using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Parties;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PosCreateCustomerInput(
    string PartyType,
    Guid IdentificationCountryId,
    string IdentificationTypeCode,
    string Identification,
    string? VerificationDigit,
    string DisplayName,
    string? LegalName,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    PartySiteInput PrimarySite);

public sealed class PosCustomerServerClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosOperationalScope scope,
    PosCatalogSynchronizer synchronization,
    PosCatalogStore catalog)
{
    public Task<IReadOnlyCollection<CountryItem>> CountriesAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyCollection<CountryItem>>(
            $"/api/pos/v1/customers/geography/countries?businessId={scope.BusinessId:D}", ct);

    public Task<IReadOnlyCollection<AdministrativeDivisionItem>> DivisionsAsync(Guid countryId, CancellationToken ct) =>
        GetAsync<IReadOnlyCollection<AdministrativeDivisionItem>>(
            $"/api/pos/v1/customers/geography/countries/{countryId:D}/divisions?businessId={scope.BusinessId:D}", ct);

    public Task<IReadOnlyCollection<CityItem>> CitiesAsync(Guid divisionId, CancellationToken ct) =>
        GetAsync<IReadOnlyCollection<CityItem>>(
            $"/api/pos/v1/customers/geography/divisions/{divisionId:D}/cities?businessId={scope.BusinessId:D}", ct);

    public async Task<PosCustomerPricing> CreateAsync(PosCreateCustomerInput input, CancellationToken ct)
    {
        var request = new CreateCustomerRequest(
            Guid.NewGuid(),
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
            null);
        using var message = DeviceRequest(HttpMethod.Post, "/api/pos/v1/customers");
        message.Content = JsonContent.Create(request);
        using var response = await http.SendAsync(message, ct);
        await EnsureSuccessAsync(response, ct);
        var created = await response.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken: ct)
            ?? throw new InvalidDataException("Auraly Server returned an empty customer.");
        await synchronization.SynchronizeAsync(ct);
        return await catalog.GetCustomerAsync(created.CustomerId, ct)
            ?? throw new InvalidDataException("The created customer was not synchronized to POS Edge.");
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken ct)
    {
        using var request = DeviceRequest(HttpMethod.Get, uri);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new InvalidDataException("Auraly Server returned an empty response.");
    }

    private HttpRequestMessage DeviceRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail) ? $"Auraly Server returned {(int)response.StatusCode}." : detail,
            null,
            response.StatusCode);
    }
}


