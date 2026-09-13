using System.IO.Compression;
using System.Text;
using Azure.Communication.Email;
using Auraly.Api;
using Auraly.Fiscal.Ubl;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class FiscalInvoiceEmailPackageTests
{
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
    public void Zip_contains_only_the_signed_attached_document()
    {
        var attachedDocument = Encoding.UTF8.GetBytes("<AttachedDocument>signed</AttachedDocument>");
        var issuedAt = new DateTimeOffset(2026, 9, 12, 9, 15, 0, TimeSpan.FromHours(-5));

        var zip = PlatformEmailOutboxHostedService.BuildFiscalContainer(
            "AttachedDocument-SETP42.xml", attachedDocument, issuedAt);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("AttachedDocument-SETP42.xml", entry.FullName);
        using var content = new MemoryStream();
        using (var source = entry.Open()) source.CopyTo(content);
        Assert.Equal(attachedDocument, content.ToArray());
        Assert.Equal(zip, PlatformEmailOutboxHostedService.BuildFiscalContainer(
            "AttachedDocument-SETP42.xml", attachedDocument, issuedAt));
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
