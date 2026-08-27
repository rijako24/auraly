using System.IO.Compression;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed class FiscalSubmissionWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Submission_package_is_deterministic_and_contains_the_signed_xml()
    {
        var builder = new FiscalSubmissionPackageBuilder();
        var xml = Encoding.UTF8.GetBytes("<Invoice>signed</Invoice>");

        var first = builder.Build("SETP42", xml);
        var second = builder.Build("SETP42", xml);

        Assert.Equal(first, second);
        using var archive = new ZipArchive(new MemoryStream(first), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("SETP42.xml", entry.FullName);
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("<Invoice>signed</Invoice>", reader.ReadToEnd());
    }

    [Fact]
    public async Task Send_attempt_is_durable_before_transport_and_track_id_schedules_query()
    {
        var store = new TestStore(Work());
        var transport = new TestTransport(new DianSubmissionResult(
            DianSubmissionDisposition.Received,
            "track-42",
            "Received",
            "Queued",
            null,
            Encoding.UTF8.GetBytes("response"),
            true));
        var worker = Worker(store, transport);

        Assert.True((await worker.ProcessAsync(
            store.BusinessId, store.DocumentId, "worker-a")).WorkFound);

        Assert.True(transport.StoreWasStartedAtCall);
        Assert.Equal(DianOperationCodes.SendTestSet, store.Started!.Operation);
        Assert.Equal(FiscalDocumentStatusCodes.PendingDianResult, store.Status);
        Assert.Equal("track-42", store.Result!.TrackId);
        Assert.Equal(Now.AddSeconds(5), store.NextAttemptAt);
    }

    [Fact]
    public async Task Existing_track_id_is_queried_and_acceptance_is_terminal()
    {
        var store = new TestStore(Work() with { TrackId = "track-42" });
        var transport = new TestTransport(new DianSubmissionResult(
            DianSubmissionDisposition.Accepted,
            "track-42",
            "00",
            "Accepted",
            Encoding.UTF8.GetBytes("<ApplicationResponse />"),
            Encoding.UTF8.GetBytes("response"),
            true));

        Assert.True((await Worker(store, transport).ProcessAsync(
            store.BusinessId, store.DocumentId, "worker-a")).WorkFound);

        Assert.Equal(DianOperationCodes.GetStatusZip, store.Started!.Operation);
        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted, store.Status);
        Assert.Null(store.NextAttemptAt);
        Assert.Equal(1, transport.QueryCalls);
        Assert.Equal(0, transport.SendCalls);
    }

    [Fact]
    public async Task Ambiguous_send_timeout_is_not_automatically_retransmitted()
    {
        var store = new TestStore(Work());
        var transport = new TestTransport(new DianSubmissionResult(
            DianSubmissionDisposition.TransientFailure,
            null,
            "TimeoutException",
            "Unknown outcome",
            null,
            [],
            true));

        Assert.True((await Worker(store, transport).ProcessAsync(
            store.BusinessId, store.DocumentId, "worker-a")).WorkFound);

        Assert.Equal(FiscalDocumentStatusCodes.PendingDianResult, store.Status);
        Assert.Null(store.NextAttemptAt);
        Assert.Equal(1, transport.SendCalls);
    }

    [Fact]
    public async Task Interrupted_send_is_quarantined_without_calling_transport_again()
    {
        var store = new TestStore(Work() with { HasUnresolvedSendAttempt = true });
        var transport = new TestTransport(new DianSubmissionResult(
            DianSubmissionDisposition.Accepted, null, null, null, null, [], true));

        Assert.True((await Worker(store, transport).ProcessAsync(
            store.BusinessId, store.DocumentId, "worker-a")).WorkFound);

        Assert.True(store.WasMarkedUnknown);
        Assert.Equal(0, transport.SendCalls + transport.QueryCalls);
    }

    [Fact]
    public async Task Missing_test_set_selects_production_SendBillSync()
    {
        var store = new TestStore(Work() with { TestSetId = null });
        var transport = new TestTransport(new DianSubmissionResult(
            DianSubmissionDisposition.Accepted, null, null, null, null, [], true));

        Assert.True((await Worker(store, transport).ProcessAsync(
            store.BusinessId, store.DocumentId, "worker-a")).WorkFound);

        Assert.Equal(FiscalDocumentStatusCodes.DianAccepted, store.Status);
        Assert.Equal(DianOperationCodes.SendBillSync, store.Started!.Operation);
        Assert.Equal(1, transport.SendCalls);
        Assert.Equal(0, transport.QueryCalls);
    }

    [Fact]
    public async Task Production_electronic_payroll_selects_SendNominaSync()
    {
        var store = new TestStore(Work() with
        {
            TestSetId = null,
            FiscalDocumentType = FiscalDocumentTypeCodes.ElectronicPayroll
        });
        var transport = new TestTransport(new DianSubmissionResult(
            DianSubmissionDisposition.Accepted, null, "00", "Accepted", null, [], true));

        Assert.True((await Worker(store, transport).ProcessAsync(
            store.BusinessId, store.DocumentId, "worker-a")).WorkFound);

        Assert.Equal(DianOperationCodes.SendPayrollSync, store.Started!.Operation);
    }

    private static FiscalSubmissionWorker Worker(TestStore store, TestTransport transport)
    {
        transport.Store = store;
        return new FiscalSubmissionWorker(
            store,
            transport,
            new TestProductionTransport(transport),
            new FiscalSubmissionPackageBuilder(),
            new FixedTimeProvider(Now));
    }

    private static FiscalSubmissionWorkItem Work() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "worker-a",
        "SETP42",
        FiscalDocumentTypeCodes.Invoice,
        Guid.NewGuid(),
        Encoding.UTF8.GetBytes("<Invoice>signed</Invoice>"),
        null,
        false);

    private sealed class TestStore(FiscalSubmissionWorkItem work) : IFiscalSubmissionWorkStore
    {
        private bool acquired;
        public FiscalSubmissionAttempt? Started { get; private set; }
        public Guid BusinessId => work.BusinessId;
        public Guid DocumentId => work.DocumentId;
        public DianSubmissionResult? Result { get; private set; }
        public DateTimeOffset? NextAttemptAt { get; private set; }
        public string? Status { get; private set; }
        public string? ErrorCode { get; private set; }
        public bool WasMarkedUnknown { get; private set; }

        public Task<FiscalSubmissionWorkItem?> AcquireAsync(
            Guid businessId, Guid documentId, string workerId, DateTimeOffset acquiredAt, TimeSpan lease,
            CancellationToken cancellationToken)
        {
            if (acquired) return Task.FromResult<FiscalSubmissionWorkItem?>(null);
            acquired = true;
            if (businessId != work.BusinessId || documentId != work.DocumentId) return Task.FromResult<FiscalSubmissionWorkItem?>(null);
            return Task.FromResult<FiscalSubmissionWorkItem?>(work with { WorkerId = workerId });
        }

        public Task<DateTimeOffset?> GetResumeAtAsync(
            Guid businessId, Guid documentId, DateTimeOffset checkedAt,
            TimeSpan lease, CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);

        public Task<FiscalSubmissionAttempt> StartAttemptAsync(
            FiscalSubmissionWorkItem item, string operation, byte[]? submissionZip,
            byte[] sanitizedRequest, DateTimeOffset startedAt,
            CancellationToken cancellationToken)
        {
            Assert.NotEmpty(sanitizedRequest);
            if (operation == DianOperationCodes.SendTestSet) Assert.NotNull(submissionZip);
            Started = new FiscalSubmissionAttempt(
                Guid.NewGuid(),
                1,
                operation,
                "correlation",
                new DianSubmissionRequest(item.BusinessId, item.DocumentId, $"{item.FiscalNumber}.zip",
                    submissionZip ?? [1], item.TestSetId?.ToString("D"), item.TrackId, "correlation"));
            return Task.FromResult(Started);
        }

        public Task CompleteAttemptAsync(
            FiscalSubmissionWorkItem item, FiscalSubmissionAttempt attempt,
            DianSubmissionResult result, DateTimeOffset completedAt,
            DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken)
        {
            Result = result;
            NextAttemptAt = nextAttemptAt;
            Status = result.Disposition switch
            {
                DianSubmissionDisposition.Accepted => FiscalDocumentStatusCodes.DianAccepted,
                DianSubmissionDisposition.Rejected => FiscalDocumentStatusCodes.DianRejected,
                DianSubmissionDisposition.TransientFailure when
                    result.MayHaveReachedDian && result.TrackId is null && item.TrackId is null =>
                    FiscalDocumentStatusCodes.PendingDianResult,
                DianSubmissionDisposition.TransientFailure => FiscalDocumentStatusCodes.RetryScheduled,
                DianSubmissionDisposition.PermanentFailure => FiscalDocumentStatusCodes.PermanentFailure,
                _ => FiscalDocumentStatusCodes.PendingDianResult
            };
            return Task.CompletedTask;
        }

        public Task MarkSubmissionOutcomeUnknownAsync(
            FiscalSubmissionWorkItem item, DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            WasMarkedUnknown = true;
            Status = FiscalDocumentStatusCodes.PendingDianResult;
            return Task.CompletedTask;
        }

        public Task FailConfigurationAsync(
            FiscalSubmissionWorkItem item, string errorCode, string errorMessage,
            DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            Status = FiscalDocumentStatusCodes.PermanentFailure;
            ErrorCode = errorCode;
            return Task.CompletedTask;
        }
    }

    private sealed class TestTransport(DianSubmissionResult result) : IDianHabilitationTransport
    {
        public TestStore? Store { get; set; }
        public int SendCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public bool StoreWasStartedAtCall { get; private set; }

        public Task<DianSubmissionResult> SubmitTestSetAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            StoreWasStartedAtCall = Store?.Started is not null;
            return Task.FromResult(result);
        }

        public Task<DianSubmissionResult> GetStatusZipAsync(
            DianSubmissionRequest request, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            StoreWasStartedAtCall = Store?.Started is not null;
            return Task.FromResult(result);
        }
    }

    private sealed class TestProductionTransport(TestTransport transport) : IDianProductionTransport
    {
        public Task<DianSubmissionResult> SubmitBillSyncAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default) =>
            transport.SubmitTestSetAsync(request, cancellationToken);

        public Task<DianSubmissionResult> SubmitPayrollSyncAsync(
            DianSubmissionRequest request,
            CancellationToken cancellationToken = default) =>
            transport.SubmitTestSetAsync(request, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
