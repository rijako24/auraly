using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed class DianXadesSignerTests : IDisposable
{
    private readonly X509Certificate2 certificate = CreateCertificate(
        "900123456", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

    [Fact]
    public async Task Signed_ubl_is_cryptographically_valid_and_passes_xsd()
    {
        var unsigned = new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml;
        var signer = CreateSigner(certificate);

        var result = await signer.SignAsync(CreateRequest(unsigned, certificate));

        DianXadesSigner.VerifySignature(result.SignedXml, certificate);
        var validation = new DianSchemaValidator().Validate(result.SignedXml);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Contains("SignedProperties", Encoding.UTF8.GetString(result.SignedXml), StringComparison.Ordinal);
        Assert.Contains(DianXadesSigner.PolicySha256Base64, Encoding.UTF8.GetString(result.SignedXml), StringComparison.Ordinal);

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(Encoding.UTF8.GetString(result.SignedXml));
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        manager.AddNamespace("xades", DianUblNamespaces.Xades.NamespaceName);
        var references = document.SelectNodes("//ds:Signature/ds:SignedInfo/ds:Reference", manager)!;
        Assert.Equal(3, references.Count);
        var keyInfoId = document.SelectSingleNode("//ds:Signature/ds:KeyInfo/@Id", manager)!.Value;
        var signedPropertiesId = document.SelectSingleNode(
            "//ds:Signature/ds:Object/xades:QualifyingProperties/xades:SignedProperties/@Id",
            manager)!.Value;
        Assert.NotNull(document.SelectSingleNode(
            $"//ds:Reference[@URI='#{keyInfoId}']", manager));
        Assert.NotNull(document.SelectSingleNode(
            $"//ds:Reference[@URI='#{signedPropertiesId}' and @Type='http://uri.etsi.org/01903#SignedProperties']",
            manager));
        Assert.All(
            references.OfType<XmlElement>(),
            reference => Assert.Equal(
                "http://www.w3.org/2001/04/xmlenc#sha256",
                reference.SelectSingleNode("ds:DigestMethod/@Algorithm", manager)!.Value));
        Assert.Equal(
            SignedXml.XmlDsigRSASHA256Url,
            document.SelectSingleNode("//ds:SignatureMethod/@Algorithm", manager)!.Value);
        Assert.NotNull(document.SelectSingleNode("//ds:KeyInfo/ds:X509Data/ds:X509Certificate", manager));
        Assert.NotNull(document.SelectSingleNode("//ds:KeyInfo/ds:KeyValue/ds:RSAKeyValue", manager));
        Assert.Single(document.SelectNodes(
            "//xades:SigningCertificate/xades:Cert", manager)!.OfType<XmlElement>());
        Assert.Equal("supplier", document.SelectSingleNode(
            "//xades:SignerRole/xades:ClaimedRoles/xades:ClaimedRole", manager)!.InnerText);
        var dataObjectFormat = document.SelectSingleNode(
            "//xades:SignedDataObjectProperties/xades:DataObjectFormat", manager)!;
        Assert.Equal(
            "#" + references[0]!.Attributes!["Id"]!.Value,
            dataObjectFormat.Attributes!["ObjectReference"]!.Value);
        Assert.Equal("text/xml", dataObjectFormat.SelectSingleNode(
            "xades:MimeType", manager)!.InnerText);
        Assert.Equal("UTF-8", dataObjectFormat.SelectSingleNode(
            "xades:Encoding", manager)!.InnerText);
        Assert.Equal(
            "http://www.w3.org/2001/04/xmlenc#sha256",
            document.SelectSingleNode("//xades:SigPolicyHash/ds:DigestMethod/@Algorithm", manager)!.Value);
        Assert.EndsWith("-05:00", document.SelectSingleNode(
            "//xades:SigningTime", manager)!.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_signed_document_invalidates_signature()
    {
        var unsigned = new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml;
        var result = await CreateSigner(certificate).SignAsync(CreateRequest(unsigned, certificate));
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(Encoding.UTF8.GetString(result.SignedXml));
        var id = document.GetElementsByTagName("ID", DianUblNamespaces.Cbc.NamespaceName)
            .OfType<XmlElement>().First();
        id.InnerText = "SETP990000999";

        Assert.Throws<CryptographicException>(() =>
            DianXadesSigner.VerifySignature(Encoding.UTF8.GetBytes(document.OuterXml), certificate));
    }

    [Fact]
    public async Task Expired_certificate_is_rejected()
    {
        using var expired = CreateCertificate(
            "900123456", DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-2));
        var unsigned = new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml;

        await Assert.ThrowsAsync<CryptographicException>(() =>
            CreateSigner(expired).SignAsync(CreateRequest(unsigned, expired)));
    }

    [Fact]
    public async Task Certificate_from_another_issuer_is_rejected()
    {
        using var other = CreateCertificate(
            "800999999", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var unsigned = new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml;

        await Assert.ThrowsAsync<CryptographicException>(() =>
            CreateSigner(other).SignAsync(CreateRequest(unsigned, other)));
    }

    [Fact]
    public async Task Certificate_without_private_key_is_rejected()
    {
        using var publicOnly = new X509Certificate2(certificate.Export(X509ContentType.Cert));
        var unsigned = new DianInvoiceUblBuilder().Build(CreateInvoice()).Xml;

        await Assert.ThrowsAsync<CryptographicException>(() =>
            CreateSigner(publicOnly).SignAsync(CreateRequest(unsigned, publicOnly)));
    }

    public void Dispose() => certificate.Dispose();

    private static DianXadesSigner CreateSigner(X509Certificate2 value) =>
        new(new TestCertificateProvider(value));

    private static FiscalSigningRequest CreateRequest(byte[] xml, X509Certificate2 value) =>
        new(
            Guid.Parse("018f53e0-fd20-7a21-bb61-65b5ea9a1111"),
            "900123456",
            xml,
            new FiscalCertificateReference(
                Guid.Parse("018f53e0-fd20-7a21-bb61-65b5ea9a1111"),
                "test-ephemeral",
                "generated",
                value.Thumbprint),
            DateTimeOffset.UtcNow);

    private static X509Certificate2 CreateCertificate(
        string taxId, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN=Auraly Fiscal Test,O=Auraly,SERIALNUMBER={taxId}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
            critical: true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static DianInvoice CreateInvoice()
    {
        var address = new DianAddress("11001", "Bogotá", "Bogotá D.C.", "11", "Carrera 8 # 6C-38");
        var supplier = new DianParty("900123456", "8", "31", "1", "Auraly Comercio SAS", "Auraly",
            "O-48", "01", "IVA", address, "facturacion@auraly.test", "6015550000");
        var customer = new DianParty("222222222222", "0", "13", "2", "Consumidor final", "Consumidor final",
            "R-99-PN", "ZZ", "No aplica", address);
        var tax = new DianTax("01", "IVA", 10_000m, 1_900m, 19m);
        var line = new DianInvoiceLine(1, "7701234567890", "010", "Producto de prueba", "EA",
            2m, 5_000m, 0m, 10_000m, [tax]);
        return new DianInvoice(
            "SETP990000001", new string('a', 96),
            new DateTimeOffset(2026, 7, 28, 10, 15, 30, TimeSpan.FromHours(-5)),
            "COP", "01", 2,
            new DianAuthorization("18760000001", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), "SETP", 990000000, 995000000),
            new DianSoftware("900123456", "8", "56f2ae4e-9812-4fad-9255-08fcfcd5ccb0", "20191"),
            supplier, customer, [line], [tax],
            new DianPayment("1", "10", new DateOnly(2026, 7, 28), null),
            10_000m, 10_000m, 11_900m, 0m, 11_900m,
            "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + new string('a', 96));
    }

    private sealed class TestCertificateProvider(X509Certificate2 certificate)
        : IFiscalSigningCertificateProvider
    {
        public Task<FiscalCertificateMaterial> ResolveAsync(
            FiscalCertificateReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FiscalCertificateMaterial(certificate, []));
    }
}
