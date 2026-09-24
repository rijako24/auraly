using System.Security.Claims;
using Auraly.Api;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Orders;
using Microsoft.AspNetCore.Http;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class OrdersDeviceActorTests
{
    [Fact]
    public void Prepared_device_invoice_actor_contains_sales_permission()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(PosAuthenticationDefaults.DeviceIdClaim, deviceId.ToString("D")),
            new Claim(PosAuthenticationDefaults.TenantIdClaim, tenantId.ToString("D"))
        ], PosAuthenticationDefaults.Scheme));
        context.Request.Headers["X-Auraly-User-Id"] = userId.ToString("D");
        context.Request.Headers["X-Auraly-Business-Id"] = businessId.ToString("D");
        context.Request.Headers["X-Auraly-Work-Session-Id"] = sessionId.ToString("D");

        var actor = context.ToOrderUserActor(sessionId);

        Assert.Contains(OrderPermissionCodes.Invoice, actor.Permissions);
        Assert.Contains(CommercePermissionCodes.SalesCreate, actor.Permissions);
        Assert.Equal(userId, actor.UserId);
        Assert.Equal(sessionId, actor.WorkSessionId);
    }
}
