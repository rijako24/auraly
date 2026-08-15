using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Configuration;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Services;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class WhatsAppChannelAdminServiceTests
{
    [Fact]
    public async Task Create_DoesNotReturnAccessToken_AndPersistsAgentAssignment()
    {
        await using var db = CreateDb();
        var (tenantId, businessId, agentId) = await SeedAsync(db);
        var service = CreateService(db, new QueueHandler());

        var result = await service.CreateAsync(tenantId, false, businessId,
            new CreateWhatsAppChannelRequest(agentId, "+573001112233", "phone-1", "waba-1", "secret-token"));

        result.AgentId.Should().Be(agentId);
        result.AgentName.Should().Be("Agente comercial");
        result.HasAccessToken.Should().BeTrue();
        result.ToString().Should().NotContain("secret-token");
        (await db.BusinessWhatsAppNumbers.SingleAsync()).WhatsAppAccessToken.Should().Be("secret-token");
    }

    [Fact]
    public async Task Validate_ConfirmsPhoneAndBusinessAccountWithMeta()
    {
        await using var db = CreateDb();
        var (tenantId, businessId, agentId) = await SeedAsync(db);
        var handler = new QueueHandler(
            """{"id":"phone-1","display_phone_number":"+57 300 111 2233","verified_name":"Auraly Demo","quality_rating":"GREEN"}""",
            """{"id":"waba-1","name":"Auraly WABA"}""");
        var service = CreateService(db, handler);
        var channel = await service.CreateAsync(tenantId, false, businessId,
            new CreateWhatsAppChannelRequest(agentId, "+573001112233", "phone-1", "waba-1", "secret-token"));

        var status = await service.ValidateAsync(tenantId, false, businessId, channel.BusinessWhatsAppNumberId);

        status.IsConnected.Should().BeTrue();
        status.VerifiedName.Should().Be("Auraly Demo");
        status.BusinessAccountName.Should().Be("Auraly WABA");
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().OnlyContain(authorization => authorization != null && authorization.Scheme == "Bearer");
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(Guid TenantId, Guid BusinessId, Guid AgentId)> SeedAsync(ApplicationDbContext db)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        db.Businesses.Add(new Business { BusinessId = businessId, TenantId = tenantId, Name = "Demo" });
        db.Agents.Add(new Agent { AgentId = agentId, BusinessId = businessId, AgentTypeId = Guid.NewGuid(), Name = "Agente comercial" });
        await db.SaveChangesAsync();
        return (tenantId, businessId, agentId);
    }

    private static WhatsAppChannelAdminService CreateService(ApplicationDbContext db, HttpMessageHandler handler)
    {
        return new WhatsAppChannelAdminService(db, new HttpClient(handler),
            Options.Create(new WhatsAppWebhookOptions { ApiBaseUrl = "https://graph.facebook.test/v25.0/" }));
    }

    private sealed class QueueHandler(params string[] bodies) : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new(bodies);
        public List<System.Net.Http.Headers.AuthenticationHeaderValue?> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Headers.Authorization);
            var body = _bodies.Count > 0 ? _bodies.Dequeue() : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
