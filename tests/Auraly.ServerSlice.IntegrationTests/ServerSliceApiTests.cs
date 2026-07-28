using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ServerSliceApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Authenticated_concurrent_duplicate_is_processed_exactly_once()
    {
        var request = fixture.CreateValidRequest(101);
        using var client = fixture.CreateClient();
        using var firstMessage = fixture.CreateUploadMessage(request);
        using var secondMessage = fixture.CreateUploadMessage(request);

        var responses = await Task.WhenAll(
            client.SendAsync(firstMessage),
            client.SendAsync(secondMessage));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var receipts = await Task.WhenAll(
            responses.Select(response =>
                response.Content.ReadFromJsonAsync<PosSaleUploadResponse>()));
        Assert.All(receipts, receipt => Assert.NotNull(receipt));
        Assert.Contains(receipts, receipt => receipt!.Status == PosSaleRemoteStatuses.FiscalVerified);
        Assert.Contains(receipts, receipt => receipt!.Status == PosSaleRemoteStatuses.AlreadyProcessed);
        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("SalesDocumentLines", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("SalesPayments", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("ServerOutboxMessages", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingReceipts", request.DocumentId));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Authentication_permission_and_authenticated_context_are_enforced()
    {
        using var client = fixture.CreateClient();
        var unauthenticated = fixture.CreateValidRequest(102);
        using var noCredentials = fixture.CreateUploadMessage(unauthenticated, secret: null);
        using var noCredentialsResponse = await client.SendAsync(noCredentials);
        Assert.Equal(HttpStatusCode.Unauthorized, noCredentialsResponse.StatusCode);

        var denied = fixture.CreateValidRequest(103) with { DeviceId = fixture.DeniedDeviceId };
        using var deniedMessage = fixture.CreateUploadMessage(
            denied,
            ServerSliceFixture.DeniedDeviceSecret,
            fixture.DeniedDeviceId);
        using var deniedResponse = await client.SendAsync(deniedMessage);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var wrongTenant = fixture.CreateValidRequest(104) with { TenantId = Guid.NewGuid() };
        using var wrongTenantMessage = fixture.CreateUploadMessage(wrongTenant);
        using var wrongTenantResponse = await client.SendAsync(wrongTenantMessage);
        Assert.Equal(HttpStatusCode.Forbidden, wrongTenantResponse.StatusCode);
    }

    [Fact]
    public async Task Altered_Auraly_document_number_is_rejected_before_persistence()
    {
        var original = fixture.CreateValidRequest(105);
        var changed = original with
        {
            DocumentNumber = original.DocumentNumber with
            {
                FullNumber = $"{original.DocumentNumber.FullNumber}X"
            }
        };
        using var client = fixture.CreateClient();
        using var message = fixture.CreateUploadMessage(changed);
        using var response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await fixture.CountAsync("SalesDocuments", original.DocumentId));
    }

    public static TheoryData<string> FiscalMutations => new()
    {
        "number",
        "date",
        "customer",
        "quantity",
        "price",
        "discount",
        "tax",
        "total",
        "prefix",
        "authorization"
    };

    [Theory]
    [MemberData(nameof(FiscalMutations))]
    public async Task Every_fiscal_mutation_creates_conflict_without_effects(string mutation)
    {
        var consecutive = 200 + mutation switch
        {
            "number" => 1, "date" => 2, "customer" => 3, "quantity" => 4,
            "price" => 5, "discount" => 6, "tax" => 7, "total" => 8,
            "prefix" => 9, "authorization" => 10,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var original = fixture.CreateValidRequest(consecutive);
        var changed = Mutate(original, mutation);
        using var client = fixture.CreateClient();
        using var message = fixture.CreateUploadMessage(changed);
        using var response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
        Assert.NotNull(receipt);
        Assert.Equal(PosSaleRemoteStatuses.FiscalIntegrityConflict, receipt.Status);
        Assert.Equal(original.FiscalSnapshot.Cufe, receipt.CufeReceived);
        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", original.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("FiscalSnapshots", original.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("SalesPayments", original.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("InventoryMovements", original.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("ServerOutboxMessages", original.DocumentId));
    }

    private static PosSaleUploadRequest Mutate(PosSaleUploadRequest request, string mutation)
    {
        var snapshot = request.FiscalSnapshot;
        var line = request.Lines.Single();
        return mutation switch
        {
            "number" => request with
            {
                FiscalSnapshot = snapshot with { FiscalNumber = $"{snapshot.FiscalNumber}X" }
            },
            "date" => request with
            {
                FiscalSnapshot = snapshot with { IssuedAt = snapshot.IssuedAt.AddMinutes(1) }
            },
            "customer" => request with
            {
                FiscalSnapshot = snapshot with { CustomerIdentification = "999999999" }
            },
            "quantity" => request with
            {
                Lines = [line with { Quantity = line.Quantity + 1 }]
            },
            "price" => request with
            {
                Lines = [line with { UnitPrice = line.UnitPrice + 1 }]
            },
            "discount" => request with
            {
                Lines = [line with { DiscountAmount = 1 }]
            },
            "tax" => request with
            {
                Lines = [line with { TaxAmount = line.TaxAmount + 1 }]
            },
            "total" => request with
            {
                FiscalSnapshot = snapshot with { PayableAmount = snapshot.PayableAmount + 1 }
            },
            "prefix" => request with
            {
                FiscalSnapshot = snapshot with { Prefix = "ZZ" }
            },
            "authorization" => request with
            {
                FiscalSnapshot = snapshot with { AuthorizationNumber = "18760000999" }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }
}

