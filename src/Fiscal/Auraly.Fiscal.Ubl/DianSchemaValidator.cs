using System.Net;
using System.Xml;
using System.Xml.Schema;

namespace Auraly.Fiscal.Ubl;

public sealed record DianSchemaValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class DianSchemaValidator
{
    private readonly XmlSchemaSet schemas;

    public DianSchemaValidator(string? schemaRoot = null)
    {
        var root = Path.GetFullPath(schemaRoot ?? Path.Combine(AppContext.BaseDirectory, "Schemas"));
        var main = Path.Combine(root, "maindoc");
        var resolver = new LocalSchemaResolver(root);
        schemas = new XmlSchemaSet { XmlResolver = resolver };
        AddSchema(schemas, "http://www.w3.org/2000/09/xmldsig#", Path.Combine(root, "common", "UBL-xmldsig-core-schema-2.1.xsd"), resolver);
        AddSchema(schemas, "http://uri.etsi.org/01903/v1.3.2#", Path.Combine(root, "common", "UBL-XAdESv132-2.1.xsd"), resolver);
        AddSchema(schemas, "http://uri.etsi.org/01903/v1.4.1#", Path.Combine(root, "common", "UBL-XAdESv141-2.1.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "DIAN_UBL_Structures.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "UBL-Invoice-2.1.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "UBL-CreditNote-2.1.xsd"), resolver);
        AddSchema(schemas, null, Path.Combine(main, "UBL-DebitNote-2.1.xsd"), resolver);
        schemas.Compile();
    }

    private static void AddSchema(XmlSchemaSet set, string? targetNamespace, string path, XmlResolver resolver)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = resolver
        });
        set.Add(targetNamespace, reader);
    }
    public DianSchemaValidationResult Validate(ReadOnlyMemory<byte> xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Schemas = schemas,
            ValidationType = ValidationType.Schema
        };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, args) => errors.Add($"{args.Severity}: {args.Message}");
        using var stream = new MemoryStream(xml.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read()) { }
        return new DianSchemaValidationResult(errors.Count == 0, errors);
    }

    private sealed class LocalSchemaResolver : XmlResolver
    {
        private readonly string root;
        public LocalSchemaResolver(string root) => this.root = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!absoluteUri.IsFile) throw new XmlException("Only local official schemas may be resolved.");
            var path = Path.GetFullPath(absoluteUri.LocalPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new XmlException("Schema resolution escaped the official schema directory.");
            return File.OpenRead(path);
        }
    }
}
