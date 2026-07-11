using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.StateManagement;

public sealed class ConversationStateManagerTests
{
    [Fact]
    public async Task DeterministicRuntimeState_RoundTripsAcrossManagerInstances()
    {
        var repository = new InMemoryStateRepository();
        var manager = new ConversationStateManager(
            NullLogger<ConversationStateManager>.Instance,
            repository);
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var state = new ConversationState
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            ActiveFlowId = "order",
            ActiveStageId = "cart_review",
            FactVersions = new Dictionary<string, long> { ["order_finalized"] = 4 },
            PendingTurnPlan = new PendingTurnPlan
            {
                ConfigurationSignature = "signature",
                FlowId = "order",
                StageId = "product_selection",
                PlanJson = "{}",
                AmbiguousFields = ["delivery_address"],
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            },
            RequestGeneration = 3,
            ExecutedOperationKeys = new Dictionary<string, DateTime>
            {
                ["3:cart_review:load"] = DateTime.UtcNow
            },
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await manager.SaveStateAsync(conversationId, state);
        var reloaded = await new ConversationStateManager(
                NullLogger<ConversationStateManager>.Instance,
                repository)
            .GetOrCreateStateAsync(conversationId, businessId, "+573001234567");

        reloaded.ActiveFlowId.Should().Be("order");
        reloaded.ActiveStageId.Should().Be("cart_review");
        reloaded.FactVersions["order_finalized"].Should().Be(4);
        reloaded.PendingTurnPlan.Should().NotBeNull();
        reloaded.PendingTurnPlan!.AmbiguousFields.Should().Equal("delivery_address");
        reloaded.RequestGeneration.Should().Be(3);
        reloaded.ExecutedOperationKeys.Should().ContainKey("3:cart_review:load");
        repository.Entity!.RuntimeStateJson.Should().Contain("cart_review");
    }

    [Fact]
    public async Task SaveStateAsync_RejectsAStaleCheckpointVersion()
    {
        var repository = new InMemoryStateRepository();
        var manager = new ConversationStateManager(
            NullLogger<ConversationStateManager>.Instance,
            repository);
        var conversationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var stale = await manager.GetOrCreateStateAsync(
            conversationId, businessId, "+573001234567");

        repository.Entity!.Version = stale.Version + 1;

        var act = () => manager.SaveStateAsync(conversationId, stale);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*state conflict*");
    }
    private sealed class InMemoryStateRepository : IConversationStateRepository
    {
        public ConversationStateEntity? Entity { get; private set; }

        public Task<ConversationStateEntity?> GetByConversationIdAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Entity?.ConversationId == conversationId ? Entity : null);

        public Task SaveAsync(
            ConversationStateEntity entity,
            CancellationToken cancellationToken = default)
        {
            Entity = entity;
            return Task.CompletedTask;
        }
    }
}