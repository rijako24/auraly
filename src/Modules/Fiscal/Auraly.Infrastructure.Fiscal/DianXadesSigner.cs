using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Core;
using Auraly.Fiscal.Ubl;

namespace Auraly.Infrastructure.Fiscal;

public sealed record FiscalCertificateMaterial(
    X509Certificate2 Certificate,
    IReadOnlyList<X509Certificate2> Chain);

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
    private const string Sha256Url = "http://www.w3.org/2001/04/xmlenc#sha256";

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
        var signature = BuildSignature(
            document,
            material.Certificate,
            rsa,
            DianFiscalDateTime.InColombia(request.SigningTime));
        extensionContent.AppendChild(signature);
        PopulateReferenceDigestsAndSignature(document, signature, rsa);
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
    }

    private static XmlElement AppendSignatureExtension(XmlDocument document)
    {
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("ext", DianUblNamespaces.Ext.NamespaceName);
        var extensions = document.SelectSingleNode("/*/ext:UBLExtensions", manager) as XmlElement;
        if (extensions is null)
        {
            var root = document.DocumentElement
                ?? throw new XmlException("The fiscal XML root is missing.");
            extensions = document.CreateElement(
                "ext", "UBLExtensions", DianUblNamespaces.Ext.NamespaceName);
            root.InsertBefore(extensions, root.FirstChild);
        }
        var extension = document.CreateElement("ext", "UBLExtension", DianUblNamespaces.Ext.NamespaceName);
        var content = document.CreateElement("ext", "ExtensionContent", DianUblNamespaces.Ext.NamespaceName);
        extension.AppendChild(content);
        extensions.AppendChild(extension);
        return content;
    }

    private static XmlElement BuildSignature(
        XmlDocument document,
        X509Certificate2 certificate,
        RSA rsa,
        DateTimeOffset signingTime)
    {
        var id = Guid.NewGuid().ToString("D");
        var signatureId = $"Signature-{id}";
        var signatureValueId = $"SignatureValue-{id}";
        var documentReferenceId = $"Reference-{Guid.NewGuid():D}";
        var keyInfoId = $"{signatureId}-KeyInfo";
        var signedPropertiesId = $"xmldsig-{signatureId}-signedprops";

        var signature = Ds(document, "Signature");
        signature.SetAttribute("xmlns:ds", SignedXml.XmlDsigNamespaceUrl);
        signature.SetAttribute("Id", signatureId);

        var signedInfo = Ds(document, "SignedInfo");
        signature.AppendChild(signedInfo);
        signedInfo.AppendChild(Algorithm(document, "CanonicalizationMethod", SignedXml.XmlDsigC14NTransformUrl));
        signedInfo.AppendChild(Algorithm(document, "SignatureMethod", SignedXml.XmlDsigRSASHA256Url));

        var documentReference = Reference(document, documentReferenceId, string.Empty);
        var transforms = Ds(document, "Transforms");
        transforms.AppendChild(Algorithm(document, "Transform", SignedXml.XmlDsigEnvelopedSignatureTransformUrl));
        documentReference.AppendChild(transforms);
        AppendDigest(document, documentReference);
        signedInfo.AppendChild(documentReference);

        var keyInfoReference = Reference(document, "ReferenceKeyInfo", $"#{keyInfoId}");
        AppendDigest(document, keyInfoReference);
        signedInfo.AppendChild(keyInfoReference);

        var signedPropertiesReference = Reference(document, null, $"#{signedPropertiesId}");
        signedPropertiesReference.SetAttribute("Type", "http://uri.etsi.org/01903#SignedProperties");
        AppendDigest(document, signedPropertiesReference);
        signedInfo.AppendChild(signedPropertiesReference);

        var signatureValue = Ds(document, "SignatureValue");
        signatureValue.SetAttribute("Id", signatureValueId);
        signature.AppendChild(signatureValue);

        var keyInfo = BuildKeyInfo(document, certificate, rsa, keyInfoId);
        signature.AppendChild(keyInfo);
        signature.AppendChild(BuildXadesObject(
            document,
            certificate,
            signatureId,
            signedPropertiesId,
            documentReferenceId,
            signingTime));
        return signature;
    }

    private static XmlElement BuildKeyInfo(
        XmlDocument document,
        X509Certificate2 certificate,
        RSA rsa,
        string keyInfoId)
    {
        var keyInfo = Ds(document, "KeyInfo");
        keyInfo.SetAttribute("Id", keyInfoId);
        var x509Data = Ds(document, "X509Data");
        AddText(document, x509Data, "ds", "X509Certificate", SignedXml.XmlDsigNamespaceUrl,
            Convert.ToBase64String(certificate.RawData));
        keyInfo.AppendChild(x509Data);

        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var keyValue = Ds(document, "KeyValue");
        var rsaKeyValue = Ds(document, "RSAKeyValue");
        AddText(document, rsaKeyValue, "ds", "Modulus", SignedXml.XmlDsigNamespaceUrl,
            Convert.ToBase64String(parameters.Modulus!));
        AddText(document, rsaKeyValue, "ds", "Exponent", SignedXml.XmlDsigNamespaceUrl,
            Convert.ToBase64String(parameters.Exponent!));
        keyValue.AppendChild(rsaKeyValue);
        keyInfo.AppendChild(keyValue);
        return keyInfo;
    }

    private static XmlElement BuildXadesObject(
        XmlDocument document,
        X509Certificate2 certificate,
        string signatureId,
        string signedPropertiesId,
        string documentReferenceId,
        DateTimeOffset signingTime)
    {
        var dataObject = Ds(document, "Object");
        dataObject.SetAttribute("Id", $"XadesObjectId-{Guid.NewGuid():D}");
        var qualifying = document.CreateElement("xades", "QualifyingProperties", DianUblNamespaces.Xades.NamespaceName);
        qualifying.SetAttribute("xmlns:xades", DianUblNamespaces.Xades.NamespaceName);
        qualifying.SetAttribute("Id", $"QualifyingProperties-{Guid.NewGuid():D}");
        qualifying.SetAttribute("Target", $"#{signatureId}");
        dataObject.AppendChild(qualifying);

        var signedProperties = document.CreateElement("xades", "SignedProperties", DianUblNamespaces.Xades.NamespaceName);
        signedProperties.SetAttribute("Id", signedPropertiesId);
        qualifying.AppendChild(signedProperties);
        var signedSignatureProperties = document.CreateElement(
            "xades", "SignedSignatureProperties", DianUblNamespaces.Xades.NamespaceName);
        signedProperties.AppendChild(signedSignatureProperties);
        AddText(document, signedSignatureProperties, "xades", "SigningTime", DianUblNamespaces.Xades.NamespaceName,
            signingTime.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));

        var signingCertificate = document.CreateElement(
            "xades", "SigningCertificate", DianUblNamespaces.Xades.NamespaceName);
        signingCertificate.AppendChild(BuildCertificate(document, certificate));
        signedSignatureProperties.AppendChild(signingCertificate);
        signedSignatureProperties.AppendChild(BuildPolicy(document));

        var role = document.CreateElement("xades", "SignerRole", DianUblNamespaces.Xades.NamespaceName);
        var claimedRoles = document.CreateElement("xades", "ClaimedRoles", DianUblNamespaces.Xades.NamespaceName);
        AddText(document, claimedRoles, "xades", "ClaimedRole", DianUblNamespaces.Xades.NamespaceName, "supplier");
        role.AppendChild(claimedRoles);
        signedSignatureProperties.AppendChild(role);

        var signedDataObjectProperties = document.CreateElement(
            "xades", "SignedDataObjectProperties", DianUblNamespaces.Xades.NamespaceName);
        var dataObjectFormat = document.CreateElement(
            "xades", "DataObjectFormat", DianUblNamespaces.Xades.NamespaceName);
        dataObjectFormat.SetAttribute("ObjectReference", $"#{documentReferenceId}");
        AddText(document, dataObjectFormat, "xades", "MimeType", DianUblNamespaces.Xades.NamespaceName, "text/xml");
        AddText(document, dataObjectFormat, "xades", "Encoding", DianUblNamespaces.Xades.NamespaceName, "UTF-8");
        signedDataObjectProperties.AppendChild(dataObjectFormat);
        signedProperties.AppendChild(signedDataObjectProperties);
        return dataObject;
    }

    private static XmlElement Reference(XmlDocument document, string? id, string uri)
    {
        var reference = Ds(document, "Reference");
        if (!string.IsNullOrWhiteSpace(id)) reference.SetAttribute("Id", id);
        reference.SetAttribute("URI", uri);
        return reference;
    }

    private static void AppendDigest(XmlDocument document, XmlElement reference)
    {
        reference.AppendChild(Algorithm(document, "DigestMethod", Sha256Url));
        reference.AppendChild(Ds(document, "DigestValue"));
    }

    private static XmlElement Algorithm(XmlDocument document, string name, string value)
    {
        var element = Ds(document, name);
        element.SetAttribute("Algorithm", value);
        return element;
    }

    private static XmlElement Ds(XmlDocument document, string name) =>
        document.CreateElement("ds", name, SignedXml.XmlDsigNamespaceUrl);

    private static void PopulateReferenceDigestsAndSignature(
        XmlDocument document,
        XmlElement signature,
        RSA rsa)
    {
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        manager.AddNamespace("xades", DianUblNamespaces.Xades.NamespaceName);

        var references = signature.SelectNodes("ds:SignedInfo/ds:Reference", manager)
            ?.OfType<XmlElement>().ToArray()
            ?? throw new CryptographicException("The XML signature references are missing.");
        if (references.Length != 3)
            throw new CryptographicException("The DIAN XAdES signature must contain exactly three references.");

        var unsignedDocument = (XmlDocument)document.CloneNode(deep: true);
        var unsignedSignature = unsignedDocument.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>().Single();
        unsignedSignature.ParentNode!.RemoveChild(unsignedSignature);
        SetDigest(references[0], SHA256.HashData(Canonicalize(unsignedDocument)), manager);

        var keyInfo = signature.SelectSingleNode("ds:KeyInfo", manager) as XmlElement
            ?? throw new CryptographicException("The XML signature KeyInfo is missing.");
        SetDigest(references[1], SHA256.HashData(Canonicalize(keyInfo)), manager);

        var signedProperties = signature.SelectSingleNode(
            "ds:Object/xades:QualifyingProperties/xades:SignedProperties", manager) as XmlElement
            ?? throw new CryptographicException("The XAdES SignedProperties element is missing.");
        SetDigest(references[2], SHA256.HashData(Canonicalize(signedProperties)), manager);

        var signedInfo = signature.SelectSingleNode("ds:SignedInfo", manager) as XmlElement
            ?? throw new CryptographicException("The XML SignedInfo element is missing.");
        var signedInfoHash = SHA256.HashData(Canonicalize(signedInfo));
        var signatureValue = signature.SelectSingleNode("ds:SignatureValue", manager) as XmlElement
            ?? throw new CryptographicException("The XML SignatureValue element is missing.");
        signatureValue.InnerText = Convert.ToBase64String(
            rsa.SignHash(signedInfoHash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static void SetDigest(
        XmlElement reference,
        byte[] digest,
        XmlNamespaceManager manager)
    {
        var digestValue = reference.SelectSingleNode("ds:DigestValue", manager) as XmlElement
            ?? throw new CryptographicException("The XML reference digest element is missing.");
        digestValue.InnerText = Convert.ToBase64String(digest);
    }

    private static byte[] Canonicalize(XmlNode node)
    {
        var clone = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        clone.LoadXml(node.OuterXml);
        if (node is not XmlDocument)
            AddInheritedNamespaces(node, clone.DocumentElement!);
        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(clone);
        using var canonical = (Stream)transform.GetOutput(typeof(Stream));
        using var output = new MemoryStream();
        canonical.CopyTo(output);
        return output.ToArray();
    }

    private static void AddInheritedNamespaces(XmlNode source, XmlElement target)
    {
        for (var current = source; current is XmlElement element; current = current.ParentNode)
        {
            foreach (var attribute in element.Attributes.OfType<XmlAttribute>()
                         .Where(attribute => attribute.Prefix == "xmlns" || attribute.Name == "xmlns"))
            {
                if (target.HasAttribute(attribute.Name)) continue;
                var inherited = target.OwnerDocument!.CreateAttribute(
                    attribute.Prefix,
                    attribute.LocalName,
                    attribute.NamespaceURI);
                inherited.Value = attribute.Value;
                target.Attributes.Append(inherited);
            }
        }
    }

    private static XmlElement BuildCertificate(XmlDocument document, X509Certificate2 certificate)
    {
        var cert = document.CreateElement("xades", "Cert", DianUblNamespaces.Xades.NamespaceName);
        var digest = document.CreateElement("xades", "CertDigest", DianUblNamespaces.Xades.NamespaceName);
        var method = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
        method.SetAttribute("Algorithm", Sha256Url);
        digest.AppendChild(method);
        AddText(document, digest, "ds", "DigestValue", SignedXml.XmlDsigNamespaceUrl,
            Convert.ToBase64String(SHA256.HashData(certificate.RawData)));
        cert.AppendChild(digest);
        var serial = document.CreateElement("xades", "IssuerSerial", DianUblNamespaces.Xades.NamespaceName);
        AddText(
            document,
            serial,
            "ds",
            "X509IssuerName",
            SignedXml.XmlDsigNamespaceUrl,
            certificate.IssuerName.Name ?? certificate.Issuer);
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
        AddText(document, sigPolicyId, "xades", "Description", DianUblNamespaces.Xades.NamespaceName, string.Empty);
        policyId.AppendChild(sigPolicyId);
        var policyHash = document.CreateElement("xades", "SigPolicyHash", DianUblNamespaces.Xades.NamespaceName);
        var method = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
        method.SetAttribute("Algorithm", Sha256Url);
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
