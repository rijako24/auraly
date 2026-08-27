using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Auraly.Fiscal.Ubl;

public static class DianPayrollCodes
{
    public const int IndividualDocument = 102;
    public const string Sha384 = "CUNE-SHA384";
    public const string XsdVersion = "1.0.6";
    public const string TechnicalVersion = "V1.0: Documento Soporte de Pago de Nómina Electrónica";
}

public sealed record DianPayrollConcept(
    string Description,
    string NatureCode,
    string? DianConceptCode,
    decimal Amount,
    bool IsSalary,
    decimal? Rate = null,
    decimal? BaseAmount = null);

public sealed record DianPayroll(
    string Prefix,
    long Consecutive,
    DateTimeOffset GeneratedAt,
    int Environment,
    int PayrollPeriodCode,
    DateOnly EmploymentStart,
    DateOnly? EmploymentEnd,
    DateOnly SettlementStart,
    DateOnly SettlementEnd,
    int WorkedTime,
    int WorkedDays,
    IReadOnlyList<DateOnly> PaymentDates,
    string EmployerTaxId,
    string EmployerCheckDigit,
    string EmployerLegalName,
    string EmployerCountryCode,
    string EmployerDepartmentCode,
    string EmployerCityCode,
    string EmployerAddress,
    string SoftwareId,
    string SoftwarePin,
    string EmployeeCode,
    string EmployeeIdentificationTypeCode,
    string EmployeeIdentification,
    string EmployeeFirstName,
    string EmployeeOtherNames,
    string EmployeeFirstSurname,
    string EmployeeSecondSurname,
    string WorkerTypeCode,
    string WorkerSubtypeCode,
    bool HighRiskPension,
    bool IntegralSalary,
    string ContractTypeCode,
    decimal MonthlySalary,
    string PaymentMethodCode,
    string? Bank,
    string? BankAccountType,
    string? BankAccountNumber,
    decimal EarningsTotal,
    decimal DeductionsTotal,
    decimal NetTotal,
    IReadOnlyList<DianPayrollConcept> Concepts,
    string QrValidationUrl);

public sealed record DianPayrollBuildResult(
    DianUblDocument Document,
    string Cune,
    string SoftwareSecurityCode,
    string QrPayload);

