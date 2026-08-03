using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PartyCustomerVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Geography_customer_identity_sites_and_pricing_are_connected_end_to_end()
    {
        using var denied = fixture.CreateAdminClient(PartyPermissionCodes.GeographyRead);
        using var deniedMaster = await denied.PostAsJsonAsync(
            "/api/commerce/v1/masters/geography/countries",
            new SaveCountryRequest("CX", "Customer test country"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedMaster.StatusCode);

        using var admin = fixture.CreateAdminClient(
            PartyPermissionCodes.GeographyRead,
            PartyPermissionCodes.GeographyManage,
            PartyPermissionCodes.CustomerRead,
            PartyPermissionCodes.CustomerCreate,
            PartyPermissionCodes.ManageSites,
            PartyPermissionCodes.ManagePricing);

        var country = await PostAndReadAsync<SaveCountryRequest, CountryItem>(
            admin,
            "/api/commerce/v1/masters/geography/countries",
            new SaveCountryRequest("CX", "Customer test country"));
        var division = await PostAndReadAsync<SaveAdministrativeDivisionRequest, AdministrativeDivisionItem>(
            admin,
            "/api/commerce/v1/masters/geography/divisions",
            new SaveAdministrativeDivisionRequest(
                country.CountryId,
                "D01",
                "Customer test department"));
        var city = await PostAndReadAsync<SaveCityRequest, CityItem>(
            admin,
            "/api/commerce/v1/masters/geography/cities",
            new SaveCityRequest(
                division.AdministrativeDivisionId,
                "C01",
                "Customer test city"));

        var priceListId = Guid.NewGuid();
        var priceChannelId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.PriceLists(PriceListId,BusinessId,Code,Name,IsActive,CreatedAt)
            VALUES(@PriceListId,@BusinessId,N'CLI-VIP',N'Lista VIP clientes',1,SYSDATETIMEOFFSET());
            INSERT dbo.PriceChannels(PriceChannelId,BusinessId,Code,Name,IsActive,CreatedAt)
            VALUES(@PriceChannelId,@BusinessId,N'CLI-MAY',N'Mayorista clientes',1,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@PriceListId", priceListId),
            new SqlParameter("@PriceChannelId", priceChannelId),
            new SqlParameter("@BusinessId", fixture.BusinessId));

        var operationId = Guid.NewGuid();
        var request = CustomerRequest(
            operationId,
            fixture.BusinessId,
            country.CountryId,
            division.AdministrativeDivisionId,
            city.CityId,
            "1.234.567-8",
            "Ada Cliente",
            "Principal",
            new CustomerPricingInput(priceListId, null));

        using var createdResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/customers",
            request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CustomerDetail>();
        Assert.NotNull(created);
        Assert.Equal("12345678", created.NormalizedIdentification);
        Assert.Equal(priceListId, created.PriceListId);
        Assert.Null(created.PriceChannelId);
        Assert.Equal("Barrio escrito por el usuario", Assert.Single(created.Sites).Neighborhood);

        using var repeatedResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/customers",
            request);
        Assert.Equal(HttpStatusCode.Created, repeatedResponse.StatusCode);
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<CustomerDetail>();
        Assert.NotNull(repeated);
        Assert.Equal(created.CustomerId, repeated.CustomerId);
        Assert.Equal(created.PartyId, repeated.PartyId);

        var identification = Uri.EscapeDataString("1 234 567 8");
        var found = await admin.GetFromJsonAsync<CustomerDetail>(
            $"/api/commerce/v1/customers/by-identification?countryId={country.CountryId:D}" +
            $"&identificationType=cc&identification={identification}");
        Assert.NotNull(found);
        Assert.Equal(created.CustomerId, found.CustomerId);

        var siteOperationId = Guid.NewGuid();
        var secondSiteRequest = new AddPartySiteRequest(
            siteOperationId,
            new PartySiteInput(
                "NORTE",
                "Sede norte",
                country.CountryId,
                division.AdministrativeDivisionId,
                city.CityId,
                "Carrera 2 # 3-4",
                "Barrio Norte",
                null,
                "norte@auraly.test",
                "3001112233",
                false));
        using var secondSiteResponse = await admin.PostAsJsonAsync(
            $"/api/commerce/v1/customers/{created.CustomerId:D}/sites",
            secondSiteRequest);
        Assert.Equal(HttpStatusCode.Created, secondSiteResponse.StatusCode);
        var secondSite = await secondSiteResponse.Content.ReadFromJsonAsync<PartySiteDetail>();
        Assert.NotNull(secondSite);

        using var repeatedSiteResponse = await admin.PostAsJsonAsync(
            $"/api/commerce/v1/customers/{created.CustomerId:D}/sites",
            secondSiteRequest);
        Assert.Equal(HttpStatusCode.Created, repeatedSiteResponse.StatusCode);
        var repeatedSite = await repeatedSiteResponse.Content.ReadFromJsonAsync<PartySiteDetail>();
        Assert.NotNull(repeatedSite);
        Assert.Equal(secondSite.PartySiteId, repeatedSite.PartySiteId);

        var detailed = await admin.GetFromJsonAsync<CustomerDetail>(
            $"/api/commerce/v1/customers/{created.CustomerId:D}");
        Assert.NotNull(detailed);
        Assert.Equal(2, detailed.Sites.Count);

        var page = await admin.GetFromJsonAsync<CustomerPage>(
            "/api/commerce/v1/customers?page=1&pageSize=10&search=123456");
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.CustomerId == created.CustomerId);

        var invalidPricing = request with
        {
            OperationId = Guid.NewGuid(),
            Party = request.Party with { Identification = "900111222" },
            Pricing = new CustomerPricingInput(priceListId, priceChannelId)
        };
        using var invalidPricingResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/customers",
            invalidPricing);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPricingResponse.StatusCode);

        using var noPricingPermission = fixture.CreateAdminClient(
            PartyPermissionCodes.CustomerCreate);
        using var pricingDeniedResponse = await noPricingPermission.PostAsJsonAsync(
            "/api/commerce/v1/customers",
            request with
            {
                OperationId = Guid.NewGuid(),
                Party = request.Party with { Identification = "900333444" }
            });
        Assert.Equal(HttpStatusCode.Forbidden, pricingDeniedResponse.StatusCode);

        Assert.Equal(
            1,
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Parties WHERE PartyId=@PartyId;",
                new SqlParameter("@PartyId", created.PartyId)));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Customers WHERE CustomerId=@CustomerId;",
                new SqlParameter("@CustomerId", created.CustomerId)));
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PartySites WHERE PartyId=@PartyId;",
                new SqlParameter("@PartyId", created.PartyId)));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.PartySiteCreationReceipts WHERE BusinessId=@BusinessId AND OperationId=@OperationId;",
                new SqlParameter("@BusinessId", fixture.BusinessId),
                new SqlParameter("@OperationId", siteOperationId)));
    }

    [Fact]
    public async Task Pos_quick_creation_uses_device_scope_and_cannot_assign_pricing()
    {
        var countryId = Guid.NewGuid();
        var divisionId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Countries(CountryId,Code,Name,IsActive,CreatedAt)
            VALUES(@CountryId,N'PX',N'POS country',1,SYSDATETIMEOFFSET());
            INSERT dbo.AdministrativeDivisions
              (AdministrativeDivisionId,CountryId,Code,Name,DivisionType,IsActive,CreatedAt)
            VALUES(@DivisionId,@CountryId,N'PD',N'POS department',N'Department',1,SYSDATETIMEOFFSET());
            INSERT dbo.Cities(CityId,AdministrativeDivisionId,Code,Name,IsActive,CreatedAt)
            VALUES(@CityId,@DivisionId,N'PC',N'POS city',1,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@CountryId", countryId),
            new SqlParameter("@DivisionId", divisionId),
            new SqlParameter("@CityId", cityId));

        var request = CustomerRequest(
            Guid.NewGuid(),
            fixture.BusinessId,
            countryId,
            divisionId,
            cityId,
            "55.667.788",
            "Cliente creado en facturaciÃ³n",
            "Principal",
            null);

        using var denied = fixture.CreateClient();
        denied.DefaultRequestHeaders.Add("X-Auraly-Device-Id", fixture.DeniedDeviceId.ToString("D"));
        denied.DefaultRequestHeaders.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeniedDeviceSecret);
        using var deniedResponse = await denied.PostAsJsonAsync(
            "/api/pos/v1/customers",
            request);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var pos = fixture.CreateClient();
        pos.DefaultRequestHeaders.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        pos.DefaultRequestHeaders.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        var countries = await pos.GetFromJsonAsync<IReadOnlyCollection<CountryItem>>(
            $"/api/pos/v1/customers/geography/countries?businessId={fixture.BusinessId:D}");
        Assert.Contains(countries!, item => item.CountryId == countryId);
        using var createdResponse = await pos.PostAsJsonAsync(
            "/api/pos/v1/customers",
            request);
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CustomerDetail>();
        Assert.NotNull(created);
        Assert.Equal(fixture.BusinessId, created.BusinessId);
        Assert.Null(created.PriceListId);
        Assert.Null(created.PriceChannelId);

        var found = await pos.GetFromJsonAsync<CustomerDetail>(
            $"/api/pos/v1/customers/by-identification?businessId={fixture.BusinessId:D}&countryId={countryId:D}" +
            "&identificationType=CC&identification=55667788");
        Assert.NotNull(found);
        Assert.Equal(created.CustomerId, found.CustomerId);

        var priceListId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.PriceLists(PriceListId,BusinessId,Code,Name,IsActive,CreatedAt)
            VALUES(@PriceListId,@BusinessId,N'POS-NO',N'POS cannot assign',1,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@PriceListId", priceListId),
            new SqlParameter("@BusinessId", fixture.BusinessId));
        using var pricingResponse = await pos.PostAsJsonAsync(
            "/api/pos/v1/customers",
            request with
            {
                OperationId = Guid.NewGuid(),
                Party = request.Party with { Identification = "55667799" },
                Pricing = new CustomerPricingInput(priceListId, null)
            });
        Assert.Equal(HttpStatusCode.Forbidden, pricingResponse.StatusCode);

        using var wrongBusinessResponse = await pos.PostAsJsonAsync(
            "/api/pos/v1/customers",
            request with { OperationId = Guid.NewGuid(), BusinessId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, wrongBusinessResponse.StatusCode);
    }

    [Fact]
    public async Task Customer_and_supplier_roles_share_one_party_and_workspace_is_concurrent_safe()
    {
        var synchronizationMessagesBefore = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Customers';",
            new SqlParameter("@BusinessId", fixture.BusinessId));
        using var admin = fixture.CreateAdminClient(
            PartyPermissionCodes.GeographyRead,
            PartyPermissionCodes.GeographyManage,
            PartyPermissionCodes.CustomerRead,
            PartyPermissionCodes.CustomerCreate,
            PartyWorkspacePermissionCodes.Read,
            PartyWorkspacePermissionCodes.Update,
            PartyWorkspacePermissionCodes.Deactivate,
            PartyWorkspacePermissionCodes.SupplierRead,
            PartyWorkspacePermissionCodes.SupplierCreate);

        var country = await PostAndReadAsync<SaveCountryRequest, CountryItem>(
            admin, "/api/commerce/v1/masters/geography/countries",
            new SaveCountryRequest("PW", "Party workspace country"));
        var division = await PostAndReadAsync<SaveAdministrativeDivisionRequest, AdministrativeDivisionItem>(
            admin, "/api/commerce/v1/masters/geography/divisions",
            new SaveAdministrativeDivisionRequest(country.CountryId, "PWD", "Party workspace division"));
        var city = await PostAndReadAsync<SaveCityRequest, CityItem>(
            admin, "/api/commerce/v1/masters/geography/cities",
            new SaveCityRequest(division.AdministrativeDivisionId, "PWC", "Party workspace city"));

        var customerRequest = CustomerRequest(
            Guid.NewGuid(), fixture.BusinessId, country.CountryId,
            division.AdministrativeDivisionId, city.CityId,
            "901.777.333-1", "Comercial unificada", "Principal", null) with
        {
            Party = new PartyInput(
                PartyTypes.Organization, country.CountryId, "NIT", "901.777.333-1", "4",
                "Comercial unificada", "Comercial unificada S.A.S.", null, null,
                "compras@unificada.test", "3007773311")
        };
        using var customerResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/customers", customerRequest);
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDetail>();
        Assert.NotNull(customer);

        var supplierRequest = new CreateSupplierRequest(
            Guid.NewGuid(), fixture.BusinessId, customerRequest.Party, customerRequest.PrimarySite);
        using var supplierResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/suppliers", supplierRequest);
        Assert.Equal(HttpStatusCode.Created, supplierResponse.StatusCode);
        var supplier = await supplierResponse.Content.ReadFromJsonAsync<SupplierAcceptance>();
        Assert.NotNull(supplier);
        Assert.Equal(customer.PartyId, supplier.PartyId);
        Assert.False(supplier.IdempotentReplay);

        using var replayResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/suppliers", supplierRequest);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replay = await replayResponse.Content.ReadFromJsonAsync<SupplierAcceptance>();
        Assert.NotNull(replay);
        Assert.Equal(supplier.SupplierId, replay.SupplierId);
        Assert.True(replay.IdempotentReplay);

        var page = await admin.GetFromJsonAsync<PartyWorkspacePage>(
            "/api/commerce/v1/parties?page=1&pageSize=10&search=9017773331");
        Assert.NotNull(page);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        var item = Assert.Single(page.Items.Where(value => value.PartyId == customer.PartyId));
        Assert.Equal(new[] { "Customer", "Supplier" }, item.Roles.OrderBy(value => value).ToArray());
        Assert.Equal("4", item.VerificationDigit);

        var update = new UpdatePartyRequest(
            PartyTypes.Organization, "Comercial unificada renovada",
            "Comercial unificada S.A.S.", null, null, "4",
            "compras@unificada.test", "3007773311", item.RowVersion);
        using var updateResponse = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{item.PartyId:D}", update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PartyWorkspaceItem>();
        Assert.NotNull(updated);
        Assert.Equal("Comercial unificada renovada", updated.DisplayName);
        Assert.Equal("4", updated.VerificationDigit);

        using var staleResponse = await admin.PutAsJsonAsync(
            $"/api/commerce/v1/parties/{item.PartyId:D}", update);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using var inactiveResponse = await admin.PostAsJsonAsync(
            $"/api/commerce/v1/parties/{item.PartyId:D}/status",
            new SetPartyBusinessStatusRequest(false, updated.RowVersion));
        Assert.Equal(HttpStatusCode.OK, inactiveResponse.StatusCode);
        var inactive = await inactiveResponse.Content.ReadFromJsonAsync<PartyWorkspaceItem>();
        Assert.NotNull(inactive);
        Assert.False(inactive.IsActive);

        using var denied = fixture.CreateAdminClient(PartyWorkspacePermissionCodes.Read);
        using var deniedResponse = await denied.PostAsJsonAsync(
            "/api/commerce/v1/suppliers", supplierRequest with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var wrongBusiness = await admin.PostAsJsonAsync(
            "/api/commerce/v1/suppliers",
            supplierRequest with { OperationId = Guid.NewGuid(), BusinessId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, wrongBusiness.StatusCode);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Parties WHERE PartyId=@PartyId;",
            new SqlParameter("@PartyId", customer.PartyId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Customers WHERE PartyId=@PartyId AND BusinessId=@BusinessId;",
            new SqlParameter("@PartyId", customer.PartyId), new SqlParameter("@BusinessId", fixture.BusinessId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Suppliers WHERE PartyId=@PartyId AND BusinessId=@BusinessId;",
            new SqlParameter("@PartyId", customer.PartyId), new SqlParameter("@BusinessId", fixture.BusinessId)));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PartySites WHERE PartyId=@PartyId AND Code=N'PRINCIPAL';",
            new SqlParameter("@PartyId", customer.PartyId)));
        Assert.True(await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages WHERE BusinessId=@BusinessId AND Stream=N'Customers';",
            new SqlParameter("@BusinessId", fixture.BusinessId)) >= synchronizationMessagesBefore + 3);
    }
    private static CreateCustomerRequest CustomerRequest(
        Guid operationId,
        Guid businessId,
        Guid countryId,
        Guid divisionId,
        Guid cityId,
        string identification,
        string displayName,
        string siteName,
        CustomerPricingInput? pricing) =>
        new(
            operationId,
            businessId,
            new PartyInput(
                PartyTypes.NaturalPerson,
                countryId,
                "CC",
                identification,
                null,
                displayName,
                null,
                "Ada",
                "Cliente",
                "cliente@auraly.test",
                "3001234567"),
            new PartySiteInput(
                "PRINCIPAL",
                siteName,
                countryId,
                divisionId,
                cityId,
                "Calle 1 # 2-3",
                "Barrio escrito por el usuario",
                null,
                "sede@auraly.test",
                "3001234567"),
            pricing);

    private static async Task<TResponse> PostAndReadAsync<TRequest, TResponse>(
        HttpClient client,
        string uri,
        TRequest request)
    {
        using var response = await client.PostAsJsonAsync(uri, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<TResponse>()
            ?? throw new InvalidOperationException($"Endpoint '{uri}' returned an empty body.");
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : (T)Convert.ChangeType(value!, typeof(T));
    }
}
