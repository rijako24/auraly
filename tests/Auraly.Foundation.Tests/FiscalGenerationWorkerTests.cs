using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using System.Xml.Linq;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class FiscalGenerationWorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expense_lines_and_supported_withholdings_survive_generation_and_adjustment(bool adjustment)
    {
        var work = CreateWork();
        var sale = work.Sale!;
        var issued = sale.CommercialSnapshot.IssuedAt;
        var accountId = Guid.NewGuid();
        var withholding = new Auraly.Commerce.Taxation.Contracts.WithholdingCalculationSnapshot(119_000m, 6_350m, 112_650m, [
            new(Guid.NewGuid(),1,"RF","Retefuente","IncomeTax","TaxExclusiveAmount",100_000m,2.5m,2_500m,null),
            new(Guid.NewGuid(),1,"RIVA","ReteIVA","Vat","VatAmount",19_000m,15m,2_850m,null),
            new(Guid.NewGuid(),1,"RICA","ReteICA","IndustryCommerce","TaxExclusiveAmount",100_000m,1m,1_000m,"11001")]);
        var expense = new Auraly.Contracts.Expenses.ExpenseDocumentPayload(sale.TenantId, work.BusinessId,
            work.DocumentId, Guid.NewGuid(), null, accountId, null, Guid.NewGuid(), "GAS-1", Guid.NewGuid(),
            "GAS", "00", 1, null, issued, issued.AddDays(30), "COP", "Gasto distribuido",100_000m,19_000m,
            119_000m,null,withholding,PurchaseEvidenceType:"BuyerElectronicSupportDocument",Lines:[
                new(1,accountId,"519595","Servicios",null,null,null,"Servicio A",60_000m,null,"IVA",19m,11_400m,"DeductibleInputVat",null),
                new(2,accountId,"519595","Servicios",null,null,null,"Servicio B",40_000m,null,"IVA",19m,7_600m,"CapitalizedCost",null)]);
        var snapshot = new PurchaseSupportFiscalSnapshot(null,work.Issuer.Id,work.FiscalNumber,2,
            "https://example.test/qr",sale.UblSnapshot!.Customer,sale.UblSnapshot.Authorization,[
                new(1,"GASTO-1","999","EA","IVA","01"),new(2,"GASTO-2","999","EA","IVA","01")],
            Expense:expense,SellerPostalZone:"110111");
        work = work with { Sale=null,FiscalDocumentType=FiscalDocumentTypeCodes.SupportDocument,SupportDocument=snapshot };
        if (adjustment)
        {
            var cancellation = new Auraly.Contracts.Expenses.ExpenseCancellationPayload(sale.TenantId,work.BusinessId,
                Guid.NewGuid(),Guid.NewGuid(),issued.AddDays(1),"Anulación",expense,null,112_650m,0m);
            work = work with { DocumentId=cancellation.CancellationId,FiscalDocumentType=FiscalDocumentTypeCodes.SupportDocumentAdjustment,
                SupportDocument=snapshot with { Expense=null,ExpenseCancellation=cancellation,OriginalSupportNumber="SETP1",
                    OriginalSupportCuds=new string('a',96),OriginalSupportIssuedOn=DateOnly.FromDateTime(issued.Date) } };
        }
        var store = new TestStore(work);
        var processed = await CreateWorker(store).ProcessAsync(work.BusinessId,work.DocumentId,"worker-a",CancellationToken.None);
        Assert.True(processed, store.ErrorMessage);
        Assert.True(store.Completed is not null,store.ErrorMessage);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(store.Completed!.UnsignedXml));
        XNamespace cac=DianUblNamespaces.Cac,cbc=DianUblNamespaces.Cbc;
        Assert.Equal(2,xml.Root!.Elements(cac+(adjustment?"CreditNoteLine":"InvoiceLine")).Count());
        Assert.Equal(119_000m,(decimal)xml.Descendants(cbc+"PayableAmount").Single());
        // CreditNote 2.1 does not permit WithholdingTaxTotal; the adjustment
        // cancels the referenced support, while accounting reverses all retentions.
        Assert.Equal(adjustment ? 0m : 5_350m,xml.Root.Elements(cac+"WithholdingTaxTotal").Sum(group=>(decimal)group.Element(cbc+"TaxAmount")!));
        Assert.Equal(adjustment ? 0 : 2,xml.Root.Elements(cac+"WithholdingTaxTotal").Count());
        Assert.Contains("Servicio A",xml.ToString()); Assert.Contains("Servicio B",xml.ToString());
    }

    [Fact]
    public async Task Generates_from_the_immutable_snapshot_after_master_data_changes()
    {
        var work = CreateWork();
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.True(await worker.ProcessAsync(
            work.BusinessId, work.DocumentId, "worker-a"));

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
        var work = CreateWork() with { Sale = CreateWork().Sale! with { UblSnapshot = null } };
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.False(await worker.ProcessAsync(
            work.BusinessId, work.DocumentId, "worker-a"));

        Assert.Null(store.Completed);
        Assert.Equal(FiscalDocumentStatusCodes.MissingMandatoryFiscalData, store.FinalStatus);
        Assert.Contains("no UBL snapshot", store.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ubl_tax_rate_must_match_the_immutable_sale_line()
    {
        var work = CreateWork();
        var line = work.Sale!.Lines.Single();
        work = work with
        {
            Sale = work.Sale with { Lines = [line with { TaxRate = 5m }] }
        };
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.False(await worker.ProcessAsync(
            work.BusinessId, work.DocumentId, "worker-a"));

        Assert.Null(store.Completed);
        Assert.Equal(FiscalDocumentStatusCodes.MissingMandatoryFiscalData, store.FinalStatus);
        Assert.Contains("tax rate differs", store.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoice_generation_preserves_the_original_fiscal_signing_date(
        bool isCorrection)
    {
        var work = CreateWork() with { IsCorrection = isCorrection };
        var store = new TestStore(work);
        var worker = CreateWorker(store);

        Assert.True(await worker.ProcessAsync(
            work.BusinessId, work.DocumentId, "worker-a"));

        var artifacts = Assert.IsType<FiscalGeneratedArtifacts>(store.Completed);
        Assert.Equal(work.Sale!.FiscalSnapshot!.IssuedAt, artifacts.SignedAt);
        Assert.NotEqual(artifacts.GeneratedAt, artifacts.SignedAt);
    }

    [Fact]
    public async Task Invoice_generation_derives_a_customer_NIT_check_digit()
    {
        var work = CreateWork();
        var customer = work.Sale!.UblSnapshot!.Customer with
        {
            Identification = "900172649",
            CheckDigit = "0",
            IdentificationTypeCode = "NIT",
            OrganizationTypeCode = "1",
            RegistrationName = "CLIENTE NIT",
            TradeName = "CLIENTE NIT",
            TaxSchemeId = "01",
            TaxSchemeName = "IVA"
        };
        work = work with
        {
            Sale = work.Sale with
            {
                CommercialSnapshot = work.Sale.CommercialSnapshot with
                    { CustomerIdentification = customer.Identification },
                FiscalSnapshot = work.Sale.FiscalSnapshot! with
                    { CustomerIdentification = customer.Identification },
                UblSnapshot = work.Sale.UblSnapshot with { Customer = customer }
            }
        };
        var store = new TestStore(work);

        Assert.True(await CreateWorker(store).ProcessAsync(
            work.BusinessId, work.DocumentId, "worker-a"));

        var xml = Encoding.UTF8.GetString(
            Assert.IsType<FiscalGeneratedArtifacts>(store.Completed).UnsignedXml);
        Assert.Contains("schemeID=\"1\"", xml, StringComparison.Ordinal);
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission, store.FinalStatus);
    }

    [Theory]
    [InlineData("Always", 0, 2, 16900, 1900)]
    [InlineData("Always", 19, 2, 16900, 2698.32)]
    [InlineData("Never", 19, 1, 11900, 1900)]
    public async Task Charge_fiscal_lines_reconcile_taxes_without_inventing_product_lines(
        string inclusion, decimal taxRate, int lineCount, decimal total, decimal vat)
    {
        var work = CreateWork();
        var sale = work.Sale!;
        var definition = new InvoiceChargeDefinition(Guid.NewGuid(), work.BusinessId, 1,
            "DOM", "Domicilio", true, 0, "Fixed", 5000, inclusion, null,
            Guid.NewGuid(), "Domicilios", Guid.NewGuid(), "TEST", "Gasto", null, null,
            Guid.NewGuid(), "IVA", "01", taxRate, [], [new(Guid.NewGuid(), "Proveedor", "TEST", 0, true)],
            Guid.NewGuid(), "IVA", 0);
        var charge = InvoiceChargeApplication.Calculate(11900,
            new InvoiceChargeSelection(Guid.NewGuid(), definition, definition.Suppliers[0].SupplierId, null));
        var commercial = sale.CommercialSnapshot with { UntaxedAmount = total - vat, TaxAmount = vat,
            PayableAmount = total, Taxes = [new("01", vat)] };
        work = work with { Sale = sale with { Charges = [charge], CommercialSnapshot = commercial,
            FiscalSnapshot = sale.FiscalSnapshot! with { UntaxedAmount = total - vat, TaxAmount = vat,
                PayableAmount = total, Taxes = commercial.Taxes }, Payments = [new(1, "Cash", total, null)] } };
        var store = new TestStore(work);
        Assert.True(await CreateWorker(store).ProcessAsync(work.BusinessId, work.DocumentId, "worker-fees"));
        Assert.True(store.Completed is not null, store.ErrorMessage);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(store.Completed!.UnsignedXml));
        XNamespace aggregate = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
        XNamespace basic = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        var lines = xml.Root!.Elements(aggregate + "InvoiceLine").ToArray();
        Assert.Equal(lineCount, lines.Length);
        Assert.Equal(total - vat, lines.Sum(line => (decimal)line.Element(basic + "LineExtensionAmount")!));
        Assert.Equal(total, (decimal)xml.Root.Element(aggregate + "LegalMonetaryTotal")!.Element(basic + "PayableAmount")!);
        Assert.Equal(vat, xml.Root.Elements(aggregate + "TaxTotal").Sum(tax => (decimal)tax.Element(basic + "TaxAmount")!));
        Assert.Single(work.Sale!.Lines);
        Assert.Equal(inclusion == "Always", lines.Any(line => line.ToString().Contains("Domicilio", StringComparison.Ordinal)));
    }

    private static FiscalGenerationWorker CreateWorker(TestStore store) => new(
        store, new TestPinProvider(), new DianInvoiceUblBuilder(),
        new DianSupportDocumentUblBuilder(), new DianCreditNoteUblBuilder(),
        new DianDebitNoteUblBuilder(), new DianSchemaValidator(),
        new DianPayrollXmlBuilder(), new DianPayrollSchemaValidator(),
        new PassthroughSigner(), new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero)));

    private static FiscalGenerationWorkItem CreateWork()
    {
        var businessId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var issued = new DateTimeOffset(2026, 7, 29, 8, 30, 0, TimeSpan.FromHours(-5));
        var address = new PosSaleUblAddressContract("11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract("900123456", "8", "31", "1",
            "EMISOR CONGELADO", "EMISOR CONGELADO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "13", "2",
            "CLIENTE CONGELADO", "CLIENTE CONGELADO", "R-99-PN", "ZZ", "No aplica", address,
            "cliente@example.com", "3000000000");
        var sale = new PosSaleUploadRequest(Guid.NewGuid(), businessId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), documentId,
            new PosSaleDocumentNumberContract(Guid.NewGuid(), PosSaleDocumentTypes.Invoice,
                "VTA", "01", 1, 8, "VTA01-00000001"),
            new PosSaleCommercialSnapshotContract(PosSaleDocumentTypes.Invoice, issued,
                "222222222", [new PosSaleTaxContract("01", 1900m)],
                10000m, 1900m, 11900m),
            new PosSaleFiscalSnapshotContract(
                Guid.NewGuid(), Guid.NewGuid(), "18760000001",
                PosSaleDocumentTypes.Invoice, "SETP1", "SETP", 1, issued, "900123456",
                "222222222", 2, "v1", [new PosSaleTaxContract("01", 1900m)],
                10000m, 1900m, 11900m, new string('a', 96), "https://example.test/qr"),
            [new PosSaleLineContract(1, Guid.NewGuid(), "PRODUCTO CONGELADO", "01", 1m,
                10000m, 0m, 1900m, 10000m, 11900m, 19m, 6_000m)],
            [new PosSalePaymentContract(1, "10", 11900m, null)],
            new PosSaleUblSnapshotContract(configId, "COP", "01", supplier, customer,
                new PosSaleUblAuthorizationContract("18760000001", new DateOnly(2026, 1, 1),
                    new DateOnly(2027, 1, 1), "SETP", 1, 1000),
                "software-id", [new PosSaleUblLineContract(1, "SKU-1", "999", "EA", "IVA", 19m)],
                "1", "10", DateOnly.FromDateTime(issued.Date), null));
        var issuer = new FiscalIssuerWorkConfiguration(configId, businessId, "900123456", "8",
            "MAESTRO MODIFICADO", "MAESTRO MODIFICADO", "R-99-PN", "01", "IVA", "31",
            address, "software-id", "env://TEST_PIN", 2, "test", "test", string.Empty,
            "1.9", "test-generator");
        var authorization = new FiscalAuthorizationWorkConfiguration("18760000001",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "SETP", 1, 1000);
        return new FiscalGenerationWorkItem(documentId, businessId, "worker-a",
            FiscalDocumentTypeCodes.Invoice, sale.FiscalSnapshot!.FiscalNumber, sale, null, null,
            issuer, authorization);
    }

    private sealed class TestStore(FiscalGenerationWorkItem work) : IFiscalGenerationWorkStore
    {
        private bool acquired;
        public FiscalGeneratedArtifacts? Completed { get; private set; }
        public string? FinalStatus { get; private set; }
        public string? ErrorMessage { get; private set; }
        public Task<FiscalGenerationWorkItem?> AcquireAsync(
            Guid businessId, Guid documentId, string workerId,
            DateTimeOffset acquiredAt, TimeSpan lease, CancellationToken cancellationToken)
        {
            if (acquired) return Task.FromResult<FiscalGenerationWorkItem?>(null);
            if (businessId != work.BusinessId || documentId != work.DocumentId) return Task.FromResult<FiscalGenerationWorkItem?>(null);
            acquired = true;
            return Task.FromResult<FiscalGenerationWorkItem?>(work with { WorkerId = workerId });
        }
        public Task<DateTimeOffset?> GetResumeAtAsync(
            Guid businessId, Guid documentId, DateTimeOffset checkedAt,
            TimeSpan lease, CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);
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
