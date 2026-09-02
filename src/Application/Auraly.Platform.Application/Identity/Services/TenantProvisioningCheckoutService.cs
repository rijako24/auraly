using System.Security.Cryptography;
using System.Text.Json;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantProvisioningCheckoutService(
    ITenantCommercialQuoteService quotes,
    ITenantCommercialCatalogStore catalog,
    ITenantProvisioningCheckoutStore store,
    IPaymentLinkService payments,
    IPaymentConfirmationHandler confirmation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    public async Task<StartTenantProvisioningCheckoutResult> StartAsync(
        StartTenantProvisioningCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var quote = await quotes.QuoteAsync(request.Quote, cancellationToken);
        var tenant = request.Tenant with
        {
            MaximumUsers = checked(quote.FullUserLimit + quote.SellerUserLimit),
            MaximumEnrolledDevices = quote.PosDeviceLimit
        };
        TenantProvisioningRequestValidator.Validate(tenant);
        var identityCatalog = await catalog.GetLegalIdentityCatalogAsync(cancellationToken);
        if (!identityCatalog.EntityTypes.Any(option => option.Code == tenant.EntityType)
            || !identityCatalog.IdentificationTypes.Any(option => option.Code == tenant.IdentificationTypeCode
                && option.EntityTypeCode == tenant.EntityType))
            throw new ArgumentException("Selecciona un tipo de persona y de identificación vigentes.");

        var billingBusinessId = await store.GetBillingBusinessIdAsync(cancellationToken);
        var draftId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var reference = $"TP-{draftId:N}";
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var amountInCents = checked((long)decimal.Round(
            quote.PayableAmountCop * 100m, 0, MidpointRounding.AwayFromZero));
        var widget = await payments.PrepareWidgetCheckoutAsync(new(
            billingBusinessId, reference, amountInCents, "COP", expiresAt, request.RedirectUrl),
            cancellationToken);
        if (!widget.Success || string.IsNullOrWhiteSpace(widget.PublicKey)
            || string.IsNullOrWhiteSpace(widget.IntegritySignature))
            throw new InvalidOperationException(widget.ErrorMessage ?? "No fue posible preparar el pago con Wompi.");

        var accessTokenBytes = RandomNumberGenerator.GetBytes(32);
        var accessToken = Base64UrlEncode(accessTokenBytes);
        var accessTokenHash = SHA256.HashData(accessTokenBytes);
        var snapshot = new TenantProvisioningCheckoutSnapshot(tenant, quote);
        var quoteHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, Json));
        await store.CreateAsync(draftId, paymentTransactionId, accessTokenHash,
            tenant.InvitationEmail.Trim(), snapshot, quoteHash, expiresAt,
            widget.MerchantConfigurationVersion, cancellationToken);

        return new(draftId, accessToken, expiresAt, quote,
            new(widget.PublicKey!, reference, amountInCents, "COP", widget.IntegritySignature!,
                widget.ExpirationTime, widget.RedirectUrl));
    }

    public async Task<TenantProvisioningCheckoutStatusDto> GetStatusAsync(
        Guid draftId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        byte[] tokenBytes;
        try { tokenBytes = Base64UrlDecode(accessToken); }
        catch (FormatException) { throw new UnauthorizedAccessException("El acceso al aprovisionamiento no es válido."); }
        var status = await store.GetStatusAsync(draftId, SHA256.HashData(tokenBytes), cancellationToken);
        return status ?? throw new UnauthorizedAccessException("El acceso al aprovisionamiento no es válido.");
    }

    public async Task<TenantProvisioningCheckoutStatusDto> ConfirmWidgetPaymentAsync(
        Guid draftId,
        string accessToken,
        ConfirmTenantProvisioningWidgetPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
            throw new ArgumentException("La transacción de Wompi es obligatoria.");
        var tokenHash = HashAccessToken(accessToken);
        var expected = await store.GetPaymentForVerificationAsync(draftId, tokenHash, cancellationToken)
            ?? throw new UnauthorizedAccessException("El acceso al aprovisionamiento no es válido.");
        var verified = await payments.VerifyTransactionAsync(
            request.TransactionId.Trim(), expected.BillingBusinessId, cancellationToken,
            expected.MerchantConfigurationVersion);
        if (!verified.IsApproved
            || !string.Equals(verified.Reference, expected.PaymentReference, StringComparison.Ordinal)
            || verified.AmountInCents != expected.AmountInCents)
            throw new InvalidOperationException("Wompi todavía no confirma este pago con el valor y la referencia esperados.");
        var result = await confirmation.HandleAsync(expected.PaymentReference,
            verified.TransactionId ?? request.TransactionId.Trim(), expected.AmountInCents,
            $"[Widget verification {DateTimeOffset.UtcNow:O}]", cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "No fue posible confirmar el pago.");
        return await GetStatusAsync(draftId, accessToken, cancellationToken);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            0 => normalized,
            _ => throw new FormatException("Invalid Base64Url value.")
        };
        return Convert.FromBase64String(normalized);
    }

    private static byte[] HashAccessToken(string accessToken)
    {
        try { return SHA256.HashData(Base64UrlDecode(accessToken)); }
        catch (FormatException) { throw new UnauthorizedAccessException("El acceso al aprovisionamiento no es válido."); }
    }
}
