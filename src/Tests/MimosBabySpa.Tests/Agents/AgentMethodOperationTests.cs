using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Tools;

using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentMethodOperationTests
{
    [Theory]
    [InlineData("{\"payment_pending_manual_confirmation\":true}", "order.checkout_pending_manual_payment")]
    [InlineData("{\"payment_required\":true}", "order.checkout_payment_required")]
    [InlineData("{}", "order.checkout_ready")]
    public async Task PrepareCheckout_MapsAuthoritativeOutcomeCode(string data, string expectedCode)
    {
        var operation = BuildOperation($$"""{"ok":true,"data":{{data}}}""");

        var outcome = await operation.ExecuteAsync(Json("{}"), Context());

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task MethodEffects_AreTranslatedAndSessionTurnIsRestored()
    {
        var operation = BuildOperation("""
            {"ok":true,"data":{},"effects":["request_completed","escalated_to_human"],"events":["order.created"]}
            """);
        var context = Context();

        var outcome = await operation.ExecuteAsync(Json("{}"), context);

        outcome.Effects.Should().ContainSingle(effect => effect is CompleteRequestOperationEffect);
        outcome.Effects.Should().ContainSingle(effect => effect is EscalateHumanOperationEffect);
        outcome.Events.Should().Equal("order.created");
    }

    private static AgentMethodOperation BuildOperation(string result)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentTool>(new StubMethod(result));
        services.AddSingleton(provider => new AgentToolRegistry(
            provider.GetServices<IAgentTool>(),
            NullLogger<AgentToolRegistry>.Instance));
        var provider = services.BuildServiceProvider();
        return new AgentMethodOperation(
            provider,
            "prepare_order_checkout",
            "commerce.prepare_checkout",
            "order.checkout_prepared",
            ["order.checkout_ready", "order.checkout_payment_required", "order.checkout_pending_manual_payment"],
            "{\"type\":\"object\",\"properties\":{},\"required\":[]}");
    }

    private static OperationContext Context()
    {
        var config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ConsecutiveErrorEscalationThreshold = 3
        };
        var state = new ConversationState();
        var session = new AgentToolContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = Guid.NewGuid(),
            Config = config,
            ConversationState = state,
            Conversation = new Conversation(),
            Facts = []
        };
        return new OperationContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = session.ConversationId,
            Config = config,
            ConversationState = state,
            Session = session
        };
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed class StubMethod(string result) : IAgentTool
    {
        public string Name => "prepare_order_checkout";
        public string Description => string.Empty;
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(
            JsonElement arguments,
            AgentToolContext ctx,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
