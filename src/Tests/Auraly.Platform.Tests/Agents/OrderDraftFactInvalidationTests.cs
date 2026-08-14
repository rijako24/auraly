using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations.Support;
using Auraly.Platform.Application.Services;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class OrderDraftFactInvalidationTests
{
    [Fact]
    public async Task ClearOrderFinalizedAsync_UsesConfiguredFactRoleKey()
    {
        var factsService = new RecordingFactsService();
        var ctx = new AgentConversationContext
        {
            ConversationId = Guid.NewGuid(),
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "checkout_done", Role = "order.finalized", Source = "user" }
                ]
            },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["checkout_done"] = "true"
            }
        };

        await OrderDraftFactInvalidation.ClearOrderFinalizedAsync(factsService, ctx, CancellationToken.None);

        factsService.LastCleared.Should().Equal("checkout_done");
        ctx.Facts.Should().NotContainKey("checkout_done");
    }

    [Fact]
    public async Task ClearOrderFinalizedAsync_WhenRoleIsNotConfigured_DoesNotClearAnything()
    {
        var factsService = new RecordingFactsService();
        var ctx = new AgentConversationContext
        {
            ConversationId = Guid.NewGuid(),
            Config = new AgentConfig(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["order_finalized"] = "true"
            }
        };

        await OrderDraftFactInvalidation.ClearOrderFinalizedAsync(factsService, ctx, CancellationToken.None);

        factsService.LastCleared.Should().BeEmpty();
        ctx.Facts.Should().ContainKey("order_finalized");
    }

    private sealed class RecordingFactsService : IConversationFactsService
    {
        public IReadOnlyList<string> LastCleared { get; private set; } = [];

        public Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(
            Guid conversationId,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            Guid conversationId,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<string?> GetAsync(
            Guid conversationId,
            string key,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task SetAsync(
            Guid conversationId,
            Guid businessId,
            string key,
            string value,
            bool rememberAcrossRequests = false,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task ApplyBatchAsync(
            Guid conversationId,
            Guid businessId,
            IReadOnlyDictionary<string, string?> mutations,
            IReadOnlySet<string> rememberAcrossRequests,
            CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ClearNonPersistentAsync(
            Guid conversationId,
            IReadOnlyCollection<string> persistentKeys,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<string>> ClearFieldsAsync(
            Guid conversationId,
            IReadOnlyCollection<string> fields,
            CancellationToken ct = default)
        {
            LastCleared = fields.ToArray();
            return Task.FromResult(LastCleared);
        }
    }
}
