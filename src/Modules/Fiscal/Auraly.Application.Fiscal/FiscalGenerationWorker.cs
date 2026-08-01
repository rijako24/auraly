using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;

namespace Auraly.Application.Fiscal;

public sealed record FiscalIssuerWorkConfiguration(
    Guid Id, Guid BusinessId, string SupplierTaxId, string SupplierCheckDigit,
    string LegalName, string TradeName, string TaxLevelCode, string TaxSchemeId,
    string TaxSchemeName, string IdentificationTypeCode, PosSaleUblAddressContract Address,
    string SoftwareId, string SoftwarePinSecretReference, int Environment,
    string CertificateProvider, string CertificateKeyReference, string CertificateThumbprint,
    string TechnicalAnnexVersion, string GeneratorVersion);

public sealed record FiscalAuthorizationWorkConfiguration(
    string Number, DateOnly ValidFrom, DateOnly ValidUntil, string Prefix,
    long RangeStart, long RangeEnd);

public sealed record FiscalGenerationWorkItem(
    Guid DocumentId, Guid BusinessId, string WorkerId, PosSaleUploadRequest Sale,
    FiscalIssuerWorkConfiguration Issuer, FiscalAuthorizationWorkConfiguration Authorization);

public sealed record FiscalGeneratedArtifacts(
    byte[] UnsignedXml, string UnsignedSha256Hex, byte[] SignedXml, string SignedSha256Hex,
    string CertificateThumbprint, DateTimeOffset GeneratedAt, DateTimeOffset SignedAt,
    string TechnicalAnnexVersion, string GeneratorVersion);

public interface IFiscalGenerationWorkStore
{
    Task<FiscalGenerationWorkItem?> AcquireAsync(
        Guid businessId, Guid documentId, string workerId,
        DateTimeOffset acquiredAt, TimeSpan lease, CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetResumeAtAsync(
        Guid businessId, Guid documentId, DateTimeOffset checkedAt,
        TimeSpan lease, CancellationToken cancellationToken);
    Task CompleteAsync(FiscalGenerationWorkItem work, FiscalGeneratedArtifacts artifacts,
        CancellationToken cancellationToken);
    Task FailAsync(FiscalGenerationWorkItem work, string status, string errorCode,
        string errorMessage, DateTimeOffset failedAt, CancellationToken cancellationToken);
}

public interface IFiscalSoftwarePinProvider
{
    Task<string> ResolveAsync(Guid businessId, string secretReference,
        CancellationToken cancellationToken);
}

public sealed class FiscalGenerationWorker(
    IFiscalGenerationWorkStore store,
    IFiscalSoftwarePinProvider pins,
    DianInvoiceUblBuilder builder,
    DianSchemaValidator validator,
    IFiscalXmlSigner signer,
    TimeProvider timeProvider)
{
    public async Task<bool> ProcessAsync(
        Guid businessId,
        Guid documentId,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty)
            throw new ArgumentException("Business and document identifiers are required.");
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("A worker identity is required.", nameof(workerId));
        var work = await store.AcquireAsync(
            businessId, documentId, workerId.Trim(), timeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(2), cancellationToken);
        if (work is null) return false;
        try
        {
            var invoice = await MapAsync(work, cancellationToken);
            var unsigned = builder.Build(invoice);
            var validation = validator.Validate(unsigned.Xml);
            if (!validation.IsValid)
            {
                await FailAsync(work, FiscalDocumentStatusCodes.SchemaValidationFailed,
                    "OfficialXsdValidationFailed", string.Join(" | ", validation.Errors.Take(10)),
                    cancellationToken);
                return false;
            }

            var generatedAt = timeProvider.GetUtcNow();
            var signed = await signer.SignAsync(new FiscalSigningRequest(
                work.BusinessId, work.Issuer.SupplierTaxId, unsigned.Xml,
                new FiscalCertificateReference(work.BusinessId, work.Issuer.CertificateProvider,
                    work.Issuer.CertificateKeyReference, work.Issuer.CertificateThumbprint),
                generatedAt), cancellationToken);
            await store.CompleteAsync(work, new FiscalGeneratedArtifacts(
                unsigned.Xml, unsigned.Sha256Hex, signed.SignedXml, signed.Sha256Hex,
                signed.CertificateThumbprint, generatedAt, signed.SignedAt,
                work.Issuer.TechnicalAnnexVersion, work.Issuer.GeneratorVersion), cancellationToken);
            return true;
        }
        catch (FiscalSnapshotDataException exception)
        {
            await FailAsync(work, FiscalDocumentStatusCodes.MissingMandatoryFiscalData,
                "MissingMandatoryFiscalData", exception.Message, cancellationToken);
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            await FailAsync(work, FiscalDocumentStatusCodes.SignatureFailed,
                "FiscalSignatureFailed", exception.Message, cancellationToken);
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await FailAsync(work, FiscalDocumentStatusCodes.PermanentFailure,
                exception.GetType().Name, exception.Message, cancellationToken);
            return false;
        }
    }

    private Task FailAsync(FiscalGenerationWorkItem work, string status, string code,
        string message, CancellationToken cancellationToken) =>
        store.FailAsync(work, status, code, message, timeProvider.GetUtcNow(), cancellationToken);

