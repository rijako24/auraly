using System.Text;
using System.Xml.Linq;
using Auraly.Fiscal.Ubl;

namespace Auraly.Foundation.Tests;

public sealed class DianPayrollXmlTests
{
    [Fact]
    public void Same_payroll_produces_identical_cune_xml_and_hash()
    {
        var builder = new DianPayrollXmlBuilder();
        var payroll = CreatePayroll();

        var first = builder.Build(payroll);
        var second = builder.Build(payroll);

        Assert.Equal(first.Cune, second.Cune);
        Assert.Equal(
            "f6ce9705bcb173a2d6271fb15c5bc01f3cd07cc2ee2e66232d6152a9946581343f110fdcffe35f4461b98b99979dbfdb",
            first.Cune);
        Assert.Equal(
            "d3a44a9ee45ef106e62313546c96d0ac28647cc95f44468a7762c0ffbf9adaa5dff8d1803b55768de13006e81c610b34",
            first.SoftwareSecurityCode);
        Assert.Equal(96, first.Cune.Length);
        Assert.Equal(first.Document.Xml, second.Document.Xml);
        Assert.Equal(first.Document.Sha256Hex, second.Document.Sha256Hex);
    }

    [Fact]
    public void Generated_payroll_passes_official_dian_xsd()
    {
        var result = new DianPayrollXmlBuilder().Build(CreatePayroll());
        var validation = new DianPayrollSchemaValidator().Validate(result.Document.Xml);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
    }

    [Fact]
    public void Payroll_contains_required_totals_and_security_codes()
    {
        var result = new DianPayrollXmlBuilder().Build(CreatePayroll());
        var document = XDocument.Parse(Encoding.UTF8.GetString(result.Document.Xml));
        XNamespace payroll = "dian:gov:co:facturaelectronica:NominaIndividual";

        Assert.Equal(result.Cune,
            document.Root?.Element(payroll + "InformacionGeneral")?.Attribute("CUNE")?.Value);
        Assert.Equal(result.SoftwareSecurityCode,
            document.Root?.Element(payroll + "ProveedorXML")?.Attribute("SoftwareSC")?.Value);
        Assert.Equal("1300000.00", document.Root?.Element(payroll + "DevengadosTotal")?.Value);
        Assert.Equal("184000.00", document.Root?.Element(payroll + "DeduccionesTotal")?.Value);
        Assert.Equal("1116000.00", document.Root?.Element(payroll + "ComprobanteTotal")?.Value);
    }

    [Fact]
    public void Payroll_rejects_identifiers_that_would_change_when_normalized()
    {
        var payroll = CreatePayroll() with { EmployeeIdentification = "CC-1012345678" };

        Assert.Throws<ArgumentException>(() => new DianPayrollXmlBuilder().Build(payroll));
    }

    private static DianPayroll CreatePayroll() => new(
        "N", 1,
        new DateTimeOffset(2026, 8, 5, 10, 53, 10, TimeSpan.FromHours(-5)),
        2, 5, new DateOnly(2024, 1, 10), null,
        new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 922, 30,
        [new DateOnly(2026, 7, 31)],
        "900123456", "7", "Auraly Comercio SAS", "CO", "11", "11001",
        "Carrera 8 # 6C-38", "56f2ae4e-9812-4fad-9255-08fcfcd5ccb0", "20191",
        "EMP-001", "13", "1012345678", "Ana", "María", "Pérez", "",
        "01", "00", false, false, "2", 1_300_000m, "42",
        null, null, null, 1_300_000m, 184_000m, 1_116_000m,
        [
            new("Salario básico", "Earning", "BasicSalary", 1_300_000m, true),
            new("Salud", "Deduction", "HealthDeduction", 52_000m, false),
            new("Pensión", "Deduction", "PensionDeduction", 52_000m, false),
            new("Libranza", "Deduction", "OtherDeduction", 80_000m, false)
        ],
        "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=");
}
