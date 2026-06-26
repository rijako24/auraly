using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Time;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class OperatingHoursTurnPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_WhenOutsideBusinessHours_RemovesGatedToolsFromLlmToolSet()
    {
        var businessId = Guid.NewGuid();
        var policy = new OperatingHoursTurnPolicy(
            WorkingHours(OpenOnTuesdayOnly()),
            NullLogger<OperatingHoursTurnPolicy>.Instance);
        var orderTool = new TestTool("add_order_item", [ToolOperatingGroups.OrderIntake]);
        var readOnlyTool = new TestTool("search_products");

        var result = await policy.EvaluateAsync(
            Config(businessId),
            Clock(businessId, new DateOnly(2026, 6, 22), new TimeOnly(20, 0)),
            [orderTool, readOnlyTool],
            CancellationToken.None);

        result.Context.IsOutsideOperatingHours.Should().BeTrue();
        result.Context.BlockedToolNames.Should().ContainSingle("add_order_item");
        result.EffectiveTools.Select(t => t.Name).Should().ContainSingle("search_products");
    }

    [Fact]
    public async Task EvaluateAsync_WhenAllConfiguredToolsAreGatedOutsideHours_LeavesNoToolsForLlm()
    {
        var businessId = Guid.NewGuid();
        var policy = new OperatingHoursTurnPolicy(
            WorkingHours(OpenOnTuesdayOnly()),
            NullLogger<OperatingHoursTurnPolicy>.Instance);

        var result = await policy.EvaluateAsync(
            Config(businessId),
            Clock(businessId, new DateOnly(2026, 6, 22), new TimeOnly(20, 0)),
            [
                new TestTool("add_order_item", [ToolOperatingGroups.OrderIntake]),
                new TestTool("create_order", [ToolOperatingGroups.OrderIntake])
            ],
            CancellationToken.None);

        result.EffectiveTools.Should().BeEmpty();
        result.Context.BlockedToolNames.Should().BeEquivalentTo("add_order_item", "create_order");
        result.Context.NextOperatingWindowText.Should().Contain("martes");
    }

    [Fact]
    public async Task ResolveAsync_WhenOutsideBusinessHours_ExposesOnlyEffectiveTools()
    {
        var businessId = Guid.NewGuid();
        var registry = new AgentToolRegistry(
            [
                new TestTool("add_order_item", [ToolOperatingGroups.OrderIntake]),
                new TestTool("search_products")
            ],
            NullLogger<AgentToolRegistry>.Instance);
        var policy = new OperatingHoursTurnPolicy(
            WorkingHours(OpenOnTuesdayOnly()),
            NullLogger<OperatingHoursTurnPolicy>.Instance);
        var resolver = new AgentTurnToolResolver(registry, policy);

        var result = await resolver.ResolveAsync(
            Config(businessId, ["add_order_item", "search_products"]),
            Clock(businessId, new DateOnly(2026, 6, 22), new TimeOnly(20, 0)),
            CancellationToken.None);

        result.ConfiguredTools.Select(t => t.Name).Should().BeEquivalentTo("add_order_item", "search_products");
        result.EffectiveTools.Select(t => t.Name).Should().ContainSingle("search_products");
        result.OperatingHours.BlockedToolNames.Should().ContainSingle("add_order_item");
    }

    [Fact]
    public void Compose_WhenOutsideHoursAndToolsAreBlocked_InstructsLlmNotToAdvanceProtectedActions()
    {
        var composer = new AgentPromptComposer(
            new Application.Agents.Composition.FlowStageDetector(),
            new Application.Agents.Composition.GuardEvaluator(new ConversationVerificationService()));
        var session = new AgentToolContext
        {
            OperatingHours = new OperatingHoursTurnContext(
                true,
                true,
                [ToolOperatingGroups.OrderIntake],
                ["add_order_item", "create_order"],
                "martes 23 de junio de 9:00 a. m. a 5:00 p. m.")
        };

        var prompt = composer.Compose(new Application.Agents.Composition.PromptCompositionInput
        {
            Config = Config(Guid.NewGuid()),
            History = [],
            Temporal = new TemporalReferenceBuilder().Build(Clock(Guid.NewGuid(), new DateOnly(2026, 6, 22), new TimeOnly(20, 0))),
            Session = session,
            EnabledTools = []
        });

        prompt.Should().Contain("## HORARIO OPERATIVO DEL TURNO");
        prompt.Should().Contain("No tomes, confirmes ni avances solicitudes");
        prompt.Should().Contain("Proximo horario habil");
    }

    private static AgentConfig Config(Guid businessId, IReadOnlyList<string>? enabledTools = null) => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = businessId,
        Name = "Test",
        Persona = "Eres un agente de prueba.",
        EnabledToolNames = enabledTools ?? [],
        OperatingHours = new OperatingHoursDefinitions
        {
            Enabled = true,
            GatedGroups = [ToolOperatingGroups.OrderIntake]
        }
    };

    private static IWorkingHoursService WorkingHours(Func<DateOnly, IReadOnlyList<TimeBlock>> resolve)
    {
        var service = new Mock<IWorkingHoursService>();
        service
            .Setup(s => s.GetEffectiveBusinessWorkingHoursAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, DateOnly, CancellationToken>((_, date, _) => Task.FromResult(resolve(date)));
        return service.Object;
    }

    private static Func<DateOnly, IReadOnlyList<TimeBlock>> OpenOnTuesdayOnly() =>
        date => date.DayOfWeek == DayOfWeek.Tuesday
            ? [new TimeBlock { Open = "09:00", Close = "17:00" }]
            : [];

    private static BusinessClockSnapshot Clock(Guid businessId, DateOnly today, TimeOnly time)
    {
        var tz = BusinessTimeZoneResolver.Resolve(BusinessClock.DefaultTimeZoneId);
        var local = today.ToDateTime(time);
        return new BusinessClockSnapshot(
            businessId,
            new DateTimeOffset(local, tz.GetUtcOffset(local)),
            today,
            tz);
    }

    private sealed class TestTool : IAgentTool
    {
        public TestTool(string name, IReadOnlyList<string>? operatingGroups = null)
        {
            Name = name;
            OperatingGroups = operatingGroups ?? [];
        }

        public string Name { get; }
        public IReadOnlyList<string> OperatingGroups { get; }
        public string Description => "Test tool";
        public string ParametersSchema => """{"type":"object"}""";

        public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }
}