    private async Task<DianInvoice> MapAsync(FiscalGenerationWorkItem work,
        CancellationToken cancellationToken)
    {
        var sale = work.Sale;
        var ubl = sale.UblSnapshot
            ?? throw new FiscalSnapshotDataException("The immutable sale has no UBL snapshot.");
        if (ubl.FiscalIssuerConfigurationId != work.Issuer.Id)
            throw new FiscalSnapshotDataException("The UBL snapshot references another issuer configuration.");
        if (ubl.Customer.Identification != sale.FiscalSnapshot.CustomerIdentification ||
            ubl.Supplier.Identification != sale.FiscalSnapshot.SupplierTaxId ||
            ubl.Supplier.Identification != work.Issuer.SupplierTaxId)
            throw new FiscalSnapshotDataException("Supplier or customer identification differs from the verified fiscal snapshot.");
        if (ubl.Lines.Count != sale.Lines.Count)
            throw new FiscalSnapshotDataException("UBL line metadata does not match the immutable sale lines.");
        if (work.Issuer.Environment != sale.FiscalSnapshot.Environment)
            throw new FiscalSnapshotDataException("Issuer environment differs from the verified fiscal snapshot.");
        if (ubl.Authorization.Number != sale.FiscalSnapshot.AuthorizationNumber ||
            ubl.Authorization.Prefix != sale.FiscalSnapshot.Prefix ||
            work.Authorization.Number != ubl.Authorization.Number ||
            work.Authorization.Prefix != ubl.Authorization.Prefix ||
            work.Authorization.ValidFrom != ubl.Authorization.ValidFrom ||
            work.Authorization.ValidUntil != ubl.Authorization.ValidUntil ||
            work.Authorization.RangeStart != ubl.Authorization.RangeStart ||
            work.Authorization.RangeEnd != ubl.Authorization.RangeEnd)
            throw new FiscalSnapshotDataException("Authorization data differs from the immutable fiscal snapshot.");
        if (ubl.SoftwareIdentificationCode != work.Issuer.SoftwareId)
            throw new FiscalSnapshotDataException("Software identification differs from the issuer configuration version.");

        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");

        var metadata = ubl.Lines.ToDictionary(line => line.LineNumber);
        var lines = sale.Lines.OrderBy(line => line.LineNumber).Select(line =>
        {
            if (!metadata.TryGetValue(line.LineNumber, out var item))
                throw new FiscalSnapshotDataException($"UBL metadata is missing for line {line.LineNumber}.");
            if (item.TaxPercent != line.TaxRate)
                throw new FiscalSnapshotDataException($"UBL tax rate differs from immutable line {line.LineNumber}.");
            return new DianInvoiceLine(line.LineNumber, item.ProductCode, item.ProductCodeScheme,
                line.Description, item.UnitCode, line.Quantity, line.UnitPrice, line.DiscountAmount,
                line.UntaxedAmount, [new DianTax(line.TaxCode, item.TaxName,
                    line.UntaxedAmount, line.TaxAmount, item.TaxPercent)]);
        }).ToArray();
        var taxes = sale.FiscalSnapshot.Taxes.Select(tax =>
        {
            var matching = lines.SelectMany(line => line.Taxes)
                .Where(item => item.Code == tax.Code).ToArray();
            if (matching.Length == 0)
                throw new FiscalSnapshotDataException($"Tax metadata is missing for code '{tax.Code}'.");
            return new DianTax(tax.Code, matching[0].Name,
                matching.Sum(item => item.TaxableAmount), tax.Amount, matching[0].Percent);
        }).ToArray();

        return new DianInvoice(sale.FiscalSnapshot.FiscalNumber, sale.FiscalSnapshot.Cufe,
            sale.FiscalSnapshot.IssuedAt, ubl.CurrencyCode, ubl.InvoiceTypeCode,
            sale.FiscalSnapshot.Environment,
            new DianAuthorization(ubl.Authorization.Number, ubl.Authorization.ValidFrom,
                ubl.Authorization.ValidUntil, ubl.Authorization.Prefix,
                ubl.Authorization.RangeStart, ubl.Authorization.RangeEnd),
            new DianSoftware(ubl.Supplier.Identification, ubl.Supplier.CheckDigit,
                ubl.SoftwareIdentificationCode, pin),
            Party(ubl.Supplier), Party(ubl.Customer), lines, taxes,
            new DianPayment(ubl.PaymentFormCode, ubl.PaymentMeansCode, ubl.DueDate,
                ubl.PaymentReference),
            sale.FiscalSnapshot.UntaxedAmount, sale.FiscalSnapshot.UntaxedAmount,
            sale.FiscalSnapshot.PayableAmount, sale.Lines.Sum(line => line.DiscountAmount),
            sale.FiscalSnapshot.PayableAmount, sale.FiscalSnapshot.QrPayload);
    }


    private static DianParty Party(PosSaleUblPartyContract value) => new(
        value.Identification, value.CheckDigit, value.IdentificationTypeCode,
        value.OrganizationTypeCode, value.RegistrationName, value.TradeName,
        value.TaxResponsibilityCode, value.TaxSchemeId, value.TaxSchemeName,
        Address(value.Address), value.Email, value.Telephone);

    private static DianAddress Address(PosSaleUblAddressContract value) => new(
        value.MunicipalityCode, value.CityName, value.DepartmentName, value.DepartmentCode,
        value.AddressLine, value.CountryCode, value.CountryName);

    private sealed class FiscalSnapshotDataException(string message) : Exception(message);
}