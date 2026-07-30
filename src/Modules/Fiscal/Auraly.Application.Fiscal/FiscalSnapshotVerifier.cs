using System.Security.Cryptography;
using System.Text;
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

        var structuralConflict = ValidateStructure(request);
        if (structuralConflict is not null)
        {
            return Conflict(snapshot.Cufe, null, structuralConflict);
        }

        var environment = (FiscalEnvironment)snapshot.Environment;
        var material = await keyProvider.ResolveAsync(
            new FiscalKeyReference(
                request.TenantId,
                request.BusinessId,
                snapshot.AuthorizationNumber,
                snapshot.TechnicalKeyVersion,
                environment),
            cancellationToken);
        if (material is null)
        {
            return Conflict(snapshot.Cufe, null, "Fiscal verification material was not found.");
        }

        if (!string.Equals(material.SupplierTaxId, snapshot.SupplierTaxId, StringComparison.Ordinal) ||
            material.Environment != environment)
        {
            return Conflict(snapshot.Cufe, null, "The fiscal issuer or environment differs from the server configuration.");
        }

        var taxes = request.Lines
            .GroupBy(line => line.TaxCode, StringComparer.Ordinal)
            .Select(group => new FiscalTaxAmount(group.Key, group.Sum(line => line.TaxAmount)))
            .ToArray();
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
        {
            return Conflict(snapshot.Cufe, calculated.Cufe, "The received CUFE differs from the server calculation.");
        }

        if (!string.Equals(snapshot.QrPayload, calculated.QrPayload, StringComparison.Ordinal))
        {
            return Conflict(snapshot.Cufe, calculated.Cufe, "The received QR payload differs from the server calculation.");
        }

        return new FiscalSnapshotVerificationResult(
            true,
            snapshot.Cufe,
            calculated.Cufe,
            null);
    }

    private static string? ValidateStructure(PosSaleUploadRequest request)
    {
        var snapshot = request.FiscalSnapshot;
        if (request.DocumentId == Guid.Empty ||
            request.TenantId == Guid.Empty ||
            request.BusinessId == Guid.Empty ||
            request.LocationId == Guid.Empty ||
            request.WarehouseId == Guid.Empty ||
            request.RegisterId == Guid.Empty)
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

        if (request.Payments.Count == 0)
        {
            return "A sale requires at least one payment.";
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
                line.TaxRate < 0)
            {
                return $"Line {line.LineNumber} contains invalid values.";
            }

            var expectedTax = decimal.Round(
                line.UntaxedAmount * line.TaxRate / 100m,
                2,
                MidpointRounding.ToEven);
            if (expectedTax != line.TaxAmount)
            {
                return $"Line {line.LineNumber} tax does not match its frozen rate and taxable amount.";
            }

            var untaxed = decimal.Round(
                (line.Quantity * line.UnitPrice) - line.DiscountAmount,
                2,
                MidpointRounding.ToEven);
            if (untaxed != line.UntaxedAmount ||
                line.LineTotal != line.UntaxedAmount + line.TaxAmount)
            {
                return $"Line {line.LineNumber} totals are inconsistent.";
            }
        }

        var untaxedTotal = request.Lines.Sum(line => line.UntaxedAmount);
        var taxTotal = request.Lines.Sum(line => line.TaxAmount);
        var payableTotal = request.Lines.Sum(line => line.LineTotal);
        if (snapshot.UntaxedAmount != untaxedTotal ||
            snapshot.TaxAmount != taxTotal ||
            snapshot.PayableAmount != payableTotal)
        {
            return "Document totals do not match its lines.";
        }

        if (request.Payments.Sum(payment => payment.Amount) != snapshot.PayableAmount ||
            request.Payments.Any(payment => payment.PaymentNumber <= 0 || payment.Amount <= 0))
        {
            return "Payments do not match the payable amount.";
        }

        var lineTaxes = request.Lines
            .GroupBy(line => line.TaxCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.TaxAmount), StringComparer.Ordinal);
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

