using System.Net;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class DocumentProcessingOrderingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Accepted_sales_receive_one_durable_sequence_and_advance_in_business_order()
    {
        var first = fixture.CreateValidRequest(8_801);
        var second = fixture.CreateValidRequest(8_802);
        using var client = fixture.CreateClient();

        using (var upload = fixture.CreateUploadMessage(first))
        using (var response = await client.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var duplicate = fixture.CreateUploadMessage(first))
        using (var response = await client.SendAsync(duplicate))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var upload = fixture.CreateUploadMessage(second))
        using (var response = await client.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jobs = await ReadJobsAsync(first.DocumentId, second.DocumentId);
        Assert.Collection(
            jobs,
            firstJob =>
            {
                Assert.Equal(first.DocumentId, firstJob.DocumentId);
                Assert.Equal("Completed", firstJob.Status);
                Assert.Equal(1, firstJob.AttemptCount);
            },
            secondJob =>
            {
                Assert.Equal(second.DocumentId, secondJob.DocumentId);
                Assert.Equal("Completed", secondJob.Status);
                Assert.Equal(1, secondJob.AttemptCount);
            });
        Assert.Equal(jobs[0].ProcessingSequence + 1, jobs[1].ProcessingSequence);

        var cursor = await ReadCursorAsync();
        Assert.True(cursor.LastAssignedSequence >= jobs[1].ProcessingSequence);
        Assert.Equal(cursor.LastAssignedSequence, cursor.LastCompletedSequence);

        await RemoveFromPendingFiscalListingAsync(first.DocumentId, second.DocumentId);
    }

    [Fact]
    public async Task Unresolved_critical_job_blocks_the_next_document_without_losing_it()
    {
        var blocking = fixture.CreateValidRequest(8_803);
        var waiting = fixture.CreateValidRequest(8_804);
        using var client = fixture.CreateClient();

        using (var upload = fixture.CreateUploadMessage(blocking))
        using (var response = await client.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var blockingSequence = (await ReadJobsAsync(blocking.DocumentId, blocking.DocumentId))[0]
            .ProcessingSequence;
        await SimulateUnresolvedFailureAsync(blocking.DocumentId, blockingSequence);

        using (var upload = fixture.CreateUploadMessage(waiting))
        using (var response = await client.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", waiting.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("SalesDocumentLines", waiting.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("InventoryMovements", waiting.DocumentId));

        await ResolveBlockingFailureAsync(blocking.DocumentId, blockingSequence);
        using (var retry = fixture.CreateUploadMessage(waiting))
        using (var response = await client.SendAsync(retry))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, await fixture.CountAsync("SalesDocumentLines", waiting.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("InventoryMovements", waiting.DocumentId));

        await RemoveFromPendingFiscalListingAsync(blocking.DocumentId, waiting.DocumentId);
    }

    private async Task RemoveFromPendingFiscalListingAsync(Guid first, Guid second)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status=N'DianAccepted', UpdatedAt=SYSDATETIMEOFFSET()
            WHERE DocumentId IN (@First,@Second);
            """;
        command.Parameters.AddWithValue("@First", first);
        command.Parameters.AddWithValue("@Second", second);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SimulateUnresolvedFailureAsync(Guid documentId, long sequence)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.DocumentProcessingJobs
            SET Status=N'RetryScheduled', CompletedAt=NULL,
                AvailableAt=DATEADD(hour,1,SYSDATETIMEOFFSET()),
                LastError=N'Fallo crítico simulado'
            WHERE DocumentId=@DocumentId;

            UPDATE dbo.BusinessProcessingCursors
            SET LastCompletedSequence=@PreviousSequence,
                UpdatedAt=SYSDATETIMEOFFSET()
            WHERE BusinessId=@BusinessId;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@PreviousSequence", sequence - 1);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ResolveBlockingFailureAsync(Guid documentId, long sequence)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.DocumentProcessingJobs
            SET Status=N'Completed', CompletedAt=SYSDATETIMEOFFSET(), LastError=NULL
            WHERE DocumentId=@DocumentId;
            UPDATE dbo.BusinessProcessingCursors
            SET LastCompletedSequence=@Sequence, UpdatedAt=SYSDATETIMEOFFSET()
            WHERE BusinessId=@BusinessId AND LastCompletedSequence=@PreviousSequence;
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@PreviousSequence", sequence - 1);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<JobEvidence>> ReadJobsAsync(Guid first, Guid second)
    {
        var result = new List<JobEvidence>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId, ProcessingSequence, Status, AttemptCount
            FROM dbo.DocumentProcessingJobs
            WHERE DocumentId IN (@First, @Second)
            ORDER BY ProcessingSequence;
            """;
        command.Parameters.AddWithValue("@First", first);
        command.Parameters.AddWithValue("@Second", second);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new JobEvidence(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return result;
    }

    private async Task<CursorEvidence> ReadCursorAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT LastAssignedSequence, LastCompletedSequence
            FROM dbo.BusinessProcessingCursors
            WHERE BusinessId = @BusinessId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CursorEvidence(reader.GetInt64(0), reader.GetInt64(1));
    }

    private sealed record JobEvidence(
        Guid DocumentId,
        long ProcessingSequence,
        string Status,
        int AttemptCount);

    private sealed record CursorEvidence(
        long LastAssignedSequence,
        long LastCompletedSequence);
}
