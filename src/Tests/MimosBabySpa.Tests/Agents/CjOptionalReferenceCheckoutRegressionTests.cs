using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CjOptionalReferenceCheckoutRegressionTests
{
    [Fact]
    public async Task DecliningOptionalReference_WithRequiredData_ProceedsToOfficialCheckoutDeterministically()
    {
        var first = await ExecuteScenarioAsync();
        var second = await ExecuteScenarioAsync();

        AssertCompletedCheckout(first.Result, first.CheckoutCallCount);
        AssertCompletedCheckout(second.Result, second.CheckoutCallCount);

        Project(second.Result).Should().BeEquivalentTo(Project(first.Result), options => options.WithStrictOrdering());
    }

    private static async Task<ScenarioResult> ExecuteScenarioAsync()
    {
        var config = LoadCjConfig();
        var checkout = new ObservableCheckoutOperation();
        var coordinator = new DeterministicTurnCoordinator(
            new NoMutationPlanner(),
            new DeterministicFlowSelector(),
            new FactMutationBatchProcessor(),
            new RecordingFactStore(),
            new ConversationVerificationService(),
            new DeterministicStageExecutor(
                new AgentOperationRegistry([checkout]),
                new StageConditionEvaluator(),
                new OperationArgumentBinder()),
            new DeterministicStageTransitionResolver(new StageConditionEvaluator()));
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_finalized"] = "true",
            ["cart_review_confirmed"] = "true",
            ["delivery_method"] = "domicilio",
            ["city"] = "Valledupar",
            ["delivery_address"] = "Calle 5N",
            ["delivery_phone"] = "3012926660",
            ["customer_name"] = "Richard",
            ["payment_method"] = "efectivo"
        };
        var conversationId = Guid.Parse("0c83d9d0-cce7-47ad-9e66-c2cfbeeb9f2b");

        var result = await coordinator.ExecuteAsync(new DeterministicTurnRequest
        {
            Config = config,
            OperationContext = new OperationContext
            {
                AgentId = config.AgentId,
                BusinessId = config.BusinessId,
                ConversationId = conversationId,
                BusinessToday = new DateOnly(2026, 7, 14),
                BusinessNow = DateTimeOffset.Parse("2026-07-14T10:00:00-05:00"),
                Config = config,
                ConversationState = new ConversationState()
            },
            CurrentFacts = facts,
            FactVersions = facts.ToDictionary(pair => pair.Key, _ => 1L, StringComparer.OrdinalIgnoreCase),
            CurrentFlowId = "order",
            ActiveFlowId = "order",
            CurrentStageId = "order_data",
            HasOpenPrimaryRequest = true,
            LatestUserMessage = "no"
        });

        return new ScenarioResult(result, checkout.CallCount);
    }

    private static void AssertCompletedCheckout(DeterministicTurnResult result, int checkoutCallCount)
    {
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.VisitedStages.Should().ContainInOrder("order_data", "payment_method", "summary", "order_confirmation");
        result.CurrentStageId.Should().Be("order_confirmation");
        checkoutCallCount.Should().Be(1);
        result.Trace.Should().ContainSingle(trace =>
            trace.ActionId == "prepare_order_checkout"
            && trace.OperationId == "commerce.prepare_checkout"
            && trace.Success
            && trace.OutcomeCode == "order.checkout_ready");
        result.Presentations.Should().ContainSingle(presentation =>
            presentation.TemplateId == "order_checkout_no_payment"
            && presentation.Mode == FragmentRenderMode.Exclusive
            && presentation.Priority == FragmentPriority.Required);
        result.Facts.Should().Contain("order_checkout_presented", "true");
        result.Facts.Should().Contain("delivery_address", "Calle 5N");
        result.Facts.Should().Contain("delivery_phone", "3012926660");
        result.Facts.Should().NotContainKey("delivery_reference");
    }

    private static object Project(DeterministicTurnResult result) => new
    {
        result.Success,
        result.CurrentStageId,
        VisitedStages = result.VisitedStages.ToArray(),
        Facts = result.Facts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
        Trace = result.Trace.Select(trace => new
        {
            trace.ActionId,
            trace.OperationId,
            trace.OutcomeCode,
            trace.Success
        }).ToArray(),
        Presentations = result.Presentations.Select(presentation => new
        {
            presentation.TemplateId,
            presentation.Mode,
            presentation.Priority
        }).ToArray(),
        result.RequestCompleted,
        result.EscalateToHuman
    };

    private static AgentConfig LoadCjConfig()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedCJDistribuciones.sql");
        var sql = File.ReadAllText(path);
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the CJ seed must declare @SettingsJson");
        var settingsJson = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);

        var config = JsonSerializer.Deserialize<AgentConfig>(settingsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
        });
        config.Should().NotBeNull();
        return config!;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MimosBabySpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MimosBabySpa.sln.");
    }

    private sealed class NoMutationPlanner : ITurnPlanner
    {
        public Task<TurnPlanProposal> PlanAsync(TurnPlanningContext context, CancellationToken ct = default) =>
            Task.FromResult(new TurnPlanProposal(
                true,
                new TurnPlan
                {
                    FlowIntent = new PlannedFlowIntent
                    {
                        CandidateFlow = "order",
                        Confidence = 1
                    }
                },
                [],
                0,
                0));
    }

    private sealed class ObservableCheckoutOperation : IAgentOperation
    {
        public int CallCount { get; private set; }

        public OperationDescriptor Descriptor { get; } = new(
            "commerce.prepare_checkout",
            "{\"type\":\"object\",\"required\":[]}",
            ["order.checkout_ready"],
            [],
            ["order_checkout_no_payment"],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(OperationOutcome.Ok(
                "order.checkout_ready",
                new { total = 86370m },
                [
                    new OperationPresentation(
                        "order_checkout_no_payment",
                        new Dictionary<string, object?>
                        {
                            ["customer_name"] = "Richard",
                            ["delivery_address"] = "Calle 5N",
                            ["delivery_phone"] = "3012926660",
                            ["total"] = 86370m
                        },
                        FragmentRenderMode.Exclusive,
                        FragmentPriority.Required)
                ]));
        }
    }

    private sealed class RecordingFactStore : IConversationFactsService
    {
        public Task ApplyBatchAsync(
            Guid conversationId,
            Guid businessId,
            IReadOnlyDictionary<string, string?> mutations,
            IReadOnlySet<string> rememberAcrossRequests,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ConversationFactRecord>> GetAllRecordsAsync(Guid conversationId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid conversationId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> GetAsync(Guid conversationId, string key, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetAsync(Guid conversationId, Guid businessId, string key, string value, bool rememberAcrossRequests = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ClearNonPersistentAsync(Guid conversationId, IReadOnlyCollection<string> persistentKeys, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> ClearFieldsAsync(Guid conversationId, IReadOnlyCollection<string> fields, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed record ScenarioResult(DeterministicTurnResult Result, int CheckoutCallCount);
}
