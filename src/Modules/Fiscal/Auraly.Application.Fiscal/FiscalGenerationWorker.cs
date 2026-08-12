using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
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
    Guid DocumentId, Guid BusinessId, string WorkerId, string FiscalDocumentType,
    string FiscalNumber, PosSaleUploadRequest? Sale,
    SalesReturnCreditNoteSnapshot? CreditNote,
    FiscalIssuerWorkConfiguration Issuer, FiscalAuthorizationWorkConfiguration? Authorization);

public sealed record FiscalGeneratedArtifacts(
    byte[] UnsignedXml, string UnsignedSha256Hex, byte[] SignedXml, string SignedSha256Hex,
    string UniqueCode, string QrPayload,
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
    DianCreditNoteUblBuilder creditNoteBuilder,
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
            var generated = await BuildAsync(work, cancellationToken);
            var unsigned = generated.Document;
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
                generated.UniqueCode, generated.QrPayload,
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

    private async Task<DianInvoice> MapInvoiceAsync(FiscalGenerationWorkItem work,
        CancellationToken cancellationToken)
    {
        var sale = work.Sale
            ?? throw new FiscalSnapshotDataException("The invoice fiscal payload is missing.");
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
        if (work.Authorization is null ||
            ubl.Authorization.Number != sale.FiscalSnapshot.AuthorizationNumber ||
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

    private async Task<FiscalUblBuildResult> BuildAsync(
        FiscalGenerationWorkItem work,
        CancellationToken cancellationToken)
    {
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.Invoice)
        {
            var invoice = await MapInvoiceAsync(work, cancellationToken);
            return new FiscalUblBuildResult(
                builder.Build(invoice), invoice.Cufe, invoice.QrPayload);
        }
        if (work.FiscalDocumentType != FiscalDocumentTypeCodes.CreditNote)
            throw new FiscalSnapshotDataException(
                $"Fiscal document type '{work.FiscalDocumentType}' is unsupported.");

        var snapshot = work.CreditNote
            ?? throw new FiscalSnapshotDataException("The credit-note fiscal payload is missing.");
        if (snapshot.FiscalIssuerConfigurationId != work.Issuer.Id)
            throw new FiscalSnapshotDataException(
                "The credit-note snapshot references another issuer configuration.");
        if (snapshot.Return.ReturnId != work.DocumentId ||
            snapshot.Return.BusinessId != work.BusinessId ||
            snapshot.FiscalNumber != work.FiscalNumber)
            throw new FiscalSnapshotDataException(
                "The credit-note snapshot differs from its durable fiscal root.");
        if (snapshot.Environment != work.Issuer.Environment)
            throw new FiscalSnapshotDataException(
                "The credit-note environment differs from its issuer configuration.");
        if (snapshot.Lines.Count != snapshot.Return.Lines.Count)
            throw new FiscalSnapshotDataException(
                "Credit-note line metadata does not match the immutable return.");

        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var metadata = snapshot.Lines.ToDictionary(line => line.LineNumber);
        var lines = snapshot.Return.Lines.OrderBy(line => line.LineNumber).Select(line =>
        {
            if (!metadata.TryGetValue(line.LineNumber, out var item))
                throw new FiscalSnapshotDataException(
                    $"Credit-note metadata is missing for line {line.LineNumber}.");
            return new DianCreditNoteLine(
                line.LineNumber, item.ProductCode, item.ProductCodeScheme,
                line.Description, item.UnitCode, line.Quantity, line.UnitPrice,
                line.DiscountAmount, line.UntaxedAmount,
                [new DianTax(line.TaxCode, item.TaxName, line.UntaxedAmount,
                    line.TaxAmount, line.TaxRate)]);
        }).ToArray();
        var taxes = lines.SelectMany(line => line.Taxes)
            .GroupBy(tax => new { tax.Code, tax.Name, tax.Percent })
            .OrderBy(group => group.Key.Code, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Percent)
            .Select(group => new DianTax(group.Key.Code, group.Key.Name,
                group.Sum(tax => tax.TaxableAmount), group.Sum(tax => tax.Amount),
                group.Key.Percent)).ToArray();
        var cude = CudeCalculator.Calculate(new CudeInput(
            snapshot.FiscalNumber, snapshot.Return.ReturnedAt,
            snapshot.Return.UntaxedAmount, snapshot.Return.TotalAmount,
            work.Issuer.SupplierTaxId, snapshot.Return.CustomerIdentification,
            pin, (FiscalEnvironment)snapshot.Environment,
            taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount))),
            snapshot.QrValidationUrl);
        var note = new DianCreditNote(
            snapshot.FiscalNumber, cude.Cude, snapshot.Return.ReturnedAt,
            snapshot.CurrencyCode, DianCreditNoteCodes.ReferencesInvoiceOperation,
            snapshot.Return.CorrectionCode, snapshot.Return.ReasonDescription,
            snapshot.Environment,
            new DianSoftware(work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
                work.Issuer.SoftwareId, pin),
            IssuerParty(work.Issuer), Party(snapshot.Customer),
            new DianInvoiceReference(snapshot.OriginalInvoiceNumber,
                snapshot.OriginalInvoiceCufe, snapshot.OriginalInvoiceIssuedOn),
            lines, taxes, snapshot.Return.UntaxedAmount,
            snapshot.Return.UntaxedAmount,
            snapshot.Return.TotalAmount,
            snapshot.Return.Lines.Sum(line => line.DiscountAmount),
            snapshot.Return.TotalAmount, cude.QrPayload);
        return new FiscalUblBuildResult(
            creditNoteBuilder.Build(note), cude.Cude, cude.QrPayload);
    }

    private static DianParty IssuerParty(FiscalIssuerWorkConfiguration issuer) => new(
        issuer.SupplierTaxId, issuer.SupplierCheckDigit, issuer.IdentificationTypeCode,
        "1", issuer.LegalName, issuer.TradeName, issuer.TaxLevelCode,
        issuer.TaxSchemeId, issuer.TaxSchemeName, Address(issuer.Address), null, null);


    private static DianParty Party(PosSaleUblPartyContract value) => new(
        value.Identification, value.CheckDigit, value.IdentificationTypeCode,
        value.OrganizationTypeCode, value.RegistrationName, value.TradeName,
        value.TaxResponsibilityCode, value.TaxSchemeId, value.TaxSchemeName,
        Address(value.Address), value.Email, value.Telephone);

    private static DianAddress Address(PosSaleUblAddressContract value) => new(
        value.MunicipalityCode, value.CityName, value.DepartmentName, value.DepartmentCode,
        value.AddressLine, value.CountryCode, value.CountryName);

    private sealed class FiscalSnapshotDataException(string message) : Exception(message);
    private sealed record FiscalUblBuildResult(
        DianUblDocument Document, string UniqueCode, string QrPayload);
}