public static class PayrollCuneCalculator
{
    public static string Calculate(DianPayroll payroll)
    {
        ArgumentNullException.ThrowIfNull(payroll);
        var source = string.Concat(
            payroll.Prefix, payroll.Consecutive.ToString(CultureInfo.InvariantCulture),
            payroll.GeneratedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            payroll.GeneratedAt.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture),
            Money(payroll.EarningsTotal), Money(payroll.DeductionsTotal), Money(payroll.NetTotal),
            Digits(payroll.EmployerTaxId), Digits(payroll.EmployeeIdentification),
            DianPayrollCodes.IndividualDocument.ToString(CultureInfo.InvariantCulture),
            payroll.SoftwarePin,
            payroll.Environment.ToString(CultureInfo.InvariantCulture));
        return Hex(SHA384.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public static string SoftwareSecurityCode(DianPayroll payroll)
    {
        var number = payroll.Prefix + payroll.Consecutive.ToString(CultureInfo.InvariantCulture);
        return Hex(SHA384.HashData(Encoding.UTF8.GetBytes(
            payroll.SoftwareId + payroll.SoftwarePin + number)));
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
}

public sealed class DianPayrollXmlBuilder
{
    private static readonly XNamespace Payroll = "dian:gov:co:facturaelectronica:NominaIndividual";
    private static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public DianPayrollBuildResult Build(DianPayroll payroll)
    {
        ArgumentNullException.ThrowIfNull(payroll);
        Validate(payroll);
        var number = payroll.Prefix + payroll.Consecutive.ToString(CultureInfo.InvariantCulture);
        var cune = PayrollCuneCalculator.Calculate(payroll);
        var security = PayrollCuneCalculator.SoftwareSecurityCode(payroll);
        var basic = Sum(payroll, "BasicSalary");
        var transport = Sum(payroll, "TransportAllowance");
        var health = Sum(payroll, "HealthDeduction");
        var pension = Sum(payroll, "PensionDeduction");
        var otherEarnings = payroll.Concepts.Where(item =>
            item.NatureCode == "Earning" && item.DianConceptCode is not ("BasicSalary" or "TransportAllowance"))
            .ToArray();
        var otherDeductions = payroll.Concepts.Where(item =>
            item.NatureCode == "Deduction" && item.DianConceptCode is not ("HealthDeduction" or "PensionDeduction" or "LaborWithholding"))
            .ToArray();
        var withholding = Sum(payroll, "LaborWithholding");
        var qr = string.Join("\n",
            $"NumNIE: {number}",
            $"FecNIE: {Date(payroll.GeneratedAt)}",
            $"HorNIE: {Time(payroll.GeneratedAt)}",
            $"NitNIE: {Digits(payroll.EmployerTaxId)}",
            $"DocEmp: {Digits(payroll.EmployeeIdentification)}",
            $"ValDev: {Money(payroll.EarningsTotal)}",
            $"ValDed: {Money(payroll.DeductionsTotal)}",
            $"ValTolNIE: {Money(payroll.NetTotal)}",
            $"CUNE: {cune}",
            QrUrl(payroll.QrValidationUrl, cune));

        var root = new XElement(Payroll + "NominaIndividual",
            new XAttribute(XNamespace.Xmlns + "ext", Ext),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute("SchemaLocation", "NominaIndividualElectronicaXSDV1.0.6.xsd"),
            new XElement(Payroll + "Periodo",
                A("FechaIngreso", Date(payroll.EmploymentStart)),
                payroll.EmploymentEnd is null ? null : A("FechaRetiro", Date(payroll.EmploymentEnd.Value)),
                A("FechaLiquidacionInicio", Date(payroll.SettlementStart)),
                A("FechaLiquidacionFin", Date(payroll.SettlementEnd)),
                A("TiempoLaborado", payroll.WorkedTime),
                A("FechaGen", Date(payroll.GeneratedAt))),
            new XElement(Payroll + "NumeroSecuenciaXML",
                A("CodigoTrabajador", payroll.EmployeeCode), A("Prefijo", payroll.Prefix),
                A("Consecutivo", payroll.Consecutive), A("Numero", number)),
            new XElement(Payroll + "LugarGeneracionXML",
                A("Pais", payroll.EmployerCountryCode), A("DepartamentoEstado", payroll.EmployerDepartmentCode),
                A("MunicipioCiudad", payroll.EmployerCityCode), A("Idioma", "es")),
            new XElement(Payroll + "ProveedorXML",
                A("RazonSocial", payroll.EmployerLegalName), A("NIT", Digits(payroll.EmployerTaxId)),
                A("DV", Digits(payroll.EmployerCheckDigit)), A("SoftwareID", payroll.SoftwareId),
                A("SoftwareSC", security)),
            new XElement(Payroll + "CodigoQR", qr),
            new XElement(Payroll + "InformacionGeneral",
                A("Version", DianPayrollCodes.TechnicalVersion), A("Ambiente", payroll.Environment),
                A("TipoXML", DianPayrollCodes.IndividualDocument), A("CUNE", cune),
                A("EncripCUNE", DianPayrollCodes.Sha384), A("FechaGen", Date(payroll.GeneratedAt)),
                A("HoraGen", Time(payroll.GeneratedAt)), A("PeriodoNomina", payroll.PayrollPeriodCode),
                A("TipoMoneda", "COP")),
            new XElement(Payroll + "Empleador",
                A("RazonSocial", payroll.EmployerLegalName), A("NIT", Digits(payroll.EmployerTaxId)),
                A("DV", Digits(payroll.EmployerCheckDigit)), A("Pais", payroll.EmployerCountryCode),
                A("DepartamentoEstado", payroll.EmployerDepartmentCode), A("MunicipioCiudad", payroll.EmployerCityCode),
                A("Direccion", payroll.EmployerAddress)),
            new XElement(Payroll + "Trabajador",
                A("TipoTrabajador", Digits(payroll.WorkerTypeCode)),
                A("SubTipoTrabajador", Digits(payroll.WorkerSubtypeCode)),
                A("AltoRiesgoPension", payroll.HighRiskPension),
                A("TipoDocumento", Digits(payroll.EmployeeIdentificationTypeCode)),
                A("NumeroDocumento", Digits(payroll.EmployeeIdentification)),
                A("PrimerApellido", payroll.EmployeeFirstSurname),
                A("SegundoApellido", payroll.EmployeeSecondSurname),
                A("PrimerNombre", payroll.EmployeeFirstName),
                string.IsNullOrWhiteSpace(payroll.EmployeeOtherNames) ? null : A("OtrosNombres", payroll.EmployeeOtherNames),
                A("LugarTrabajoPais", payroll.EmployerCountryCode),
                A("LugarTrabajoDepartamentoEstado", payroll.EmployerDepartmentCode),
                A("LugarTrabajoMunicipioCiudad", payroll.EmployerCityCode),
                A("LugarTrabajoDireccion", payroll.EmployerAddress),
                A("SalarioIntegral", payroll.IntegralSalary),
                A("TipoContrato", Digits(payroll.ContractTypeCode)),
                A("Sueldo", Money(payroll.MonthlySalary)), A("CodigoTrabajador", payroll.EmployeeCode)),
            new XElement(Payroll + "Pago",
                A("Forma", 1), A("Metodo", payroll.PaymentMethodCode),
                OptionalAttribute("Banco", payroll.Bank), OptionalAttribute("TipoCuenta", payroll.BankAccountType),
                OptionalAttribute("NumeroCuenta", payroll.BankAccountNumber)),
            new XElement(Payroll + "FechasPagos",
                payroll.PaymentDates.Select(value => new XElement(Payroll + "FechaPago", Date(value)))),
            new XElement(Payroll + "Devengados",
                new XElement(Payroll + "Basico", A("DiasTrabajados", payroll.WorkedDays), A("SueldoTrabajado", Money(basic))),
                transport == 0 ? null : new XElement(Payroll + "Transporte",
                    A("AuxilioTransporte", Money(transport))),
                otherEarnings.Length == 0 ? null : new XElement(Payroll + "OtrosConceptos",
                    otherEarnings.Select(item => new XElement(Payroll + "OtroConcepto",
                        A("DescripcionConcepto", item.Description),
                        item.IsSalary ? A("ConceptoS", Money(item.Amount)) : A("ConceptoNS", Money(item.Amount)))))),
            new XElement(Payroll + "Deducciones",
                new XElement(Payroll + "Salud", A("Porcentaje", Percent("HealthDeduction", payroll)), A("Deduccion", Money(health))),
                new XElement(Payroll + "FondoPension", A("Porcentaje", Percent("PensionDeduction", payroll)), A("Deduccion", Money(pension))),
                otherDeductions.Length == 0 ? null : new XElement(Payroll + "OtrasDeducciones",
                    otherDeductions.Select(item => new XElement(Payroll + "OtraDeduccion", Money(item.Amount)))),
                withholding == 0 ? null : new XElement(Payroll + "RetencionFuente", Money(withholding))),
            new XElement(Payroll + "DevengadosTotal", Money(payroll.EarningsTotal)),
            new XElement(Payroll + "DeduccionesTotal", Money(payroll.DeductionsTotal)),
            new XElement(Payroll + "ComprobanteTotal", Money(payroll.NetTotal)));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "no"), root);
        var xml = Serialize(document);
        return new(new DianUblDocument(xml,
            Convert.ToHexString(SHA256.HashData(xml)).ToLowerInvariant()), cune, security, qr);
    }

