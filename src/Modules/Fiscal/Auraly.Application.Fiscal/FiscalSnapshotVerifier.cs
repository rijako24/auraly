using System.Security.Cryptography;
using System.Text;
using Auraly.BuildingBlocks.Domain.Money;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;

namespace Auraly.Application.Fiscal;

public sealed class FiscalSnapshotVerifier(IFiscalTechnicalKeyProvider keyProvider)
    : IFiscalSnapshotVerifier
{
    public async Task<FiscalSnapshotVerificationResult> VerifyAsync(
        PosSaleUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.FiscalSnapshot;
        if (snapshot is null)
        {
            return Conflict(string.Empty, null, "The fiscal snapshot is required.");
        }

        var structuralConflict = FiscalSnapshotValidator.ValidateStructure(request);
        if (structuralConflict is not null)
        {
            return Conflict(snapshot.Cufe, null, structuralConflict);
        }

        var environment = (FiscalEnvironment)snapshot.Environment;
        var material = await keyProvider.ResolveAsync(
            new FiscalKeyReference(
                request.TenantId,
                request.BusinessId,
                snapshot.FiscalAuthorizationId,
                snapshot.AuthorizationNumber,
                snapshot.TechnicalKeyVersion,
                environment),
            cancellationToken);
        if (material is null)
        {
            return Conflict(snapshot.Cufe, null, "Fiscal verification material was not found.");
        }

        return FiscalSnapshotValidator.Verify(request, material);
    }

    private static FiscalSnapshotVerificationResult Conflict(
        string received,
        string? calculated,
        string reason) =>
        new(false, received, calculated, reason);
}

public static class FiscalSnapshotValidator
{
    public static FiscalSnapshotVerificationResult Verify(
        PosSaleUploadRequest request,
        FiscalVerificationMaterial material)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(material);
        var snapshot = request.FiscalSnapshot;
        var structuralConflict = ValidateStructure(request);
        if (snapshot is null || structuralConflict is not null)
            return Conflict(snapshot?.Cufe ?? string.Empty, null,
                structuralConflict ?? "The fiscal snapshot is required.");

        var environment = (FiscalEnvironment)snapshot.Environment;
        if (!string.Equals(material.SupplierTaxId, snapshot.SupplierTaxId, StringComparison.Ordinal) ||
            material.Environment != environment)
        {
            return Conflict(snapshot.Cufe, null,
                "The fiscal issuer or environment differs from the server configuration.");
        }

        var taxes = PosSaleTaxSummary.Calculate(request.Lines.Select(line =>
                new PosSaleTaxContract(line.TaxCode, line.TaxAmount)), request.Charges)
            .Select(tax => new FiscalTaxAmount(tax.Code, tax.Amount)).ToArray();
        var calculated = CufeCalculator.Calculate(
            new CufeInput(
                snapshot.FiscalNumber,
                snapshot.IssuedAt,
                snapshot.UntaxedAmount,
                snapshot.PayableAmount,
                material.SupplierTaxId,
                snapshot.CustomerIdentification,
                material.TechnicalKey,
                material.Environment,
                taxes),
            material.QrValidationUrl);

        if (!FixedTimeEquals(snapshot.Cufe, calculated.Cufe))
            return Conflict(snapshot.Cufe, calculated.Cufe,
                "The received CUFE differs from the server calculation.");
        if (!string.Equals(snapshot.QrPayload, calculated.QrPayload, StringComparison.Ordinal))
            return Conflict(snapshot.Cufe, calculated.Cufe,
                "The received QR payload differs from the server calculation.");

