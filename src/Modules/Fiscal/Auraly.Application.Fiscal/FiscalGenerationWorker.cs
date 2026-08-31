using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Payroll.Contracts;
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
    SalesDebitNoteFiscalSnapshot? DebitNote,
    FiscalIssuerWorkConfiguration Issuer, FiscalAuthorizationWorkConfiguration? Authorization,
    PurchaseSupportFiscalSnapshot? SupportDocument = null,
    ElectronicPayrollSnapshot? ElectronicPayroll = null,
    ServiceInvoiceSnapshot? ServiceInvoice = null);

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
            var signed = await signer.SignAsync(new FiscalSigningRequest(
                work.BusinessId, work.Issuer.SupplierTaxId, unsigned.Xml,
                new FiscalCertificateReference(work.BusinessId, work.Issuer.CertificateProvider,
                    work.Issuer.CertificateKeyReference, work.Issuer.CertificateThumbprint),
                generatedAt), cancellationToken);
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
            snapshot.Environment,
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
        var taxes = lines.SelectMany(line => line.Taxes)
            .GroupBy(tax => new { tax.Code, tax.Name, tax.Percent })
            .OrderBy(group => group.Key.Code, StringComparer.Ordinal)
            .Select(group => new DianTax(group.Key.Code, group.Key.Name,
                group.Sum(tax => tax.TaxableAmount), group.Sum(tax => tax.Amount),
                group.Key.Percent)).ToArray();

        return new DianInvoice(snapshot.FiscalNumber, snapshot.Cufe, snapshot.IssuedAt,
            ubl.CurrencyCode, ubl.InvoiceTypeCode, snapshot.Environment,
            new DianAuthorization(ubl.Authorization.Number, ubl.Authorization.ValidFrom,
                ubl.Authorization.ValidUntil, ubl.Authorization.Prefix,
                ubl.Authorization.RangeStart, ubl.Authorization.RangeEnd),
            new DianSoftware(ubl.Supplier.Identification, ubl.Supplier.CheckDigit,
                ubl.SoftwareIdentificationCode, pin), Party(ubl.Supplier), Party(ubl.Customer),
            lines, taxes, new DianPayment(ubl.PaymentFormCode, ubl.PaymentMeansCode,
                ubl.DueDate, ubl.PaymentReference), snapshot.UntaxedAmount,
            snapshot.UntaxedAmount, snapshot.PayableAmount,
            invoice.Lines.Sum(line => line.DiscountAmount), snapshot.PayableAmount,
            snapshot.QrPayload);
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
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.SupportDocument)
            return await BuildSupportDocumentAsync(work, cancellationToken);
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.ElectronicPayroll)
            return await BuildElectronicPayrollAsync(work, cancellationToken);
        if (work.FiscalDocumentType == FiscalDocumentTypeCodes.DebitNote)
            return await BuildDebitNoteAsync(work, cancellationToken);
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
        var taxes = lines.SelectMany(line => line.Taxes)
            .GroupBy(tax => new { tax.Code, tax.Name, tax.Percent })
            .OrderBy(group => group.Key.Code, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Percent)
            .Select(group => new DianTax(group.Key.Code, group.Key.Name,
                group.Sum(tax => tax.TaxableAmount), group.Sum(tax => tax.Amount),
                group.Key.Percent)).ToArray();
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
        if (snapshot.FiscalIssuerConfigurationId != work.Issuer.Id ||
            receipt.DocumentId != work.DocumentId || receipt.BusinessId != work.BusinessId ||
            snapshot.FiscalNumber != work.FiscalNumber || snapshot.Environment != work.Issuer.Environment)
            throw new FiscalSnapshotDataException(
                "The support-document snapshot differs from its durable fiscal root.");
        var pin = await pins.ResolveAsync(work.BusinessId,
            work.Issuer.SoftwarePinSecretReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(pin))
            throw new FiscalSnapshotDataException("The software PIN secret could not be resolved.");
        var metadata = snapshot.Lines.ToDictionary(line => line.LineNumber);
        var lines = receipt.Lines.OrderBy(line => line.LineNumber).Select(line =>
        {
            if (!metadata.TryGetValue(line.LineNumber, out var item))
                throw new FiscalSnapshotDataException(
                    $"Support-document metadata is missing for line {line.LineNumber}.");
            return new DianInvoiceLine(line.LineNumber, item.ProductCode, item.ProductCodeScheme,
                line.Description, item.UnitCode, line.Quantity, line.UnitCost, line.DiscountAmount,
                line.NetAmount, [new DianTax(line.TaxCode, item.TaxName,
                    line.NetAmount, line.TaxAmount, line.TaxRate)]);
        }).ToArray();
        var taxes = lines.SelectMany(line => line.Taxes)
            .GroupBy(tax => new { tax.Code, tax.Name, tax.Percent })
            .Select(group => new DianTax(group.Key.Code, group.Key.Name,
                group.Sum(x => x.TaxableAmount), group.Sum(x => x.Amount), group.Key.Percent))
            .ToArray();
        var cuds = CudsCalculator.Calculate(new CudsInput(snapshot.FiscalNumber,
            receipt.ReceivedAt, receipt.NetAmount,
            taxes.Where(x => x.Code == "01").Sum(x => x.Amount), receipt.GrandTotal,
            snapshot.Seller.Identification, work.Issuer.SupplierTaxId, pin,
            (FiscalEnvironment)snapshot.Environment), snapshot.QrValidationUrl);
        var auth = snapshot.Authorization;
        var invoice = new DianInvoice(snapshot.FiscalNumber, cuds.Cuds, receipt.ReceivedAt,
            receipt.CurrencyCode, "05", snapshot.Environment,
            new DianAuthorization(auth.Number, auth.ValidFrom, auth.ValidUntil,
                auth.Prefix, auth.RangeStart, auth.RangeEnd),
            new DianSoftware(work.Issuer.SupplierTaxId, work.Issuer.SupplierCheckDigit,
                work.Issuer.SoftwareId, pin), Party(snapshot.Seller), IssuerParty(work.Issuer),
            lines, taxes, new DianPayment(receipt.CreatesPayable ? "2" : "1", "42",
                DateOnly.FromDateTime((receipt.DueDate ?? receipt.ReceivedAt).Date), null),
            receipt.NetAmount, receipt.NetAmount, receipt.GrandTotal,
            receipt.Lines.Sum(x => x.DiscountAmount), receipt.GrandTotal, cuds.QrPayload,
            snapshot.SellerOriginCode,
            "DIAN 2.1: documento soporte en adquisiciones efectuadas a no obligados a facturar.",
            "CUDS-SHA384", true);
        return new FiscalUblBuildResult(builder.Build(invoice), cuds.Cuds, cuds.QrPayload);
    }

    private static string TaxName(string code) => code switch
    {
        "01" => "IVA",
        "04" => "INC",
        "22" => "INC Bolsas",
        _ => "Impuesto"
    };

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
