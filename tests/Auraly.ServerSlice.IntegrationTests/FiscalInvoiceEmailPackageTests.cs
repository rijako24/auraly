using System.IO.Compression;
using System.Text;
using Azure.Communication.Email;
using Auraly.Api;
using Auraly.Fiscal.Ubl;
using Auraly.Contracts.Sales;
using System.Text.Json;

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
