using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class UserCapacityConcurrencyTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Concurrent_user_creation_never_exceeds_tenant_capacity()
    {
        var original = await ReadCapacityAsync();
        await SetCapacityAsync(original.ActiveUsers + 1);

        try
        {
            using var firstClient = fixture.CreateAdminClient("users.create");
            using var secondClient = fixture.CreateAdminClient("users.create");
            var suffix = Guid.NewGuid().ToString("N");

            var first = firstClient.PostAsJsonAsync("/api/v1/users", new
            {
                username = $"capacity-a-{suffix}",
                email = $"capacity-a-{suffix}@auraly.test",
                password = "Auraly-Capacity-2026!",
                firstName = "Capacidad",
                lastName = "A",
                phoneNumber = (string?)null
            });
            var second = secondClient.PostAsJsonAsync("/api/v1/users", new
            {
                username = $"capacity-b-{suffix}",
                email = $"capacity-b-{suffix}@auraly.test",
                password = "Auraly-Capacity-2026!",
                firstName = "Capacidad",
                lastName = "B",
                phoneNumber = (string?)null
            });

            using var firstResponse = await first;
            using var secondResponse = await second;
            var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };

            Assert.Single(statuses, status => status == HttpStatusCode.Created);
            Assert.Single(statuses, status => status == HttpStatusCode.Conflict);
            Assert.Equal(original.ActiveUsers + 1, (await ReadCapacityAsync()).ActiveUsers);
        }
        finally
        {
            await SetCapacityAsync(original.MaximumUsers);
        }
    }

    private async Task<CapacityState> ReadCapacityAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT t.MaximumUsers,
                   (SELECT COUNT(*) FROM dbo.AppUsers u
                    WHERE u.TenantId=t.TenantId AND u.IsActive=1)
            FROM dbo.Tenants t
            WHERE t.TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CapacityState(reader.GetInt32(0), reader.GetInt32(1));
    }

    private async Task SetCapacityAsync(int maximumUsers)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE dbo.Tenants SET MaximumUsers=@MaximumUsers WHERE TenantId=@TenantId;",
            connection);
        command.Parameters.AddWithValue("@MaximumUsers", maximumUsers);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed record CapacityState(int MaximumUsers, int ActiveUsers);
}