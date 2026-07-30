using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class FiscalGenerationWorkerTests
{
    [Fact]
    public async Task Generates_from_the_immutable_snapshot_after_master_data_changes()
    {
        var work = CreateWork();
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.True(await worker.ProcessNextAsync("worker-a"));

        var artifacts = Assert.IsType<FiscalGeneratedArtifacts>(store.Completed);
        var xml = Encoding.UTF8.GetString(artifacts.UnsignedXml);
        Assert.Contains("EMISOR CONGELADO", xml, StringComparison.Ordinal);
        Assert.Contains("CLIENTE CONGELADO", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("MAESTRO MODIFICADO", xml, StringComparison.Ordinal);
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission, store.FinalStatus);
        Assert.Equal(artifacts.UnsignedSha256Hex,
            Convert.ToHexString(SHA256.HashData(artifacts.UnsignedXml)).ToLowerInvariant());
    }

    [Fact]
    public async Task Missing_historical_ubl_data_is_explicit_and_never_invented()
    {
        var work = CreateWork() with { Sale = CreateWork().Sale with { UblSnapshot = null } };
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.True(await worker.ProcessNextAsync("worker-a"));

        Assert.Null(store.Completed);
        Assert.Equal(FiscalDocumentStatusCodes.MissingMandatoryFiscalData, store.FinalStatus);
        Assert.Contains("no UBL snapshot", store.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ubl_tax_rate_must_match_the_immutable_sale_line()
    {
        var work = CreateWork();
        var line = work.Sale.Lines.Single();
        work = work with
        {
            Sale = work.Sale with { Lines = [line with { TaxRate = 5m }] }
        };
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.True(await worker.ProcessNextAsync("worker-a"));

        Assert.Null(store.Completed);
        Assert.Equal(FiscalDocumentStatusCodes.MissingMandatoryFiscalData, store.FinalStatus);
        Assert.Contains("tax rate differs", store.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
    private static FiscalGenerationWorker CreateWorker(TestStore store) => new(
        store, new TestPinProvider(), new DianInvoiceUblBuilder(), new DianSchemaValidator(),
        new PassthroughSigner(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero)));

    private static FiscalGenerationWorkItem CreateWork()
    {
        var businessId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var issued = new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.FromHours(-5));
        var address = new PosSaleUblAddressContract("11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract("900123456", "7", "31", "1",
            "EMISOR CONGELADO", "EMISOR CONGELADO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "13", "2",
            "CLIENTE CONGELADO", "CLIENTE CONGELADO", "R-99-PN", "ZZ", "No aplica", address,
            "cliente@example.com", "3000000000");
        var sale = new PosSaleUploadRequest(Guid.NewGuid(), businessId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), documentId,
            new PosSaleDocumentNumberContract(Guid.NewGuid(), PosSaleDocumentTypes.Invoice,
                "VTA", "01", 1, 8, "VTA01-00000001"),
            new PosSaleFiscalSnapshotContract(Guid.NewGuid(), Guid.NewGuid(), "18760000001",
                PosSaleDocumentTypes.Invoice, "SETP1", "SETP", 1, issued, "900123456",
                "222222222", 2, "v1", [new PosSaleTaxContract("01", 1900m)],
                10000m, 1900m, 11900m, new string('a', 96), "https://example.test/qr"),
            [new PosSaleLineContract(1, Guid.NewGuid(), "PRODUCTO CONGELADO", "01", 1m,
                10000m, 0m, 1900m, 10000m, 11900m, 19m)],
            [new PosSalePaymentContract(1, "10", 11900m, null)],
            new PosSaleUblSnapshotContract(configId, "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract("18760000001", new DateOnly(2026, 1, 1),
                    new DateOnly(2027, 1, 1), "SETP", 1, 1000),
                "software-id", [new PosSaleUblLineContract(1, "SKU-1", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(issued.Date), null));
        var issuer = new FiscalIssuerWorkConfiguration(configId, businessId, "900123456", "7",
            "MAESTRO MODIFICADO", "MAESTRO MODIFICADO", "R-99-PN", "01", "IVA", "31",
            address, "software-id", "env://TEST_PIN", 2, "test", "test", string.Empty,
            "1.9", "test-generator");
        var authorization = new FiscalAuthorizationWorkConfiguration("18760000001",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "SETP", 1, 1000);
        return new FiscalGenerationWorkItem(documentId, businessId, "worker-a", sale, issuer, authorization);
    }

    private sealed class TestStore(FiscalGenerationWorkItem work) : IFiscalGenerationWorkStore
    {
        private bool acquired;
        public FiscalGeneratedArtifacts? Completed { get; private set; }
        public string? FinalStatus { get; private set; }
        public string? ErrorMessage { get; private set; }
        public Task<FiscalGenerationWorkItem?> AcquireNextAsync(string workerId, DateTimeOffset acquiredAt,
            TimeSpan lease, CancellationToken cancellationToken)
        {
            if (acquired) return Task.FromResult<FiscalGenerationWorkItem?>(null);
            acquired = true;
            return Task.FromResult<FiscalGenerationWorkItem?>(work with { WorkerId = workerId });
        }
        public Task CompleteAsync(FiscalGenerationWorkItem item, FiscalGeneratedArtifacts artifacts,
            CancellationToken cancellationToken)
        {
            Completed = artifacts;
            FinalStatus = FiscalDocumentStatusCodes.PendingSubmission;
            return Task.CompletedTask;
        }
        public Task FailAsync(FiscalGenerationWorkItem item, string status, string errorCode,
            string errorMessage, DateTimeOffset failedAt, CancellationToken cancellationToken)
        {
            FinalStatus = status;
            ErrorMessage = errorMessage;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPinProvider : IFiscalSoftwarePinProvider
    {
        public Task<string> ResolveAsync(Guid businessId, string secretReference,
            CancellationToken cancellationToken) => Task.FromResult("test-pin");
    }

    private sealed class PassthroughSigner : IFiscalXmlSigner
    {
        public Task<FiscalSigningResult> SignAsync(FiscalSigningRequest request,
            CancellationToken cancellationToken = default)
        {
            var hash = Convert.ToHexString(SHA256.HashData(request.UnsignedXml)).ToLowerInvariant();
            return Task.FromResult(new FiscalSigningResult(request.UnsignedXml, hash, "TEST", request.SigningTime));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}