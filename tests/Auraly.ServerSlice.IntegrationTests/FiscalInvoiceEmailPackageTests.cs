using System.IO.Compression;
using System.Text;
using Azure.Communication.Email;
using Auraly.Api;
using Auraly.Fiscal.Ubl;
using Auraly.Contracts.Sales;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class FiscalInvoiceEmailPackageTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3000, 0)]
    [InlineData(3000, 100)]
    public void Email_preserves_real_payment_allocations_credit_and_withholding(decimal credit, decimal retained)
    {
        var receipt = new OnlineSalesReceipt(Guid.NewGuid(), "SalesInvoice", "FE1", "FE1",
            DateTimeOffset.UtcNow, "123", [], [], 10000, 1900, 11900, "cufe", "qr", null, "Cliente");
        var payments = new OnlineSalesPayment[] {
            new("Cash", 1000, null, TenderedAmount: 2000), new("Transfer", 10900 - credit - retained, "ABC") };
        var withholding = retained == 0 ? null : JsonSerializer.Serialize(new {
            grossAmount = 11900m, withholdingTotal = retained, netAmount = 11900m - retained,
            lines = Array.Empty<object>() });
        var projected = PlatformEmailOutboxHostedService.ApplyInvoiceSettlement(receipt, "FV-123",
            JsonSerializer.Serialize(payments), credit, withholding);
        Assert.Equal("FV-123", projected.DocumentNumber);
        Assert.Equal(payments[0], projected.Payments[0]);
        Assert.Equal(payments[1], projected.Payments[1]);
        Assert.Equal(credit, projected.Payments.Where(x => x.MethodCode == "Credit").Sum(x => x.Amount));
        Assert.Equal(11900m - retained, projected.NetPayableAmount);
        Assert.Equal(retained, projected.WithholdingTotal);
        Assert.Throws<InvalidOperationException>(() => PlatformEmailOutboxHostedService.ApplyInvoiceSettlement(
            receipt, "FV-123", JsonSerializer.Serialize(payments), credit + 1, withholding));
    }

    [Fact]
    public void Credit_only_email_accepts_the_empty_payment_projection()
    {
        var receipt = new OnlineSalesReceipt(Guid.NewGuid(), "SalesInvoice", "FE1", "FE1",
            DateTimeOffset.UtcNow, "123", [], [], 10000, 1900, 11900, "cufe", "qr", null, "Cliente");

        var projected = PlatformEmailOutboxHostedService.ApplyInvoiceSettlement(
            receipt, "FV-124", "[]",
            receipt.PayableAmount, null);

        var payment = Assert.Single(projected.Payments);
        Assert.Equal("Credit", payment.MethodCode);
        Assert.Equal(receipt.PayableAmount, payment.Amount);
    }

    [Theory]
    [InlineData(11900, 0, 0, 0)]
    [InlineData(11800, 100, 0, 0)]
    [InlineData(3000, 0, 0, 0)]
    [InlineData(3000, 100, 0.25, 0.75)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 100, 0.25, -0.25)]
    public void Email_and_pos_present_identical_payments_including_credit_and_rounding(
        decimal credit, decimal retained, decimal invoiceRounding, decimal paymentRounding)
    {
        var issued = new DateTimeOffset(2026, 9, 22, 12, 34, 0, TimeSpan.FromHours(-5));
        var due = issued.AddDays(30);
        var net = 11900m - retained + invoiceRounding;
        var payments = credit == net ? Array.Empty<PosSalePaymentContract>() :
            new PosSalePaymentContract[] {
                new(1, "Cash", 1000, null, TenderedAmount: 2000, RoundingAdjustment: paymentRounding),
                new(2, "Transfer", net - credit - 1000 - paymentRounding, "ABC", BankAccountId: Guid.NewGuid()) };
        var withholding = retained == 0 ? null : new WithholdingCalculationSnapshot(
            11900m, retained, 11900m - retained, []);
        var request = new PosSaleUploadRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new(Guid.NewGuid(), PosSaleDocumentTypes.Invoice, "FV", "00", 124, 8, "FV00-00000124"),
            new(PosSaleDocumentTypes.Invoice, issued, "123", [], 10000, 1900, 11900m + invoiceRounding,
                withholding, invoiceRounding), null,
            [new(1, Guid.NewGuid(), "Producto", "01", 1, 10000, 0, 1900, 10000, 11900, 19, 6000)],
            payments, Credit: credit == 0 ? null : new(Guid.NewGuid(), credit, due));

        var pos = OnlineSalesReceiptMapper.From(request, null);
        var email = PlatformEmailOutboxHostedService.ApplyInvoiceSettlement(
            pos with { Payments = [], NetPayableAmount = 0, WithholdingTotal = 0, Withholdings = null },
            pos.DocumentNumber, JsonSerializer.Serialize(payments), credit,
            withholding is null ? null : JsonSerializer.Serialize(withholding), credit == 0 ? null : due);

        Assert.Equal(pos.Payments, email.Payments);
        Assert.Equal(net, email.NetPayableAmount);
        Assert.Equal(retained, email.WithholdingTotal);
        Assert.Equal(net, email.Payments.Sum(payment => payment.CollectedAmount));
        if (credit > 0)
        {
            var creditPayment = Assert.Single(email.Payments.Where(payment => payment.MethodCode == "Credit"));
            Assert.Equal(credit, creditPayment.Amount);
            Assert.Equal(due.ToString("O"), creditPayment.Reference);
        }
        var renderer = new DianInvoicePdfRenderer();
        Assert.Equal(renderer.RenderHtml(pos), renderer.RenderHtml(email));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11900)]
    public void Email_without_payments_or_credit_is_rejected(decimal amount)
    {
        var receipt = new OnlineSalesReceipt(Guid.NewGuid(), PosSaleDocumentTypes.Invoice, "FV1", "FE1",
            DateTimeOffset.UtcNow, "123", [], [], amount, 0, amount, "cufe", "qr", null, "Cliente");
        Assert.Throws<InvalidOperationException>(() => PlatformEmailOutboxHostedService.ApplyInvoiceSettlement(
            receipt, "FV1", "[]", 0, null));
    }

    [Fact]
    public void Credit_cannot_be_counted_as_both_a_collection_and_financed_balance()
    {
        var receipt = new OnlineSalesReceipt(Guid.NewGuid(), PosSaleDocumentTypes.Invoice, "FV1", "FE1",
            DateTimeOffset.UtcNow, "123", [], [], 10000, 0, 10000, "cufe", "qr", null, "Cliente");
        Assert.Throws<InvalidOperationException>(() => PlatformEmailOutboxHostedService.ApplyInvoiceSettlement(
            receipt, "FV1", JsonSerializer.Serialize(new[] { new OnlineSalesPayment("Credit", 5000, null) }),
            5000, null));
    }

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 100, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 0, 100)]
    public void Issued_pos_snapshot_is_presented_without_revalidating_its_settlement(
        bool hasCredit, decimal collected, decimal credit)
    {
        var issued = new DateTimeOffset(2026, 9, 22, 12, 34, 0, TimeSpan.FromHours(-5));
        var due = issued.AddDays(30);
        var request = new PosSaleUploadRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new(Guid.NewGuid(), PosSaleDocumentTypes.Invoice, "FV", "00", 124, 8, "FV00-00000124"),
            new(PosSaleDocumentTypes.Invoice, issued, "123", [], 10000, 1900, 11900), null, [],
            collected == 0 ? [] : [new(1, "Cash", collected, "Original")],
            Credit: hasCredit ? new(Guid.NewGuid(), credit, due) : null);

        var receipt = OnlineSalesReceiptMapper.From(request, "DianAccepted");

        Assert.Equal(request.DocumentId, receipt.DocumentId);
        Assert.Equal(11900m, receipt.PayableAmount);
        Assert.Equal(collected, receipt.Payments.Where(payment => payment.MethodCode == "Cash").Sum(payment => payment.Amount));
        if (hasCredit)
            Assert.Equal(new OnlineSalesPayment("Credit", credit, due.ToString("O")), Assert.Single(receipt.Payments));
        else if (collected == 0)
            Assert.Empty(receipt.Payments);
        else
            Assert.Equal(new OnlineSalesPayment("Cash", collected, "Original"), Assert.Single(receipt.Payments));
    }

    [Fact]
    public void Email_message_only_contains_explicit_attachments()
    {
        var attachment = new EmailAttachment(
            "FacturaElectronica-FE1.zip", "application/zip", BinaryData.FromBytes([1, 2, 3]));

        var message = PlatformEmailOutboxHostedService.BuildEmailMessage(
            "notificaciones@example.test", "customer@example.test", "subject",
            "<p>body</p>", "body", [attachment]);

        var actual = Assert.Single(message.Attachments);
        Assert.Equal("FacturaElectronica-FE1.zip", actual.Name);
    }

    [Fact]
    public void Email_message_without_business_attachments_has_none()
    {
        var message = PlatformEmailOutboxHostedService.BuildEmailMessage(
            "notificaciones@example.test", "customer@example.test", "subject",
            "<p>body</p>", "body");

        Assert.Empty(message.Attachments);
    }

    [Fact]
    public void Zip_contains_the_signed_attached_document_and_pdf_representation()
    {
        var attachedDocument = Encoding.UTF8.GetBytes("<AttachedDocument>signed</AttachedDocument>");
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nrepresentation");
        var issuedAt = new DateTimeOffset(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(-5));

        var zip = PlatformEmailOutboxHostedService.BuildFiscalContainer(
            "AttachedDocument-SETP42.xml", attachedDocument,
            "RepresentacionGrafica-SETP42.pdf", pdf, issuedAt);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        var entry = archive.GetEntry("AttachedDocument-SETP42.xml");
        Assert.NotNull(entry);
        using var content = new MemoryStream();
        using (var source = entry!.Open()) source.CopyTo(content);
        Assert.Equal(attachedDocument, content.ToArray());
        var pdfEntry = archive.GetEntry("RepresentacionGrafica-SETP42.pdf");
        Assert.NotNull(pdfEntry);
        using var pdfContent = new MemoryStream();
        using (var source = pdfEntry!.Open()) source.CopyTo(pdfContent);
        Assert.Equal(pdf, pdfContent.ToArray());
        Assert.Equal(zip, PlatformEmailOutboxHostedService.BuildFiscalContainer(
            "AttachedDocument-SETP42.xml", attachedDocument,
            "RepresentacionGrafica-SETP42.pdf", pdf, issuedAt));
    }

    [Fact]
    public void Habilitation_zip_keeps_the_signed_xml_and_test_set_response_separate()
    {
        var signedXml = Encoding.UTF8.GetBytes("<Invoice>signed</Invoice>");
        var status = Encoding.UTF8.GetBytes("{\"StatusCode\":\"2\"}");
        var issuedAt = new DateTimeOffset(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(-5));

        var zip = PlatformEmailOutboxHostedService.BuildHabilitationContainer(
            "FESI115", signedXml, status, issuedAt);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(new[] { "FESI115-dian-test-set-response.json", "FESI115-signed.xml" },
            archive.Entries.Select(entry => entry.FullName).Order().ToArray());
    }

    [Fact]
    public void Subject_uses_the_dian_semicolon_contract_and_removes_header_breaks()
    {
        var metadata = new DianAttachedDocumentMetadata(
            "900123456",
            "Auraly; SAS",
            "Auraly\r\nCommerce",
            "Cliente",
            "SETP42",
            "01",
            "2");

        var subject = PlatformEmailOutboxHostedService.BuildFiscalInvoiceSubject(metadata);

        Assert.Equal("900123456;Auraly, SAS;SETP42;01;Auraly  Commerce", subject);
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
    }
}
