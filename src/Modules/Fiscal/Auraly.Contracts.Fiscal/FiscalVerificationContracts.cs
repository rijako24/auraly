using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;

namespace Auraly.Contracts.Fiscal;

public sealed record FiscalKeyReference(
    Guid TenantId,
    Guid BusinessId,
    string AuthorizationNumber,
    string TechnicalKeyVersion,
    FiscalEnvironment Environment);

public sealed record FiscalVerificationMaterial(
    FiscalTechnicalKey TechnicalKey,
    string SupplierTaxId,
    FiscalEnvironment Environment,
    string QrValidationUrl);

public interface IFiscalTechnicalKeyProvider
{
    Task<FiscalVerificationMaterial?> ResolveAsync(
        FiscalKeyReference reference,
        CancellationToken cancellationToken);
}

public sealed record FiscalSnapshotVerificationResult(
    bool IsVerified,
    string CufeReceived,
    string? CufeCalculated,
    string? ConflictReason);

public interface IFiscalSnapshotVerifier
{
    Task<FiscalSnapshotVerificationResult> VerifyAsync(
        PosSaleUploadRequest request,
        CancellationToken cancellationToken = default);
}

