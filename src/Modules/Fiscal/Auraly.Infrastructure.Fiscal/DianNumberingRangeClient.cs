using System.Globalization;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Infrastructure.Fiscal;

public sealed class DianNumberingRangeClient(IDianWcfClientFactory clients)
    : IDianNumberingRangeClient
{
    private static readonly Uri ProductionEndpoint = new(
        "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc");

    public async Task<IReadOnlyList<ImportedDianNumberingRange>> GetAsync(
        DianNumberingRangeContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var configuration = new DianHabilitationConfiguration(
            ProductionEndpoint,
            context.Certificate,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60),
            10 * 1024 * 1024);
        await using var client = await clients.CreateAsync(configuration, cancellationToken);
        var response = await client.GetNumberingRangeAsync(
            context.SupplierTaxId,
            context.SoftwareOwnerTaxId,
            context.SoftwareIdentificationCode,
            cancellationToken);
        if (!string.Equals(response.OperationCode, "100", StringComparison.Ordinal))
            throw new FiscalConfigurationValidationException(
                response.OperationDescription ?? "La DIAN rechazó la consulta de resoluciones.");

        return response.ResponseList.Select(Map).ToArray();
    }

    private static ImportedDianNumberingRange Map(DianNumberRangeResponse value)
    {
        if (string.IsNullOrWhiteSpace(value.ResolutionNumber) ||
            string.IsNullOrWhiteSpace(value.Prefix) ||
            string.IsNullOrWhiteSpace(value.TechnicalKey) ||
            value.FromNumber < 1 || value.ToNumber < value.FromNumber)
            throw new FiscalConfigurationValidationException(
                "La DIAN devolvió una resolución incompleta o inválida.");
        return new ImportedDianNumberingRange(
            value.ResolutionNumber.Trim(),
            ParseOptional(value.ResolutionDate),
            value.Prefix.Trim(),
            value.FromNumber,
            value.ToNumber,
            ParseRequired(value.ValidDateFrom, "fecha inicial"),
            ParseRequired(value.ValidDateTo, "fecha final"),
            value.TechnicalKey.Trim());
    }

    private static DateOnly? ParseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseRequired(value, "fecha de resolución");

    private static DateOnly ParseRequired(string? value, string label)
    {
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "yyyyMMdd", "M/d/yyyy h:mm:ss tt" };
        if (DateOnly.TryParseExact(value?.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
            DateOnly.TryParse(value, CultureInfo.GetCultureInfo("es-CO"),
                DateTimeStyles.AllowWhiteSpaces, out parsed))
            return parsed;
        throw new FiscalConfigurationValidationException(
            $"La DIAN devolvió una {label} inválida.");
    }
}
