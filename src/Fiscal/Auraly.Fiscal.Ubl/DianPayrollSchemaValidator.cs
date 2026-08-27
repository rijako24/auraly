using System.Net;
using System.Xml;
using System.Xml.Schema;

namespace Auraly.Fiscal.Ubl;

public sealed class DianPayrollSchemaValidator
{
    private readonly XmlSchemaSet schemas;

    public DianPayrollSchemaValidator(string? schemaRoot = null)
    {
        var root = Path.GetFullPath(schemaRoot ?? Path.Combine(AppContext.BaseDirectory, "Schemas"));
        var resolver = new PayrollSchemaResolver(root);
        schemas = new XmlSchemaSet { XmlResolver = resolver };
        AddSchema(schemas, null,
            Path.Combine(root, "payroll", "NominaIndividualElectronicaXSDV1.0.6.xsd"), resolver);
        schemas.Compile();
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

    private static void AddSchema(XmlSchemaSet set, string? targetNamespace,
        string path, XmlResolver resolver)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = resolver
        });
        set.Add(targetNamespace, reader);
    }

    private sealed class PayrollSchemaResolver(string schemaRoot) : XmlResolver
    {
        private readonly string root = schemaRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
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
