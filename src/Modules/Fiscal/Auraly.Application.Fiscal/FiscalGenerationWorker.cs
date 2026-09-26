using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.BuildingBlocks.Domain.Identity;
using Auraly.Fiscal.Core;
using Auraly.Fiscal.Ubl;

namespace Auraly.Application.Fiscal;

public sealed record FiscalIssuerWorkConfiguration(
    Guid Id, Guid BusinessId, string SupplierTaxId, string SupplierCheckDigit,
    string LegalName, string TradeName, string TaxLevelCode, string TaxSchemeId,
    string TaxSchemeName, string IdentificationTypeCode, PosSaleUblAddressContract Address,
    string SoftwareId, string SoftwarePinSecretReference, int Environment,
    string CertificateProvider, string CertificateKeyReference, string CertificateThumbprint,
    string TechnicalAnnexVersion, string GeneratorVersion,
    string? LegalProfileOrganizationType = null,
    string? LegalProfileName = null,
    string? LegalProfileEmail = null,
    string? LegalProfileTelephone = null);

public sealed record FiscalAuthorizationWorkConfiguration(
    string Number, DateOnly ValidFrom, DateOnly ValidUntil, string Prefix,
    long RangeStart, long RangeEnd);

public sealed record FiscalGenerationWorkItem(
    Guid DocumentId, Guid BusinessId, string WorkerId, string FiscalDocumentType,
    string FiscalNumber, PosSaleUploadRequest? Sale,
    SalesReturnCreditNoteSnapshot? CreditNote,
    SalesDebitNoteFiscalSnapshot? DebitNote,
    FiscalIssuerWorkConfiguration Issuer, FiscalAuthorizationWorkConfiguration? Authorization,
    PurchaseSupportFiscalSnapshot? SupportDocument = null,
    ElectronicPayrollSnapshot? ElectronicPayroll = null,
    ServiceInvoiceSnapshot? ServiceInvoice = null,
    bool IsCorrection = false,
    bool IsFiscalHabilitation = false,
    FiscalOnlyCreditNoteSnapshot? FiscalOnlyCreditNote = null);

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
    DianSupportDocumentUblBuilder supportDocumentBuilder,
    DianCreditNoteUblBuilder creditNoteBuilder,
    DianDebitNoteUblBuilder debitNoteBuilder,
    DianSchemaValidator validator,
    DianPayrollXmlBuilder payrollBuilder,
    DianPayrollSchemaValidator payrollValidator,
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
            var validation = work.FiscalDocumentType == FiscalDocumentTypeCodes.ElectronicPayroll
                ? payrollValidator.Validate(unsigned.Xml)
                : validator.Validate(unsigned.Xml);
            if (!validation.IsValid)
            {
                await FailAsync(work, FiscalDocumentStatusCodes.SchemaValidationFailed,
                    "OfficialXsdValidationFailed", string.Join(" | ", validation.Errors.Take(10)),
                    cancellationToken);
                return false;
            }

            var generatedAt = timeProvider.GetUtcNow();
            var signingTime = ResolveSigningTime(work) ?? generatedAt;
            var signed = await signer.SignAsync(new FiscalSigningRequest(
                work.BusinessId, work.Issuer.SupplierTaxId, unsigned.Xml,
                new FiscalCertificateReference(work.BusinessId, work.Issuer.CertificateProvider,
                    work.Issuer.CertificateKeyReference, work.Issuer.CertificateThumbprint),
                signingTime), cancellationToken);
            await store.CompleteAsync(work, new FiscalGeneratedArtifacts(
                unsigned.Xml, unsigned.Sha256Hex, signed.SignedXml, signed.Sha256Hex,
                generated.UniqueCode, generated.QrPayload,
                signed.CertificateThumbprint, generatedAt, signed.SignedAt,
                work.FiscalDocumentType == FiscalDocumentTypeCodes.ElectronicPayroll
                    ? DianPayrollCodes.XsdVersion
                    : work.Issuer.TechnicalAnnexVersion,
                work.Issuer.GeneratorVersion), cancellationToken);
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
        var snapshot = sale.FiscalSnapshot
            ?? throw new FiscalSnapshotDataException("The immutable sale has no fiscal snapshot.");
        var ubl = sale.UblSnapshot
            ?? throw new FiscalSnapshotDataException("The immutable sale has no UBL snapshot.");
        if (ubl.FiscalIssuerConfigurationId != work.Issuer.Id)
            throw new FiscalSnapshotDataException("The UBL snapshot references another issuer configuration.");
        if (ubl.Customer.Identification != snapshot.CustomerIdentification ||
            ubl.Supplier.Identification != snapshot.SupplierTaxId ||
            ubl.Supplier.Identification != work.Issuer.SupplierTaxId)
            throw new FiscalSnapshotDataException("Supplier or customer identification differs from the verified fiscal snapshot.");
        if (ubl.Lines.Count != sale.Lines.Count)
            throw new FiscalSnapshotDataException("UBL line metadata does not match the immutable sale lines.");
        if (work.Issuer.Environment != snapshot.Environment)
            throw new FiscalSnapshotDataException("Issuer environment differs from the verified fiscal snapshot.");
        if (work.Authorization is null ||
            ubl.Authorization.Number != snapshot.AuthorizationNumber ||
            ubl.Authorization.Prefix != snapshot.Prefix ||
            work.Authorization.Number != ubl.Authorization.Number ||
            work.Authorization.Prefix != ubl.Authorization.Prefix ||
            work.Authorization.ValidFrom != ubl.Authorization.ValidFrom ||
            work.Authorization.ValidUntil != ubl.Authorization.ValidUntil ||
            work.Authorization.RangeStart != ubl.Authorization.RangeStart ||
            work.Authorization.RangeEnd != ubl.Authorization.RangeEnd)
            throw new FiscalSnapshotDataException("Authorization data differs from the immutable fiscal snapshot.");
        if (ubl.SoftwareIdentificationCode != work.Issuer.SoftwareId)
            throw new FiscalSnapshotDataException("Software identification differs from the issuer configuration version.");
        if (snapshot.PayableRoundingAmount !=
                sale.CommercialSnapshot.PayableRoundingAmount)
            throw new FiscalSnapshotDataException(
                "The fiscal adjustment-to-peso differs from the commercial snapshot.");

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
        }).Concat((sale.Charges ?? []).Where(charge => charge.InvoicedAmount > 0)
            .Select((charge, index) => new DianInvoiceLine(sale.Lines.Count + index + 1,
                charge.Code, "999", charge.Name, "EA", 1, charge.InvoicedUntaxedAmount, 0,
                charge.InvoicedUntaxedAmount, [new DianTax(charge.TaxCode,
                    PosSaleFiscalMappings.TaxName(charge.TaxCode), charge.InvoicedUntaxedAmount,
                    charge.InvoicedTaxAmount, charge.TaxRate)]))).ToArray();
        var taxes = SummarizeTaxes(lines.SelectMany(line => line.Taxes));
        var frozenTaxes = sale.FiscalSnapshot.Taxes
            .GroupBy(tax => tax.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(tax => tax.Amount),
                StringComparer.Ordinal);
        var generatedTaxes = taxes
            .GroupBy(tax => tax.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(tax => tax.Amount),
                StringComparer.Ordinal);
        if (frozenTaxes.Count != generatedTaxes.Count ||
            frozenTaxes.Any(tax => generatedTaxes.GetValueOrDefault(tax.Key) != tax.Value))
            throw new FiscalSnapshotDataException(
                "The immutable tax summary differs from its invoice lines.");

        return new DianInvoice(sale.FiscalSnapshot.FiscalNumber, sale.FiscalSnapshot.Cufe,
            sale.FiscalSnapshot.IssuedAt, ubl.CurrencyCode, ubl.InvoiceTypeCode,
            snapshot.Environment,
            new DianAuthorization(ubl.Authorization.Number, ubl.Authorization.ValidFrom,
                ubl.Authorization.ValidUntil, ubl.Authorization.Prefix,
                ubl.Authorization.RangeStart, ubl.Authorization.RangeEnd),
            new DianSoftware(ubl.Supplier.Identification, ubl.Supplier.CheckDigit,
                ubl.SoftwareIdentificationCode, pin),
            SupplierParty(ubl.Supplier, work), Party(ubl.Customer), lines, taxes,
            new DianPayment(ubl.PaymentFormCode, ubl.PaymentMeansCode, ubl.DueDate,
                ubl.PaymentReference),
            sale.FiscalSnapshot.UntaxedAmount, sale.FiscalSnapshot.UntaxedAmount,
            sale.FiscalSnapshot.UntaxedAmount + sale.FiscalSnapshot.TaxAmount,
            sale.Lines.Sum(line => line.DiscountAmount),
            sale.FiscalSnapshot.PayableAmount, sale.FiscalSnapshot.QrPayload,
            PayableRoundingAmount: sale.FiscalSnapshot.PayableRoundingAmount);
    }

    private async Task<DianInvoice> MapServiceInvoiceAsync(
        FiscalGenerationWorkItem work,
        CancellationToken cancellationToken)
    {
        var invoice = work.ServiceInvoice
            ?? throw new FiscalSnapshotDataException("The service invoice fiscal payload is missing.");
        var snapshot = invoice.FiscalSnapshot;
        var ubl = invoice.UblSnapshot;
        if (invoice.DocumentId != work.DocumentId || invoice.BusinessId != work.BusinessId ||
            snapshot.FiscalNumber != work.FiscalNumber ||
            ubl.FiscalIssuerConfigurationId != work.Issuer.Id ||
            ubl.Customer.Identification != snapshot.CustomerIdentification ||
            ubl.Supplier.Identification != snapshot.SupplierTaxId ||
            ubl.Supplier.Identification != work.Issuer.SupplierTaxId ||
            ubl.Lines.Count != invoice.Lines.Count || work.Issuer.Environment != snapshot.Environment)
            throw new FiscalSnapshotDataException("The service invoice differs from its durable fiscal root.");
        if (work.Authorization is null)
            throw new FiscalSnapshotDataException(
                "The service invoice fiscal authorization could not be loaded.");
        if (ubl.Authorization.Number != snapshot.AuthorizationNumber ||
            work.Authorization.Number != ubl.Authorization.Number)
            throw new FiscalSnapshotDataException(
                "The service invoice authorization number is inconsistent.");
        if (ubl.Authorization.Prefix != snapshot.Prefix ||
            work.Authorization.Prefix != ubl.Authorization.Prefix)
            throw new FiscalSnapshotDataException(
                "The service invoice authorization prefix is inconsistent.");
        if (work.Authorization.ValidFrom != ubl.Authorization.ValidFrom ||
            work.Authorization.ValidUntil != ubl.Authorization.ValidUntil)
            throw new FiscalSnapshotDataException(
                "The service invoice authorization validity is inconsistent.");
        if (work.Authorization.RangeStart != ubl.Authorization.RangeStart ||
            work.Authorization.RangeEnd != ubl.Authorization.RangeEnd)
            throw new FiscalSnapshotDataException(
                "The service invoice authorization range is inconsistent.");
        if (ubl.SoftwareIdentificationCode != work.Issuer.SoftwareId)
            throw new FiscalSnapshotDataException(
                "The service invoice software identification is inconsistent.");
        if (snapshot.PayableRoundingAmount !=
                invoice.CommercialSnapshot.PayableRoundingAmount)
            throw new FiscalSnapshotDataException(
                "The service invoice adjustment-to-peso is inconsistent.");

        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var metadata = ubl.Lines.ToDictionary(line => line.LineNumber);
        var lines = invoice.Lines.OrderBy(line => line.LineNumber).Select(line =>
        {
            if (!metadata.TryGetValue(line.LineNumber, out var item) ||
                item.TaxPercent != line.TaxRate)
                throw new FiscalSnapshotDataException(
                    $"UBL metadata differs from service line {line.LineNumber}.");
            return new DianInvoiceLine(line.LineNumber, line.ServiceCode, "999",
                line.Description, line.UnitCode, line.Quantity, line.UnitPrice,
                line.DiscountAmount, line.UntaxedAmount,
                [new DianTax(line.TaxCode, line.TaxName, line.UntaxedAmount,
                    line.TaxAmount, line.TaxRate)]);
        }).ToArray();
        var taxes = SummarizeTaxes(lines.SelectMany(line => line.Taxes));

        return new DianInvoice(snapshot.FiscalNumber, snapshot.Cufe, snapshot.IssuedAt,
            ubl.CurrencyCode, ubl.InvoiceTypeCode, snapshot.Environment,
            new DianAuthorization(ubl.Authorization.Number, ubl.Authorization.ValidFrom,
                ubl.Authorization.ValidUntil, ubl.Authorization.Prefix,
                ubl.Authorization.RangeStart, ubl.Authorization.RangeEnd),
            new DianSoftware(ubl.Supplier.Identification, ubl.Supplier.CheckDigit,
                ubl.SoftwareIdentificationCode, pin), SupplierParty(ubl.Supplier, work), Party(ubl.Customer),
            lines, taxes, new DianPayment(ubl.PaymentFormCode, ubl.PaymentMeansCode,
                ubl.DueDate, ubl.PaymentReference),
            snapshot.UntaxedAmount, snapshot.UntaxedAmount,
            snapshot.UntaxedAmount + snapshot.TaxAmount,
            invoice.Lines.Sum(line => line.DiscountAmount), snapshot.PayableAmount,
            snapshot.QrPayload,
            PayableRoundingAmount: snapshot.PayableRoundingAmount);
    }

    private async Task<FiscalUblBuildResult> BuildAsync(
        FiscalGenerationWorkItem work,
        CancellationToken cancellationToken)
    {
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.Invoice)
        {
            var invoice = work.ServiceInvoice is null
                ? await MapInvoiceAsync(work, cancellationToken)
                : await MapServiceInvoiceAsync(work, cancellationToken);
            return new FiscalUblBuildResult(
                builder.Build(invoice), invoice.Cufe, invoice.QrPayload);
        }
        if (work.FiscalDocumentType is FiscalDocumentTypeCodes.SupportDocument or
            FiscalDocumentTypeCodes.SupportDocumentAdjustment)
            return await BuildSupportDocumentAsync(work, cancellationToken);
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.ElectronicPayroll)
            return await BuildElectronicPayrollAsync(work, cancellationToken);
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.DebitNote)
            return await BuildDebitNoteAsync(work, cancellationToken);
        if (work.FiscalDocumentType != FiscalDocumentTypeCodes.CreditNote)
            throw new FiscalSnapshotDataException(
                $"Fiscal document type '{work.FiscalDocumentType}' is unsupported.");

        if (work.FiscalOnlyCreditNote is not null)
            return await BuildFiscalOnlyCreditNoteAsync(
                work, work.FiscalOnlyCreditNote, cancellationToken);

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
        }).ToList();
        foreach (var charge in (snapshot.Return.Charges ?? []).Where(charge => charge.InvoicedAmount > 0m))
            lines.Add(new DianCreditNoteLine(
                lines.Count + 1, charge.Code, "999", charge.Name, "EA", 1m,
                charge.InvoicedUntaxedAmount, 0m, charge.InvoicedUntaxedAmount,
                [new DianTax(charge.TaxCode, TaxName(charge.TaxCode),
                    charge.InvoicedUntaxedAmount, charge.InvoicedTaxAmount, charge.TaxRate)]));
        return await BuildCreditNoteAsync(work, snapshot.FiscalNumber,
            snapshot.Return.ReturnedAt, snapshot.CurrencyCode,
            snapshot.Environment, snapshot.QrValidationUrl,
            snapshot.Customer, snapshot.Return.CustomerIdentification,
            snapshot.OriginalInvoiceNumber, snapshot.OriginalInvoiceCufe,
            snapshot.OriginalInvoiceIssuedOn, snapshot.Return.CorrectionCode,
            snapshot.Return.ReasonDescription, snapshot.Return.UntaxedAmount,
            snapshot.Return.TotalAmount,
            snapshot.Return.Lines.Sum(line => line.DiscountAmount),
            lines, cancellationToken,
            snapshot.Return.TotalAmount - snapshot.Return.UntaxedAmount - snapshot.Return.TaxAmount);
    }

    private Task<FiscalUblBuildResult> BuildFiscalOnlyCreditNoteAsync(
        FiscalGenerationWorkItem work,
        FiscalOnlyCreditNoteSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.CorrectionId != work.DocumentId ||
            snapshot.BusinessId != work.BusinessId ||
            snapshot.FiscalIssuerConfigurationId != work.Issuer.Id ||
            snapshot.FiscalNumber != work.FiscalNumber ||
            snapshot.Environment != work.Issuer.Environment ||
            snapshot.Lines.Count == 0 ||
            snapshot.Customer.Identification != snapshot.CustomerIdentification)
            throw new FiscalSnapshotDataException(
                "La nota crédito fiscal no coincide con su raíz ni con el adquirente congelado.");

        var lines = snapshot.Lines.OrderBy(line => line.LineNumber)
            .Select(line => new DianCreditNoteLine(
                line.LineNumber, line.ProductCode, line.ProductCodeScheme,
                line.Description, line.UnitCode, line.Quantity,
                line.UnitPrice, line.DiscountAmount, line.UntaxedAmount,
                [new DianTax(line.TaxCode, line.TaxName, line.UntaxedAmount,
                    line.TaxAmount, line.TaxRate)]))
            .ToArray();
        if (lines.Sum(line => line.UntaxedAmount) != snapshot.UntaxedAmount ||
            lines.Sum(line => line.UntaxedAmount + line.Taxes.Sum(tax => tax.Amount))
                != snapshot.TotalAmount)
            throw new FiscalSnapshotDataException(
                "Los valores de la nota crédito fiscal no concilian con sus líneas.");

        return BuildCreditNoteAsync(work, snapshot.FiscalNumber,
            snapshot.IssuedAt, snapshot.CurrencyCode, snapshot.Environment,
            snapshot.QrValidationUrl, snapshot.Customer,
            snapshot.CustomerIdentification, snapshot.OriginalInvoiceNumber,
            snapshot.OriginalInvoiceCufe, snapshot.OriginalInvoiceIssuedOn,
            DianCreditNoteCodes.FullCancellation,
            "Anulación de factura electrónica duplicada",
            snapshot.UntaxedAmount, snapshot.TotalAmount,
            snapshot.DiscountAmount, lines, cancellationToken);
    }

    private async Task<FiscalUblBuildResult> BuildCreditNoteAsync(
        FiscalGenerationWorkItem work,
        string fiscalNumber,
        DateTimeOffset issuedAt,
        string currencyCode,
        int environment,
        string qrValidationUrl,
        PosSaleUblPartyContract customer,
        string customerIdentification,
        string originalInvoiceNumber,
        string originalInvoiceCufe,
        DateOnly originalInvoiceIssuedOn,
        string correctionCode,
        string reason,
        decimal untaxedAmount,
        decimal totalAmount,
        decimal discountAmount,
        IReadOnlyList<DianCreditNoteLine> lines,
        CancellationToken cancellationToken,
        decimal payableRoundingAmount = 0m)
    {
        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var taxes = SummarizeTaxes(lines.SelectMany(line => line.Taxes));
        var cude = CudeCalculator.Calculate(new CudeInput(
            fiscalNumber, issuedAt, untaxedAmount, totalAmount,
            work.Issuer.SupplierTaxId, customerIdentification,
            pin, (FiscalEnvironment)environment,
            taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount))),
            qrValidationUrl);
        var note = new DianCreditNote(
            fiscalNumber, cude.Cude, issuedAt, currencyCode,
            DianCreditNoteCodes.ReferencesInvoiceOperation,
            correctionCode, reason, environment,
            new DianSoftware(work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
                work.Issuer.SoftwareId, pin),
            IssuerParty(work.Issuer), Party(customer),
            new DianInvoiceReference(originalInvoiceNumber, originalInvoiceCufe,
                originalInvoiceIssuedOn),
            lines, taxes, untaxedAmount, untaxedAmount,
            untaxedAmount + taxes.Sum(tax => tax.Amount),
            discountAmount, totalAmount, cude.QrPayload,
            PayableRoundingAmount: payableRoundingAmount);
        return new FiscalUblBuildResult(
            creditNoteBuilder.Build(note), cude.Cude, cude.QrPayload);
    }

    private async Task<FiscalUblBuildResult> BuildElectronicPayrollAsync(
        FiscalGenerationWorkItem work, CancellationToken cancellationToken)
    {
        var snapshot = work.ElectronicPayroll
            ?? throw new FiscalSnapshotDataException("The electronic-payroll fiscal snapshot is missing.");
        if (snapshot.BusinessId != work.BusinessId || work.DocumentId == Guid.Empty ||
            work.FiscalNumber != snapshot.FiscalPrefix + snapshot.FiscalConsecutive)
            throw new FiscalSnapshotDataException(
                "The electronic-payroll snapshot differs from its durable fiscal root.");
        var pin = await pins.ResolveAsync(work.BusinessId,
            snapshot.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        if (snapshot.WorkedDays != decimal.Truncate(snapshot.WorkedDays))
            throw new FiscalSnapshotDataException(
                "DIAN payroll worked days must be an integer after monthly consolidation.");
        var concepts = snapshot.Lines.Where(line => !line.IsEmployerCost &&
                line.NatureCode is "Earning" or "Deduction")
            .Select(line => new DianPayrollConcept(line.ConceptName, line.NatureCode,
                line.DianConceptCode, line.Amount, line.IsSalaryBase,
                line.Rate, line.BaseAmount)).ToArray();
        var payroll = new DianPayroll(
            snapshot.FiscalPrefix, snapshot.FiscalConsecutive, snapshot.GeneratedAt,
            work.Issuer.Environment, snapshot.PayrollPeriodCode,
            snapshot.EmploymentStart, snapshot.EmploymentEnd,
            snapshot.PeriodStart, snapshot.PeriodEnd,
            WorkedTime(snapshot.EmploymentStart, snapshot.PeriodEnd),
            decimal.ToInt32(snapshot.WorkedDays), snapshot.PaymentDates,
            work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
            work.Issuer.LegalName, work.Issuer.Address.CountryCode,
            work.Issuer.Address.DepartmentCode, work.Issuer.Address.MunicipalityCode,
            work.Issuer.Address.AddressLine, snapshot.SoftwareIdentificationCode, pin,
            snapshot.EmployeeCode, snapshot.EmployeeIdentificationTypeCode,
            snapshot.EmployeeIdentification, snapshot.EmployeeFirstName,
            snapshot.EmployeeOtherNames, snapshot.EmployeeFirstSurname,
            snapshot.EmployeeSecondSurname, snapshot.WorkerTypeCode,
            snapshot.WorkerSubtypeCode, snapshot.HighRiskPension,
            snapshot.IntegralSalary, snapshot.ContractTypeCode, snapshot.MonthlySalary,
            snapshot.PaymentMethodCode, snapshot.Bank, snapshot.BankAccountType,
            snapshot.BankAccountNumber, snapshot.Earnings, snapshot.Deductions,
            snapshot.NetPayable, concepts, snapshot.QrValidationUrl);
        var result = payrollBuilder.Build(payroll);
        return new FiscalUblBuildResult(result.Document, result.Cune, result.QrPayload);
    }

    private static int WorkedTime(DateOnly start, DateOnly end)
    {
        if (end < start) throw new FiscalSnapshotDataException("Employment starts after the payroll period.");
        var years = end.Year - start.Year;
        var months = end.Month - start.Month;
        var days = end.Day - start.Day;
        if (days < 0) { days += 30; months--; }
        if (months < 0) { months += 12; years--; }
        return Math.Max(0, years * 360 + months * 30 + days + 1);
    }

    private async Task<FiscalUblBuildResult> BuildDebitNoteAsync(
        FiscalGenerationWorkItem work,
        CancellationToken cancellationToken)
    {
        var snapshot = work.DebitNote
            ?? throw new FiscalSnapshotDataException("The debit-note fiscal payload is missing.");
        var value = snapshot.DebitNote;
        if (snapshot.FiscalIssuerConfigurationId != work.Issuer.Id ||
            value.DebitNoteId != work.DocumentId || value.BusinessId != work.BusinessId ||
            snapshot.FiscalNumber != work.FiscalNumber ||
            snapshot.Environment != work.Issuer.Environment)
            throw new FiscalSnapshotDataException(
                "The debit-note snapshot differs from its durable fiscal root.");
        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var lines = value.Lines.OrderBy(line => line.LineNumber)
            .Select(line => new DianDebitNoteLine(
                line.LineNumber, line.Description, "EA", line.Quantity, line.UnitPrice,
                line.UntaxedAmount,
                [new DianTax(line.TaxCode, TaxName(line.TaxCode), line.UntaxedAmount,
                    line.TaxAmount, line.TaxRate)]))
            .ToArray();
        var taxes = SummarizeTaxes(lines.SelectMany(line => line.Taxes));
        var cude = CudeCalculator.Calculate(new CudeInput(
            snapshot.FiscalNumber, value.IssuedAt, value.UntaxedAmount,
            value.TotalAmount, work.Issuer.SupplierTaxId, value.CustomerIdentification,
            pin, (FiscalEnvironment)snapshot.Environment,
            taxes.Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount))),
            snapshot.QrValidationUrl);
        var note = new DianDebitNote(
            snapshot.FiscalNumber, cude.Cude, value.IssuedAt, snapshot.CurrencyCode,
            DianDebitNoteCodes.ReferencesInvoiceOperation, value.ConceptCode,
            value.ReasonDescription, snapshot.Environment,
            new DianSoftware(work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
                work.Issuer.SoftwareId, pin),
            IssuerParty(work.Issuer), Party(snapshot.Customer),
            new DianInvoiceReference(snapshot.OriginalInvoiceNumber,
                snapshot.OriginalInvoiceCufe, snapshot.OriginalInvoiceIssuedOn),
            lines, taxes, value.UntaxedAmount, value.TotalAmount, cude.QrPayload);
        return new FiscalUblBuildResult(
            debitNoteBuilder.Build(note), cude.Cude, cude.QrPayload);
    }

    private async Task<FiscalUblBuildResult> BuildSupportDocumentAsync(
        FiscalGenerationWorkItem work, CancellationToken cancellationToken)
    {
        var snapshot = work.SupportDocument
            ?? throw new FiscalSnapshotDataException("The support-document fiscal payload is missing.");
        var receipt = snapshot.Receipt;
        var expense = snapshot.Expense;
        var costDocument = snapshot.CostDocument;
        var adjustment = snapshot.Adjustment;
        var expenseCancellation = snapshot.ExpenseCancellation;
        var sourceCount = (receipt is null ? 0 : 1) + (expense is null ? 0 : 1) +
                          (costDocument is null ? 0 : 1) + (adjustment is null ? 0 : 1) +
                          (expenseCancellation is null ? 0 : 1);
        if (sourceCount != 1)
            throw new FiscalSnapshotDataException(
                "The purchase-support snapshot must contain exactly one acquisition source.");
        if (adjustment is not null || expenseCancellation is not null)
            return await BuildSupportAdjustmentAsync(work, snapshot,
                cancellationToken);
        if (receipt is null && expense is null && costDocument is null)
            throw new FiscalSnapshotDataException(
                "The support-document snapshot must contain exactly one acquisition source.");
        var sourceDocumentId = receipt?.DocumentId ?? expense?.ExpenseId ?? costDocument!.Document.CostDocumentId;
        var sourceBusinessId = receipt?.BusinessId ?? expense?.BusinessId ?? costDocument!.BusinessId;
        if (snapshot.FiscalIssuerConfigurationId != work.Issuer.Id ||
            sourceDocumentId != work.DocumentId || sourceBusinessId != work.BusinessId ||
            snapshot.FiscalNumber != work.FiscalNumber || snapshot.Environment != work.Issuer.Environment)
            throw new FiscalSnapshotDataException(
                "The support-document snapshot differs from its durable fiscal root.");
        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var metadata = snapshot.Lines.ToDictionary(line => line.LineNumber);
        IReadOnlyList<DianInvoiceLine> lines;
        DateTimeOffset issuedAt;
        DateTimeOffset dueAt;
        string currencyCode;
        decimal untaxedAmount;
        decimal taxAmount;
        decimal totalAmount;
        decimal discountAmount;
        bool createsPayable;
        if (receipt is not null)
        {
            lines = receipt.Lines.OrderBy(line => line.LineNumber).Select(line =>
            {
                if (!metadata.TryGetValue(line.LineNumber, out var item))
                    throw new FiscalSnapshotDataException(
                        $"Support-document metadata is missing for line {line.LineNumber}.");
                if (string.IsNullOrWhiteSpace(item.DianTaxCode))
                    throw new FiscalSnapshotDataException(
                        $"Support-document metadata has no DIAN tax code for line {line.LineNumber}.");
                return new DianInvoiceLine(line.LineNumber, item.ProductCode, item.ProductCodeScheme,
                    line.Description, item.UnitCode, line.Quantity, line.UnitCost, line.DiscountAmount,
                    line.NetAmount, [new DianTax(item.DianTaxCode, item.TaxName,
                        line.NetAmount, line.TaxAmount, line.TaxRate)]);
            }).ToArray();
            issuedAt = receipt.ReceivedAt;
            dueAt = receipt.DueDate ?? receipt.ReceivedAt;
            currencyCode = receipt.CurrencyCode;
            untaxedAmount = receipt.NetAmount;
            taxAmount = receipt.TaxAmount;
            totalAmount = receipt.GrandTotal;
            discountAmount = receipt.Lines.Sum(x => x.DiscountAmount);
            createsPayable = receipt.CreatesPayable;
        }
        else if (costDocument is not null)
        {
            var cost = costDocument.Document;
            lines = cost.Lines.OrderBy(line => line.LineNumber).Select(line =>
            {
                if (!metadata.TryGetValue(line.LineNumber, out var item) ||
                    string.IsNullOrWhiteSpace(item.DianTaxCode))
                    throw new FiscalSnapshotDataException(
                        $"Faltan los datos tributarios DIAN de la línea {line.LineNumber} del costo adicional.");
                return new DianInvoiceLine(line.LineNumber, item.ProductCode, item.ProductCodeScheme,
                    line.Description, item.UnitCode, 1m, line.Amount, 0m, line.Amount,
                    [new DianTax(item.DianTaxCode, item.TaxName,
                        line.TaxableBaseAmount, line.TaxAmount, line.TaxRate)]);
            }).ToArray();
            issuedAt = cost.IssuedAt;
            dueAt = cost.DueDate ?? cost.IssuedAt;
            currencyCode = cost.CurrencyCode;
            untaxedAmount = cost.NetAmount;
            taxAmount = cost.TaxAmount;
            totalAmount = cost.GrandTotal;
            discountAmount = 0m;
            createsPayable = cost.CreatesPayable;
        }
        else
        {
            if (!metadata.TryGetValue(1, out var item))
                throw new FiscalSnapshotDataException(
                    "Support-document metadata is missing for the expense line.");
            var taxRate = expense!.TaxExclusiveAmount == 0 ? 0 :
                decimal.Round(expense.VatAmount / expense.TaxExclusiveAmount * 100m,
                    6, MidpointRounding.AwayFromZero);
            lines = [new DianInvoiceLine(1, item.ProductCode, item.ProductCodeScheme,
                expense.Description, item.UnitCode, 1m, expense.TaxExclusiveAmount, 0,
                expense.TaxExclusiveAmount, [new DianTax("01", item.TaxName,
                    expense.TaxExclusiveAmount, expense.VatAmount, taxRate)])];
            issuedAt = expense.IssuedAt;
            dueAt = expense.DueDate;
            currencyCode = expense.CurrencyCode;
            untaxedAmount = expense.TaxExclusiveAmount;
            taxAmount = expense.VatAmount;
            totalAmount = expense.GrossAmount;
            discountAmount = 0;
            createsPayable = true;
        }
        var taxes = SummarizeTaxes(lines.SelectMany(line => line.Taxes));
        var cuds = CudsCalculator.Calculate(new CudsInput(snapshot.FiscalNumber,
            issuedAt, untaxedAmount,
            taxes.Where(x => x.Code == "01").Sum(x => x.Amount), totalAmount,
            snapshot.Seller.Identification, work.Issuer.SupplierTaxId, pin,
            (FiscalEnvironment)snapshot.Environment), snapshot.QrValidationUrl);
        var auth = snapshot.Authorization;
        var document = new DianSupportDocument(snapshot.FiscalNumber, cuds.Cuds, issuedAt,
            currencyCode, snapshot.Environment,
            new DianAuthorization(auth.Number, auth.ValidFrom, auth.ValidUntil,
                auth.Prefix, auth.RangeStart, auth.RangeEnd),
            new DianSoftware(work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
                work.Issuer.SoftwareId, pin), SupportSeller(snapshot), IssuerParty(work.Issuer),
            snapshot.SellerOriginCode, snapshot.SellerPostalZone ?? string.Empty,
            lines, taxes, new DianPayment(createsPayable ? "2" : "1", "42",
                DateOnly.FromDateTime(dueAt.Date), null),
            untaxedAmount, untaxedAmount, untaxedAmount + taxAmount,
            discountAmount, totalAmount, cuds.QrPayload);
        return new FiscalUblBuildResult(
            supportDocumentBuilder.Build(document), cuds.Cuds, cuds.QrPayload);
    }

    private async Task<FiscalUblBuildResult> BuildSupportAdjustmentAsync(
        FiscalGenerationWorkItem work,
        PurchaseSupportFiscalSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var adjustment = snapshot.Adjustment;
        var cancellation = snapshot.ExpenseCancellation;
        if ((adjustment is null) == (cancellation is null))
            throw new FiscalSnapshotDataException("The support adjustment has no unique source.");
        var documentId = adjustment?.ReturnId ?? cancellation!.CancellationId;
        var businessId = adjustment?.BusinessId ?? cancellation!.BusinessId;
        if (work.FiscalDocumentType != FiscalDocumentTypeCodes.SupportDocumentAdjustment ||
            snapshot.FiscalIssuerConfigurationId != work.Issuer.Id ||
            documentId != work.DocumentId ||
            businessId != work.BusinessId ||
            snapshot.FiscalNumber != work.FiscalNumber ||
            snapshot.Environment != work.Issuer.Environment ||
            string.IsNullOrWhiteSpace(snapshot.OriginalSupportNumber) ||
            string.IsNullOrWhiteSpace(snapshot.OriginalSupportCuds) ||
            snapshot.OriginalSupportIssuedOn is null)
            throw new FiscalSnapshotDataException(
                "The support-document adjustment differs from its durable fiscal root or original reference.");
        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var metadata = snapshot.Lines.ToDictionary(line => line.LineNumber);
        IReadOnlyList<DianCreditNoteLine> lines;
        DateTimeOffset issuedAt; string currencyCode; decimal untaxedAmount, totalAmount, discountAmount;
        if (adjustment is not null)
        {
            lines = adjustment.Lines.OrderBy(line => line.LineNumber).Select(line =>
            {
                if (!metadata.TryGetValue(line.LineNumber, out var item))
                    throw new FiscalSnapshotDataException(
                        $"Support-adjustment metadata is missing for line {line.LineNumber}.");
                if (string.IsNullOrWhiteSpace(item.DianTaxCode))
                    throw new FiscalSnapshotDataException(
                        $"Support-adjustment metadata has no DIAN tax code for line {line.LineNumber}.");
                return new DianCreditNoteLine(line.LineNumber, item.ProductCode,
                    item.ProductCodeScheme, line.Description, item.UnitCode, line.Quantity,
                    line.UnitCost, line.DiscountAmount, line.NetAmount,
                    [new DianTax(item.DianTaxCode, item.TaxName, line.NetAmount,
                        line.TaxAmount, line.TaxRate)]);
            }).ToArray();
            issuedAt = adjustment.ReturnedAt;
            currencyCode = adjustment.CurrencyCode;
            untaxedAmount = adjustment.NetAmount;
            totalAmount = adjustment.TotalAmount;
            discountAmount = adjustment.Lines.Sum(line => line.DiscountAmount);
        }
        else
        {
            var original = cancellation!.Original;
            if (!metadata.TryGetValue(1, out var item))
                throw new FiscalSnapshotDataException(
                    "Support-adjustment metadata is missing for the expense line.");
            if (string.IsNullOrWhiteSpace(item.DianTaxCode))
                throw new FiscalSnapshotDataException(
                    "Support-adjustment metadata has no DIAN tax code for the expense line.");
            var taxRate = original.TaxExclusiveAmount == 0 ? 0 :
                decimal.Round(original.VatAmount / original.TaxExclusiveAmount * 100m,
                    6, MidpointRounding.AwayFromZero);
            lines = [new DianCreditNoteLine(1, item.ProductCode, item.ProductCodeScheme,
                original.Description, item.UnitCode, 1m, original.TaxExclusiveAmount, 0,
                original.TaxExclusiveAmount, [new DianTax(item.DianTaxCode, item.TaxName,
                    original.TaxExclusiveAmount, original.VatAmount, taxRate)])];
            issuedAt = cancellation.CancelledAt;
            currencyCode = original.CurrencyCode;
            untaxedAmount = original.TaxExclusiveAmount;
            totalAmount = original.GrossAmount;
            discountAmount = 0;
        }
        var taxes = SummarizeTaxes(lines.SelectMany(line => line.Taxes));
        var cuds = CudsCalculator.Calculate(new CudsInput(snapshot.FiscalNumber,
            issuedAt, untaxedAmount,
            taxes.Where(tax => tax.Code == "01").Sum(tax => tax.Amount),
            totalAmount, snapshot.Seller.Identification,
            work.Issuer.SupplierTaxId, pin, (FiscalEnvironment)snapshot.Environment),
            snapshot.QrValidationUrl);
        var note = new DianCreditNote(snapshot.FiscalNumber, cuds.Cuds,
            issuedAt, currencyCode,
            DianCreditNoteCodes.ReferencesInvoiceOperation, cancellation is null ? "1" : "2",
            cancellation is null
                ? "Devolución parcial de los bienes y/o no aceptación parcial del servicio"
                : "Anulación del documento soporte",
            snapshot.Environment,
            new DianSoftware(work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
                work.Issuer.SoftwareId, pin), SupportSeller(snapshot),
            IssuerParty(work.Issuer),
            new DianInvoiceReference(snapshot.OriginalSupportNumber!,
                snapshot.OriginalSupportCuds!, snapshot.OriginalSupportIssuedOn.Value),
            lines, taxes, untaxedAmount, untaxedAmount,
            totalAmount, discountAmount,
            totalAmount, cuds.QrPayload,
            DocumentTypeCode: "95", CustomizationId: snapshot.SellerOriginCode,
            ProfileId: DianCreditNoteCodes.SupportAdjustmentProfileId,
            UniqueCodeScheme: "CUDS-SHA384",
            OriginalUniqueCodeScheme: "CUDS-SHA384", BuyerGenerated: true,
            SellerPostalZone: snapshot.SellerPostalZone);
        return new FiscalUblBuildResult(
            creditNoteBuilder.Build(note), cuds.Cuds, cuds.QrPayload);
    }

    private static string TaxName(string code) => code switch
    {
        "01" => "IVA",
        "04" => "INC",
        "22" => "INC Bolsas",
        _ => "Impuesto"
    };

    private static DianTax[] SummarizeTaxes(IEnumerable<DianTax> lineTaxes) =>
        lineTaxes
            .GroupBy(tax => new { tax.Code, tax.Name, tax.Percent })
            .OrderBy(group => group.Key.Code, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Percent)
            .Select(group => new DianTax(
                group.Key.Code,
                group.Key.Name,
                group.Sum(tax => tax.TaxableAmount),
                group.Sum(tax => tax.Amount),
                group.Key.Percent))
            .ToArray();

    private static DianParty IssuerParty(FiscalIssuerWorkConfiguration issuer) => new(
        issuer.SupplierTaxId, issuer.SupplierCheckDigit, issuer.IdentificationTypeCode,
        "1", issuer.LegalName, issuer.TradeName, issuer.TaxLevelCode,
        issuer.TaxSchemeId, issuer.TaxSchemeName, Address(issuer.Address), null, null);


    private static DianParty Party(PosSaleUblPartyContract value)
    {
        var identificationType = DianIdentificationTypeCode(value.IdentificationTypeCode);
        return new DianParty(
            value.Identification,
            PosSaleFiscalMappings.DianCheckDigit(
                identificationType, value.Identification, value.CheckDigit),
            identificationType,
            value.OrganizationTypeCode, value.RegistrationName, value.TradeName,
            value.TaxResponsibilityCode, value.TaxSchemeId, value.TaxSchemeName,
            Address(value.Address), value.Email, value.Telephone);
    }

    private static DianParty SupportSeller(PurchaseSupportFiscalSnapshot snapshot)
    {
        var seller = Party(snapshot.Seller);
        if (snapshot.SellerOriginCode != "10") return seller;
        if (!ColombianNit.TryCalculateVerificationDigit(
                seller.Identification, out var verificationDigit))
            throw new FiscalSnapshotDataException(
                "A resident support-document seller requires a numeric Colombian NIT.");
        return seller with
        {
            CheckDigit = verificationDigit.ToString(),
            IdentificationTypeCode = "31"
        };
    }

    private static DianParty SupplierParty(
        PosSaleUblPartyContract value,
        FiscalGenerationWorkItem work)
    {
        var frozen = Party(value);
        if (!work.IsCorrection) return frozen;

        var issuer = work.Issuer;
        return frozen with
        {
            OrganizationTypeCode = issuer.LegalProfileOrganizationType == "NaturalPerson" ? "2" : "1",
            RegistrationName = string.IsNullOrWhiteSpace(issuer.LegalProfileName)
                ? frozen.RegistrationName
                : issuer.LegalProfileName.Trim(),
            Email = string.IsNullOrWhiteSpace(issuer.LegalProfileEmail)
                ? frozen.Email
                : issuer.LegalProfileEmail.Trim(),
            Telephone = string.IsNullOrWhiteSpace(issuer.LegalProfileTelephone)
                ? frozen.Telephone
                : issuer.LegalProfileTelephone.Trim()
        };
    }

    private static DateTimeOffset? ResolveSigningTime(FiscalGenerationWorkItem work) =>
        work.FiscalDocumentType switch
        {
            FiscalDocumentTypeCodes.Invoice =>
                work.Sale?.FiscalSnapshot?.IssuedAt ??
                work.ServiceInvoice?.FiscalSnapshot.IssuedAt,
            FiscalDocumentTypeCodes.SupportDocument =>
                work.SupportDocument?.Receipt?.ReceivedAt ??
                work.SupportDocument?.Expense?.IssuedAt ??
                work.SupportDocument?.CostDocument?.Document.IssuedAt,
            FiscalDocumentTypeCodes.SupportDocumentAdjustment =>
                work.SupportDocument?.Adjustment?.ReturnedAt ??
                work.SupportDocument?.ExpenseCancellation?.CancelledAt,
            FiscalDocumentTypeCodes.CreditNote =>
                work.CreditNote?.Return.ReturnedAt ?? work.FiscalOnlyCreditNote?.IssuedAt,
            FiscalDocumentTypeCodes.DebitNote => work.DebitNote?.DebitNote.IssuedAt,
            _ => null
        };

    private static string DianIdentificationTypeCode(string value) =>
        PosSaleFiscalMappings.DianIdentificationTypeCode(value)
        ?? throw new FiscalSnapshotDataException(
            $"Identification type '{value}' has no DIAN equivalence.");

    private static DianAddress Address(PosSaleUblAddressContract value) => new(
        value.MunicipalityCode, value.CityName, value.DepartmentName, value.DepartmentCode,
        value.AddressLine, value.CountryCode, value.CountryName);

    private sealed class FiscalSnapshotDataException(string message) : Exception(message);
    private sealed record FiscalUblBuildResult(
        DianUblDocument Document, string UniqueCode, string QrPayload);
}
