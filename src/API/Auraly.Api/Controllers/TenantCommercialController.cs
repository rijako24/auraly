using Auraly.Api.Authorization;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/tenant-commercial")]
[Authorize]
public sealed class TenantCommercialController(
    ITenantCommercialQuoteService quotes,
    TenantProvisioningCheckoutService checkout,
    TenantSubscriptionCheckoutService subscriptionCheckout,
    TenantRenewalOrderService renewalOrders,
    ITenantCommercialCatalogStore catalogStore,
    ITenantCommercialSubscriptionStore subscriptions,
    ITenantBillingNotificationStore billingNotifications) : ControllerBase
{
    [HttpGet("catalog")]
    [AllowAnonymous]
    public async Task<ActionResult<TenantCommercialCatalogDto>> Catalog(CancellationToken ct) =>
        Ok(await quotes.GetCatalogAsync(ct));

    [HttpPost("quote")]
    [AllowAnonymous]
    public async Task<ActionResult<TenantQuoteDto>> Quote(TenantQuoteRequest request, CancellationToken ct) =>
        Ok(await quotes.QuoteAsync(request, ct));

    [HttpGet("subscription")]
    [PermissionAuthorize("dashboard.read")]
    public async Task<ActionResult<TenantCommercialSubscriptionDto?>> Subscription(CancellationToken ct) =>
        Ok(await subscriptions.GetAsync(User.GetTenantId(), ct));

    [HttpGet("subscription/renewal-order")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<ActionResult<TenantRenewalOrderDto?>> RenewalOrder(CancellationToken ct) =>
        Ok(await renewalOrders.GetCurrentAsync(User.GetTenantId(), ct));

    [HttpPut("subscription/renewal-order")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<ActionResult<TenantRenewalOrderDto>> ReviseRenewalOrder(
        TenantQuoteRequest request, CancellationToken ct) =>
        Ok(await renewalOrders.ReviseAsync(User.GetTenantId(), User.GetUserId(), request, ct));

    [HttpPost("subscription/renewal-order/checkout")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<ActionResult<StartTenantSubscriptionCheckoutResult>> StartRenewalCheckout(
        StartTenantSubscriptionCheckoutRequest request,
        CancellationToken ct) => Ok(await subscriptionCheckout.StartAsync(
            User.GetTenantId(), request, ct));

    [HttpPost("subscription/renewal-orders/{renewalOrderId:guid}/confirm")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<ActionResult<TenantSubscriptionReceiptDto>> ConfirmRenewalCheckout(
        Guid renewalOrderId,
        ConfirmTenantSubscriptionPaymentRequest request,
        CancellationToken ct) => Ok(await subscriptionCheckout.ConfirmAsync(
            User.GetTenantId(), renewalOrderId, request, ct));

    [HttpGet("subscription/renewal-orders/{renewalOrderId:guid}/receipt")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<ActionResult<TenantSubscriptionReceiptDto>> RenewalReceipt(
        Guid renewalOrderId,
        CancellationToken ct) => Ok(await subscriptionCheckout.GetReceiptAsync(
            User.GetTenantId(), renewalOrderId, ct));

    [HttpGet("subscription/notifications")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<ActionResult<IReadOnlyList<TenantBillingNotificationDto>>> Notifications(
        [FromQuery] int take = 30, CancellationToken ct = default) =>
        Ok(await billingNotifications.GetAsync(User.GetTenantId(), User.GetUserId(), take, ct));

    [HttpPost("subscription/notifications/{notificationId:guid}/read")]
    [PermissionAuthorize("subscription.manage")]
    public async Task<IActionResult> MarkNotificationRead(Guid notificationId, CancellationToken ct)
    {
        await billingNotifications.MarkReadAsync(
            User.GetTenantId(), User.GetUserId(), notificationId, ct);
        return NoContent();
    }

    [HttpGet("geography/countries")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TenantProvisioningGeographyDto>>> Countries(CancellationToken ct) =>
        Ok(await catalogStore.GetCountriesAsync(ct));

    [HttpGet("legal-identity-options")]
    [AllowAnonymous]
    public async Task<ActionResult<TenantProvisioningLegalIdentityCatalogDto>> LegalIdentityOptions(CancellationToken ct) =>
        Ok(await catalogStore.GetLegalIdentityCatalogAsync(ct));

    [HttpGet("geography/countries/{countryId:guid}/divisions")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TenantProvisioningGeographyDto>>> Divisions(Guid countryId, CancellationToken ct) =>
        Ok(await catalogStore.GetDivisionsAsync(countryId, ct));

    [HttpGet("geography/divisions/{divisionId:guid}/cities")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TenantProvisioningGeographyDto>>> Cities(Guid divisionId, CancellationToken ct) =>
        Ok(await catalogStore.GetCitiesAsync(divisionId, ct));

    [HttpPost("provisioning-checkouts")]
    [AllowAnonymous]
    public async Task<ActionResult<StartTenantProvisioningCheckoutResult>> StartCheckout(
        StartTenantProvisioningCheckoutRequest request,
        CancellationToken ct) => Ok(await checkout.StartAsync(request, ct));

    [HttpGet("provisioning-checkouts/{draftId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<TenantProvisioningCheckoutStatusDto>> CheckoutStatus(
        Guid draftId,
        [FromHeader(Name = "X-Provisioning-Token")] string accessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return Unauthorized();
        try { return Ok(await checkout.GetStatusAsync(draftId, accessToken, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [HttpPost("provisioning-checkouts/{draftId:guid}/confirm")]
    [AllowAnonymous]
    public async Task<ActionResult<TenantProvisioningCheckoutStatusDto>> ConfirmCheckout(
        Guid draftId,
        [FromHeader(Name = "X-Provisioning-Token")] string accessToken,
        ConfirmTenantProvisioningWidgetPaymentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return Unauthorized();
        try { return Ok(await checkout.ConfirmWidgetPaymentAsync(draftId, accessToken, request, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
}
