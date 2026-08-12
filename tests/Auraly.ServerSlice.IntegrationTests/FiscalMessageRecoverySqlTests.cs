using System.Net;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class FiscalMessageRecoverySqlTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Active_generation_lease_is_rescheduled_instead_of_losing_the_message()
    {
        var request = fixture.CreateValidRequest(911);
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await SetIssuerConfigurationAsync(request.DocumentId);

        var now = new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero);
        var lease = TimeSpan.FromMinutes(2);
        var store = new SqlFiscalGenerationWorkStore(
            new SqlServerConnectionFactory(fixture.ConnectionString),
            new TestIds());

        var acquired = await store.AcquireAsync(
            fixture.BusinessId,
            request.DocumentId,
            "recovery-test",
            now,
            lease,
            CancellationToken.None);
        Assert.NotNull(acquired);

        var resumeAt = await store.GetResumeAtAsync(
            fixture.BusinessId,
            request.DocumentId,
            now.AddSeconds(10),
            lease,
            CancellationToken.None);

        Assert.Equal(now.Add(lease).AddSeconds(1), resumeAt);
    }

    [Fact]
    public async Task Submission_resume_uses_the_durable_next_attempt_and_ignores_terminal_statuses()
    {
        var request = fixture.CreateValidRequest(912);
        using var client = fixture.CreateClient();
        using var response = await client.SendAsync(fixture.CreateUploadMessage(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var now = new DateTimeOffset(2026, 8, 1, 14, 10, 0, TimeSpan.Zero);
        var nextAttemptAt = now.AddMinutes(3);
        await SetProcessAsync(
            request.DocumentId,
            FiscalDocumentStatusCodes.RetryScheduled,
            nextAttemptAt);
        var store = new SqlFiscalSubmissionWorkStore(
            new SqlServerConnectionFactory(fixture.ConnectionString),
            new TestIds());

        var resumeAt = await store.GetResumeAtAsync(
            fixture.BusinessId,
            request.DocumentId,
            now,
            TimeSpan.FromMinutes(2),
            CancellationToken.None);
        Assert.Equal(nextAttemptAt, resumeAt);

        await SetProcessAsync(
            request.DocumentId,
            FiscalDocumentStatusCodes.DianAccepted,
            null);
        Assert.Null(await store.GetResumeAtAsync(
            fixture.BusinessId,
            request.DocumentId,
            now,
            TimeSpan.FromMinutes(2),
            CancellationToken.None));
    }

    private async Task SetIssuerConfigurationAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.FiscalDocumentProcesses
            SET FiscalIssuerConfigurationId=@ConfigurationId
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            """;
        command.Parameters.AddWithValue(
            "@ConfigurationId",
            fixture.FiscalIssuerConfigurationId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task SetProcessAsync(
        Guid documentId,
        string status,
        DateTimeOffset? nextAttemptAt)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status=@Status,NextAttemptAt=@NextAttemptAt,LockedAt=NULL,LockedBy=NULL
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            """;
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue(
            "@NextAttemptAt",
            (object?)nextAttemptAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private sealed class TestIds : IAuralyIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