    private static void Validate(DianPayroll value)
    {
        if (value.Consecutive <= 0 || value.Environment is not (1 or 2) || value.PayrollPeriodCode <= 0 ||
            value.WorkedDays is < 0 or > 30 || value.WorkedTime < 0 || value.MonthlySalary <= 0 ||
            value.PaymentDates.Count == 0 || value.EarningsTotal < 0 || value.DeductionsTotal < 0 ||
            value.NetTotal != value.EarningsTotal - value.DeductionsTotal ||
            value.Concepts.Any(item => item.Amount < 0) ||
            Sum(value, "BasicSalary") <= 0)
            throw new ArgumentException("The electronic payroll values are incomplete or inconsistent.");
        foreach (var required in new[] { value.Prefix, value.EmployerTaxId, value.EmployerCheckDigit,
                     value.EmployerLegalName, value.SoftwareId, value.SoftwarePin, value.EmployeeCode,
                     value.EmployeeIdentificationTypeCode, value.EmployeeIdentification,
                     value.EmployeeFirstName, value.EmployeeFirstSurname, value.WorkerTypeCode,
                     value.WorkerSubtypeCode, value.ContractTypeCode, value.PaymentMethodCode })
            if (string.IsNullOrWhiteSpace(required))
                throw new ArgumentException("The electronic payroll identity and DIAN catalog values are required.");
        foreach (var identifier in new[] { value.EmployerTaxId, value.EmployerCheckDigit,
                     value.EmployeeIdentification, value.EmployeeIdentificationTypeCode,
                     value.WorkerTypeCode, value.WorkerSubtypeCode, value.ContractTypeCode,
                     value.PaymentMethodCode })
            if (identifier.Any(char.IsLetter) || !identifier.Any(char.IsDigit))
                throw new ArgumentException(
                    "Electronic payroll identifiers and DIAN catalog values must be numeric.");
    }

    private static decimal Sum(DianPayroll value, string code) => value.Concepts
        .Where(item => item.DianConceptCode == code).Sum(item => item.Amount);
    private static string Percent(string code, DianPayroll value)
    {
        var concepts = value.Concepts.Where(item => item.DianConceptCode == code).ToArray();
        var rate = concepts.Where(item => item.Rate is not null).Select(item => item.Rate!.Value)
            .DefaultIfEmpty(0).Max();
        if (rate == 0)
        {
            var deduction = concepts.Sum(item => item.Amount);
            var baseAmount = concepts.Where(item => item.BaseAmount is not null)
                .Select(item => item.BaseAmount!.Value).DefaultIfEmpty(0).Max();
            rate = baseAmount == 0 ? 0 : deduction / baseAmount;
        }
        return (rate * 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }
    private static XAttribute A(string name, object value) => new(name, value);
    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : A(name, value.Trim());
    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Date(DateTimeOffset value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Time(DateTimeOffset value) => value.ToString("HH:mm:sszzz", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string QrUrl(string baseUrl, string cune)
    {
        var value = baseUrl.Trim();
        return value.EndsWith('=') || value.EndsWith('/')
            ? value + cune
            : value + "/" + cune;
    }
    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = false, OmitXmlDeclaration = false
        })) document.Save(writer);
        return stream.ToArray();
    }
}
