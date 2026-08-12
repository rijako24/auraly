using System.Net;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class SaleCustomerSnapshotIntegrationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Sale_assigns_a_visiting_party_to_the_business_without_changing_the_snapshot()
    {
        var countryId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT dbo.Countries(CountryId,Code,Name,IsActive,CreatedAt)
            VALUES(@CountryId,N'SX',N'Snapshot country',1,SYSDATETIMEOFFSET());
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
               Identification,NormalizedIdentification,DisplayName,CompletionStatus,
               IsActive,CreatedBy,CreatedAt)
            VALUES
              (@PartyId,@TenantId,N'NaturalPerson',@CountryId,N'CC',
               N'222222222',N'222222222',N'Nombre al facturar',N'Complete',
               1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Businesses
              (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES(@OtherBusinessId,@TenantId,N'Otro negocio',N'Prueba de aislamiento',
              N'Calle 1',N'3000000000',@OtherBusinessEmail,N'https://other.auraly.test',1,SYSUTCDATETIME());
            INSERT dbo.Customers(CustomerId,PartyId,BusinessId,IsActive,CreatedBy,CreatedAt)
            VALUES(@OtherCustomerId,@PartyId,@OtherBusinessId,1,@UserId,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@CountryId", countryId),
            new SqlParameter("@PartyId", partyId),
            new SqlParameter("@OtherCustomerId", otherCustomerId),
            new SqlParameter("@TenantId", fixture.TenantId),
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@OtherBusinessId", otherBusinessId),
            new SqlParameter("@OtherBusinessEmail", $"other-{otherBusinessId:N}@auraly.test"),
            new SqlParameter("@UserId", fixture.UserId));

        var request = fixture.CreateValidRequest(8701) with { CustomerId = otherCustomerId };
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var storedCustomerId = await ScalarAsync<Guid?>(
            "SELECT CustomerId FROM dbo.SalesDocuments WHERE DocumentId=@DocumentId;",
            new SqlParameter("@DocumentId", request.DocumentId));
        Assert.NotNull(storedCustomerId);
        Assert.NotEqual(otherCustomerId, storedCustomerId.Value);
        Assert.Equal(1, await ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM dbo.Customers
            WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
            """,
            new SqlParameter("@PartyId", partyId), new SqlParameter("@BusinessId", fixture.BusinessId)));
        var snapshotJson = await ScalarAsync<string>(
            "SELECT SnapshotJson FROM dbo.FiscalSnapshots WHERE DocumentId=@DocumentId;",
            new SqlParameter("@DocumentId", request.DocumentId));
        var snapshot = PosSaleContractSerializer.Deserialize(snapshotJson!);
        Assert.Equal(storedCustomerId, snapshot.CustomerId);
        Assert.Equal("222222222", snapshot.FiscalSnapshot!.CustomerIdentification);

        await ExecuteAsync(
            """
            UPDATE dbo.Parties
            SET DisplayName=N'Nombre cambiado después', Identification=N'333333333',
                NormalizedIdentification=N'333333333', UpdatedAt=SYSDATETIMEOFFSET()
            WHERE PartyId=@PartyId;
            """,
            new SqlParameter("@PartyId", partyId));
        var unchangedJson = await ScalarAsync<string>(
            "SELECT SnapshotJson FROM dbo.FiscalSnapshots WHERE DocumentId=@DocumentId;",
            new SqlParameter("@DocumentId", request.DocumentId));
        var unchanged = PosSaleContractSerializer.Deserialize(unchangedJson!);
        Assert.Equal("222222222", unchanged.FiscalSnapshot!.CustomerIdentification);

        var visitingAgain = fixture.CreateValidRequest(8702) with { CustomerId = otherCustomerId };
        using var visitingAgainResponse = await client.SendAsync(fixture.CreateUploadMessage(visitingAgain));
        Assert.Equal(HttpStatusCode.OK, visitingAgainResponse.StatusCode);
        Assert.Equal(storedCustomerId, await ScalarAsync<Guid?>(
            """
            SELECT CustomerId FROM dbo.SalesDocuments
            WHERE DocumentId=@DocumentId;
            """,
            new SqlParameter("@DocumentId", visitingAgain.DocumentId)));
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T?> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default;
        return value is T typed ? typed : (T)Convert.ChangeType(value, typeof(T));
    }
}
