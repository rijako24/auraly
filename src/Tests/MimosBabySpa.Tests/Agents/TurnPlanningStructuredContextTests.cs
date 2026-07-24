using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class TurnPlanningStructuredContextTests
{
    [Fact]
    public async Task CommerceEnricher_ProvidesOnlyCurrentItemNamesAndQuantities()
    {
        var commerce = new StubCommerceService(Snapshot(
            new OrderItemSnapshot(Guid.NewGuid(), Guid.NewGuid(), "external", "sku", "PECHUGA MAC POLLO", 2, 13001.08m, 26002.16m)));
        var enricher = new CommerceCartPlanningContextEnricher(commerce);
        var config = new AgentConfig { Commerce = new CommerceConfig { Enabled = true } };
        var context = new OperationContext
        {
            Session = new AgentConversationContext(),
            Config = config,
            ConversationState = new ConversationState()
        };

        var fragment = await enricher.EnrichAsync(config, context);

        fragment.Should().NotBeNull();
        fragment!.Key.Should().Be("currentCart");
        var item = fragment.Value.GetProperty("items")[0];
        item.GetProperty("name").GetString().Should().Be("PECHUGA MAC POLLO");
        item.GetProperty("quantity").GetDecimal().Should().Be(2);
        item.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("name", "quantity");
    }

    [Fact]
    public async Task CommerceEnricher_DoesNotQueryCommerceWhenDisabled()
    {
        var commerce = new StubCommerceService(Snapshot());
        var enricher = new CommerceCartPlanningContextEnricher(commerce);
        var config = new AgentConfig { Commerce = new CommerceConfig { Enabled = false } };

        var fragment = await enricher.EnrichAsync(config, new OperationContext
        {
            Session = new AgentConversationContext(),
            Config = config,
            ConversationState = new ConversationState()
        });

        fragment.Should().BeNull();
        commerce.GetDraftCalls.Should().Be(0);
    }

    [Fact]
    public async Task CommerceEnricher_WhenDraftReadFails_FailsClosed()
    {
        var commerce = new StubCommerceService(null, throwOnGet: true);
        var enricher = new CommerceCartPlanningContextEnricher(commerce);
        var config = new AgentConfig { Commerce = new CommerceConfig { Enabled = true } };

        var action = () => enricher.EnrichAsync(config, new OperationContext
        {
            Session = new AgentConversationContext(),
            Config = config,
            ConversationState = new ConversationState()
        });

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("draft unavailable");
        commerce.GetDraftCalls.Should().Be(1);
    }
    [Fact]
    public async Task Planner_IncludesStructuredCartInSingleExtractorPrompt()
    {
        const string response = """
            {"flowIntent":{"candidateFlow":"order","confidence":0.95,"evidence":null},"facts":[],"signals":[],"decision":null,"response":{"mode":"continue","ambiguousFields":[]}}
            """;
        var chat = new CapturingChatClient(response);
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());
        var stage = new AgentFlowStage { Id = "selection" };
        var config = new AgentConfig
        {
            Flows = [new AgentFlowDefinition { Id = "order", Type = FlowTypes.Primary, Stages = [stage] }]
        };
        var structuredContext = new Dictionary<string, JsonElement>
        {
            ["currentCart"] = JsonSerializer.SerializeToElement(new
            {
                items = new[] { new { name = "PECHUGA MAC POLLO", quantity = 2 } }
            })
        };

        var proposal = await planner.PlanAsync(new TurnPlanningContext(
            config,
            stage,
            TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>(), "order"),
            new Dictionary<string, string>(),
            "dejame 3 de mac pollo",
            DateTimeOffset.Parse("2026-07-11T10:00:00-05:00"),
            [],
            structuredContext));

        proposal.Success.Should().BeTrue();
        chat.CallCount.Should().Be(1);
        chat.Messages.Should().ContainSingle();
        chat.Messages[0].Content.Should().Contain("structuredContext");
        chat.Messages[0].Content.Should().Contain("PECHUGA MAC POLLO");
        chat.Messages[0].Content.Should().Contain("\"quantity\":2");
    }

    private static OrderSnapshot Snapshot(params OrderItemSnapshot[] items) => new(
        Guid.NewGuid(), OrderStatus.Draft, "COP", 0, 0, 0, 0, items);

    private sealed class CapturingChatClient(string response) : IChatClient
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Messages = messages;
            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = response,
                AssistantMessage = ChatMessage.Assistant(response)
            });
        }
    }

    private sealed class StubCommerceService(OrderSnapshot? snapshot, bool throwOnGet = false) : ICommerceService
    {
        public int GetDraftCalls { get; private set; }

        public Task<OrderSnapshot> GetDraftAsync(AgentConversationContext ctx, CancellationToken ct = default)
        {
            GetDraftCalls++;
            if (throwOnGet)
                throw new InvalidOperationException("draft unavailable");
            return Task.FromResult(snapshot!);
        }

        public Task<ProductSearchResult> SearchProductsAsync(AgentConversationContext ctx, ProductSearchRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProductCategoryPage> BrowseCategoriesAsync(AgentConversationContext ctx, int page, int pageSize, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> ResolveCategoryNameAsync(AgentConversationContext ctx, string name, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<OrderSnapshot> AddItemAsync(AgentConversationContext ctx, AddOrderItemRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderSnapshot> RemoveItemAsync(AgentConversationContext ctx, Guid orderItemId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderSnapshot> UpdateItemQuantityAsync(AgentConversationContext ctx, Guid orderItemId, decimal quantity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> DiscardDraftsAsync(Guid businessId, Guid conversationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderSnapshot> CreateOrderAsync(AgentConversationContext ctx, CreateOrderRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrderSnapshot> ConfirmPaidOrderAsync(Guid businessId, Guid paymentTransactionId, AgentConfig config, CancellationToken ct = default) => throw new NotSupportedException();
    }
}