        return new FiscalSnapshotVerificationResult(
            true, snapshot.Cufe, calculated.Cufe, null);
    }

    public static string? ValidateStructure(PosSaleUploadRequest request)
    {
        var snapshot = request.FiscalSnapshot;
        if (snapshot is null)
        {
            return "The fiscal snapshot is required.";
        }

        if (request.DocumentId == Guid.Empty ||
            request.TenantId == Guid.Empty ||
            request.BusinessId == Guid.Empty ||
            request.WarehouseId == Guid.Empty ||
            request.WorkSessionId == Guid.Empty ||
            request.SoldByUserId == Guid.Empty)
        {
            return "One or more required identifiers are empty.";
        }
        if (request.SourceMode is not (
                SaleSourceModes.PosEdge or SaleSourceModes.Online) ||
            (request.SourceMode == SaleSourceModes.PosEdge &&
             request.DeviceId == Guid.Empty) ||
            (request.SourceMode == SaleSourceModes.Online &&
             request.DeviceId != Guid.Empty))
            return "The sale source and device identity are inconsistent.";

        if (request.Lines.Count == 0)
        {
            return "A sale requires at least one line.";
        }

        if (request.Payments.Count == 0 && request.Credit is null)
        {
            return "A sale requires a real payment or a financed balance.";
        }

        if (!string.Equals(snapshot.DocumentType, PosSaleDocumentTypes.Invoice, StringComparison.Ordinal))
        {
            return "The document type is not supported.";
        }

        if (!string.Equals(
                snapshot.FiscalNumber,
                $"{snapshot.Prefix}{snapshot.Consecutive}",
                StringComparison.Ordinal))
        {
            return "The fiscal number does not match its prefix and consecutive.";
        }

        if (!Enum.IsDefined(typeof(FiscalEnvironment), snapshot.Environment))
        {
            return "The fiscal environment is invalid.";
        }

        var expectedLineNumber = 1;
        foreach (var line in request.Lines.OrderBy(line => line.LineNumber))
        {
            if (line.LineNumber != expectedLineNumber++)
            {
                return "Line numbers must be consecutive and start at one.";
            }

            if (line.ProductId == Guid.Empty ||
                line.Quantity <= 0 ||
                line.UnitPrice < 0 ||
                line.DiscountAmount < 0 ||
                line.TaxAmount < 0 ||
                line.UntaxedAmount < 0 ||
                line.LineTotal < 0 ||
                line.TaxRate < 0)
            {
                return $"Line {line.LineNumber} contains invalid values.";
            }

            if (line.UntaxedAmount != MonetaryRounding.RoundLineAmount(line.UntaxedAmount) ||
                line.TaxAmount != MonetaryRounding.RoundLineAmount(line.TaxAmount) ||
                line.LineTotal != MonetaryRounding.RoundLineAmount(line.LineTotal) ||
                line.LineTotal != line.UntaxedAmount + line.TaxAmount)
            {
                return $"Line {line.LineNumber} does not contain closed monetary totals.";
            }
        }

        var untaxedTotal = request.Lines.Sum(line => line.UntaxedAmount) + (request.Charges?.Sum(charge => charge.InvoicedUntaxedAmount) ?? 0);
        var taxTotal = request.Lines.Sum(line => line.TaxAmount) + (request.Charges?.Sum(charge => charge.InvoicedTaxAmount) ?? 0);
        var payableTotal = untaxedTotal + taxTotal;
        if (snapshot.UntaxedAmount != untaxedTotal ||
            snapshot.TaxAmount != taxTotal ||
            snapshot.PayableAmount != payableTotal + snapshot.PayableRoundingAmount ||
            request.CommercialSnapshot.UntaxedAmount != untaxedTotal ||
            request.CommercialSnapshot.TaxAmount != taxTotal ||
            request.CommercialSnapshot.PayableAmount !=
                payableTotal + request.CommercialSnapshot.PayableRoundingAmount ||
            snapshot.PayableRoundingAmount !=
                request.CommercialSnapshot.PayableRoundingAmount)
        {
            return "Document totals do not match its lines.";
        }

        var financedAmount = request.Credit?.Amount ?? 0m;
        if (request.Payments.Sum(payment => payment.CollectedAmount) + financedAmount !=
                request.CommercialSnapshot.NetPayableAmount ||
            request.Payments.Any(payment => payment.PaymentNumber <= 0 || payment.Amount <= 0) ||
            request.Credit is { Amount: <= 0m })
        {
            return "Actual payments plus financed balance do not match the net sale settlement.";
        }

        var lineTaxes = PosSaleTaxSummary.Calculate(request.Lines.Select(line =>
                new PosSaleTaxContract(line.TaxCode, line.TaxAmount)), request.Charges)
            .ToDictionary(tax => tax.Code, tax => tax.Amount, StringComparer.Ordinal);
        var snapshotTaxes = snapshot.Taxes
            .GroupBy(tax => tax.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(tax => tax.Amount), StringComparer.Ordinal);
        if (lineTaxes.Count != snapshotTaxes.Count ||
            lineTaxes.Any(pair => snapshotTaxes.GetValueOrDefault(pair.Key) != pair.Value))
        {
            return "The tax summary does not match the lines.";
        }

        return null;
    }

    private static FiscalSnapshotVerificationResult Conflict(
        string received,
        string? calculated,
        string reason) =>
        new(false, received, calculated, reason);

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

