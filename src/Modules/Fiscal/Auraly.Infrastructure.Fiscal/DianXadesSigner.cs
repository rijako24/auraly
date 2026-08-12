using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Ubl;

namespace Auraly.Infrastructure.Fiscal;

public sealed record FiscalCertificateMaterial(
    X509Certificate2 Certificate,
    IReadOnlyList<X509Certificate2> Chain,
    bool RequireTrustedChain);

public interface IFiscalSigningCertificateProvider
{
    Task<FiscalCertificateMaterial> ResolveAsync(
        FiscalCertificateReference reference,
        CancellationToken cancellationToken = default);
}

public sealed class DianXadesSigner(IFiscalSigningCertificateProvider certificates)
    : IFiscalXmlSigner
{
    public const string PolicyUrl =
        "https://facturaelectronica.dian.gov.co/politicadefirma/v2/politicadefirmav2.pdf";
    public const string PolicySha256Base64 =
        "dMoMvtcG5aIzgYo0tIsSQeVJBDnUnfSOfBpxXrmor0Y=";

    public async Task<FiscalSigningResult> SignAsync(
        FiscalSigningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var material = await certificates.ResolveAsync(request.Certificate, cancellationToken);
        ValidateCertificate(material, request);
        using var rsa = material.Certificate.GetRSAPrivateKey()
            ?? throw new CryptographicException("The fiscal certificate does not expose an RSA private key.");

        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var stream = new MemoryStream(request.UnsignedXml, writable: false))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        }))
        {
            document.Load(reader);
        }

        var extensionContent = AppendSignatureExtension(document);
        var signatureId = $"xmldsig-{Guid.NewGuid():D}";
        var keyInfoId = $"{signatureId}-keyinfo";
        var signedPropertiesId = $"{signatureId}-signedprops";
        var signedXml = new IdAwareSignedXml(document)
        {
            SigningKey = rsa
        };
        signedXml.Signature.Id = signatureId;
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var documentReference = new Reference(string.Empty)
        {
            Id = $"{signatureId}-ref0",
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        documentReference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signedXml.AddReference(documentReference);

        var keyInfo = new KeyInfo { Id = keyInfoId };
        var x509Data = new KeyInfoX509Data(material.Certificate);
        foreach (var certificate in material.Chain)
        {
            if (!string.Equals(certificate.Thumbprint, material.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                x509Data.AddCertificate(certificate);
        }
        keyInfo.AddClause(x509Data);
        signedXml.KeyInfo = keyInfo;

        var qualifyingProperties = BuildQualifyingProperties(
            material,
            signatureId,
            signedPropertiesId,
            request.SigningTime);
        var objectDocument = new XmlDocument { PreserveWhitespace = true };
        var objectRoot = objectDocument.CreateElement("xades-object");
        objectRoot.SetAttribute("xmlns", SignedXml.XmlDsigNamespaceUrl);
        objectDocument.AppendChild(objectRoot);
        objectRoot.AppendChild(objectDocument.ImportNode(qualifyingProperties, deep: true));
        var dataObject = new DataObject { Data = objectRoot.ChildNodes };
        signedXml.AddObject(dataObject);
        var signedProperties = objectRoot.SelectSingleNode("xades:QualifyingProperties/xades:SignedProperties", CreateXadesNamespaceManager(objectDocument)) as XmlElement
            ?? throw new CryptographicException("The XAdES SignedProperties element is missing.");
        signedXml.RegisterId(signedPropertiesId, signedProperties);
        var signedPropertiesReference = new Reference($"#{signedPropertiesId}")
        {
            Type = "http://uri.etsi.org/01903#SignedProperties",
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        signedPropertiesReference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(signedPropertiesReference);
        signedXml.ComputeSignature();
        var signature = document.ImportNode(signedXml.GetXml(), deep: true);
        extensionContent.AppendChild(signature);
        var signedBytes = Serialize(document);
        VerifySignature(signedBytes, material.Certificate);
        return new FiscalSigningResult(
            signedBytes,
            Convert.ToHexString(SHA256.HashData(signedBytes)).ToLowerInvariant(),
            material.Certificate.Thumbprint,
            request.SigningTime);
    }

    public static void VerifySignature(ReadOnlyMemory<byte> xml, X509Certificate2 certificate)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var stream = new MemoryStream(xml.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        document.Load(reader);
        var signature = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>()
            .SingleOrDefault()
            ?? throw new CryptographicException("The signed UBL does not contain exactly one XML signature.");
        var signedXml = new IdAwareSignedXml(document);
        signedXml.LoadXml(signature);
        if (!signedXml.CheckSignature(certificate, verifySignatureOnly: true))
            throw new CryptographicException("The fiscal XML signature is invalid.");
    }

    private static void ValidateCertificate(FiscalCertificateMaterial material, FiscalSigningRequest request)
    {
        var certificate = material.Certificate;
        if (!certificate.HasPrivateKey) throw new CryptographicException("The fiscal certificate lacks a private key.");
        if (request.SigningTime.UtcDateTime < certificate.NotBefore.ToUniversalTime() ||
            request.SigningTime.UtcDateTime > certificate.NotAfter.ToUniversalTime())
            throw new CryptographicException("The fiscal certificate is not valid at the signing time.");
        if (!string.IsNullOrWhiteSpace(request.Certificate.ExpectedThumbprint) &&
            !string.Equals(certificate.Thumbprint, request.Certificate.ExpectedThumbprint,
                StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The fiscal certificate thumbprint differs from configuration.");
        var normalizedSubject = certificate.Subject.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        var normalizedTaxId = request.SupplierTaxId.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (!normalizedSubject.Contains(normalizedTaxId, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The fiscal certificate does not identify the configured issuer.");
        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        if (keyUsage is not null &&
            (keyUsage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
            throw new CryptographicException("The fiscal certificate is not enabled for digital signatures.");
        if (material.RequireTrustedChain)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationTime = request.SigningTime.UtcDateTime;
            foreach (var extra in material.Chain) chain.ChainPolicy.ExtraStore.Add(extra);
            if (!chain.Build(certificate))
                throw new CryptographicException("The fiscal certificate chain could not be validated.");
        }
    }

    private static XmlNamespaceManager CreateXadesNamespaceManager(XmlDocument document)
    {
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("xades", DianUblNamespaces.Xades.NamespaceName);
        return manager;
    }
    private static XmlElement AppendSignatureExtension(XmlDocument document)
    {
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("ext", DianUblNamespaces.Ext.NamespaceName);
        var extensions = document.SelectSingleNode("/*/ext:UBLExtensions", manager) as XmlElement
            ?? throw new XmlException("The UBL extensions container is missing.");
        var extension = document.CreateElement("ext", "UBLExtension", DianUblNamespaces.Ext.NamespaceName);
        var content = document.CreateElement("ext", "ExtensionContent", DianUblNamespaces.Ext.NamespaceName);
        extension.AppendChild(content);
        extensions.AppendChild(extension);
        return content;
    }

    private static XmlElement BuildQualifyingProperties(
        FiscalCertificateMaterial material,
        string signatureId,
        string signedPropertiesId,
        DateTimeOffset signingTime)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        var qualifying = document.CreateElement("xades", "QualifyingProperties", DianUblNamespaces.Xades.NamespaceName);
        qualifying.SetAttribute("Target", $"#{signatureId}");
        var signedProperties = document.CreateElement("xades", "SignedProperties", DianUblNamespaces.Xades.NamespaceName);
        signedProperties.SetAttribute("Id", signedPropertiesId);
        var signedSignatureProperties = document.CreateElement("xades", "SignedSignatureProperties", DianUblNamespaces.Xades.NamespaceName);
        AddText(document, signedSignatureProperties, "xades", "SigningTime", DianUblNamespaces.Xades.NamespaceName,
            signingTime.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", System.Globalization.CultureInfo.InvariantCulture));
        var signingCertificate = document.CreateElement("xades", "SigningCertificate", DianUblNamespaces.Xades.NamespaceName);
        foreach (var certificate in new[] { material.Certificate }.Concat(material.Chain)
                     .DistinctBy(item => item.Thumbprint, StringComparer.OrdinalIgnoreCase))
            signingCertificate.AppendChild(BuildCertificate(document, certificate));
        signedSignatureProperties.AppendChild(signingCertificate);
        signedSignatureProperties.AppendChild(BuildPolicy(document));
        var role = document.CreateElement("xades", "SignerRole", DianUblNamespaces.Xades.NamespaceName);
        var claimedRoles = document.CreateElement("xades", "ClaimedRoles", DianUblNamespaces.Xades.NamespaceName);
        AddText(document, claimedRoles, "xades", "ClaimedRole", DianUblNamespaces.Xades.NamespaceName, "supplier");
        role.AppendChild(claimedRoles);
        signedSignatureProperties.AppendChild(role);
        signedProperties.AppendChild(signedSignatureProperties);
        qualifying.AppendChild(signedProperties);
        document.AppendChild(qualifying);
        return qualifying;
    }

    private static XmlElement BuildCertificate(XmlDocument document, X509Certificate2 certificate)
    {
        var cert = document.CreateElement("xades", "Cert", DianUblNamespaces.Xades.NamespaceName);
        var digest = document.CreateElement("xades", "CertDigest", DianUblNamespaces.Xades.NamespaceName);
        var method = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
        method.SetAttribute("Algorithm", SignedXml.XmlDsigSHA256Url);
        digest.AppendChild(method);
        AddText(document, digest, "ds", "DigestValue", SignedXml.XmlDsigNamespaceUrl,
            Convert.ToBase64String(SHA256.HashData(certificate.RawData)));
        cert.AppendChild(digest);
        var serial = document.CreateElement("xades", "IssuerSerial", DianUblNamespaces.Xades.NamespaceName);
        AddText(document, serial, "ds", "X509IssuerName", SignedXml.XmlDsigNamespaceUrl, certificate.Issuer);
        AddText(document, serial, "ds", "X509SerialNumber", SignedXml.XmlDsigNamespaceUrl,
            new BigInteger(certificate.GetSerialNumber(), isUnsigned: true, isBigEndian: false).ToString());
        cert.AppendChild(serial);
        return cert;
    }

    private static XmlElement BuildPolicy(XmlDocument document)
    {
        var container = document.CreateElement("xades", "SignaturePolicyIdentifier", DianUblNamespaces.Xades.NamespaceName);
        var policyId = document.CreateElement("xades", "SignaturePolicyId", DianUblNamespaces.Xades.NamespaceName);
        var sigPolicyId = document.CreateElement("xades", "SigPolicyId", DianUblNamespaces.Xades.NamespaceName);
        AddText(document, sigPolicyId, "xades", "Identifier", DianUblNamespaces.Xades.NamespaceName, PolicyUrl);
        AddText(document, sigPolicyId, "xades", "Description", DianUblNamespaces.Xades.NamespaceName,
            "Política de firma para facturas electrónicas de la República de Colombia.");
        policyId.AppendChild(sigPolicyId);
        var policyHash = document.CreateElement("xades", "SigPolicyHash", DianUblNamespaces.Xades.NamespaceName);
        var method = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
        method.SetAttribute("Algorithm", SignedXml.XmlDsigSHA256Url);
        policyHash.AppendChild(method);
        AddText(document, policyHash, "ds", "DigestValue", SignedXml.XmlDsigNamespaceUrl, PolicySha256Base64);
        policyId.AppendChild(policyHash);
        container.AppendChild(policyId);
        return container;
    }

    private static void AddText(XmlDocument document, XmlElement parent, string prefix,
        string name, string ns, string value)
    {
        var element = document.CreateElement(prefix, name, ns);
        element.InnerText = value;
        parent.AppendChild(element);
    }

    private static byte[] Serialize(XmlDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None
        })) document.Save(writer);
        return stream.ToArray();
    }

    private sealed class IdAwareSignedXml(XmlDocument document) : SignedXml(document)
    {
        private readonly Dictionary<string, XmlElement> registeredIds = new(StringComparer.Ordinal);

        public void RegisterId(string id, XmlElement element) => registeredIds[id] = element;

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue) =>
            base.GetIdElement(document, idValue) ??
            (registeredIds.TryGetValue(idValue, out var registered) ? registered : null) ??
            document?.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement;
    }}
