using System.Text.Json;
using FluentAssertions;
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
    public async Task EvaluateAsync_WhenEnforcementIsDisabled_ReturnsDisabledContext()
    {
        var businessId = Guid.NewGuid();
        var policy = new OperatingHoursTurnPolicy(WorkingHours(OpenOnTuesdayOnly()));

        var result = await policy.EvaluateAsync(
            Config(businessId, enforceHours: false),
            Clock(businessId, new DateOnly(2026, 6, 22), new TimeOnly(20, 0)),
            CancellationToken.None);

        result.IsEnforced.Should().BeFalse();
        result.IsOutsideOperatingHours.Should().BeFalse();
        result.NextOperatingWindowText.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WhenOutsideBusinessHours_ReturnsClosedContext()
    {
        var businessId = Guid.NewGuid();
        var policy = new OperatingHoursTurnPolicy(WorkingHours(OpenOnTuesdayOnly()));

        var result = await policy.EvaluateAsync(
            Config(businessId),
            Clock(businessId, new DateOnly(2026, 6, 22), new TimeOnly(20, 0)),
            CancellationToken.None);

        result.IsEnforced.Should().BeTrue();
        result.IsOutsideOperatingHours.Should().BeTrue();
        result.NextOperatingWindowText.Should().Contain("martes");
    }

    [Fact]
    public async Task EvaluateAsync_WhenNextWindowIsToday_FormatsNextWindowWithoutDate()
    {
        var businessId = Guid.NewGuid();
        var today = new DateOnly(2026, 6, 26);
        var policy = new OperatingHoursTurnPolicy(WorkingHours(OpenOn(today, "13:00", "21:00")));

        var result = await policy.EvaluateAsync(
            Config(businessId),
            Clock(businessId, today, new TimeOnly(12, 0)),
            CancellationToken.None);

        result.IsOutsideOperatingHours.Should().BeTrue();
        result.NextOperatingWindowText.Should().Be("hoy de 1:00 p. m. a 9:00 p. m.");
        result.NextOperatingWindowText.Should().NotContain("viernes");
        result.NextOperatingWindowText.Should().NotContain("26 de junio");
    }

    [Fact]
    public async Task EvaluateAsync_WhenBusinessIsOpen_ReturnsOpenContext()
    {
        var businessId = Guid.NewGuid();
        var policy = new OperatingHoursTurnPolicy(WorkingHours(OpenOnTuesdayOnly()));

        var result = await policy.EvaluateAsync(
            Config(businessId),
            Clock(businessId, new DateOnly(2026, 6, 23), new TimeOnly(10, 0)),
            CancellationToken.None);

        result.IsEnforced.Should().BeTrue();
        result.IsOutsideOperatingHours.Should().BeFalse();
        result.NextOperatingWindowText.Should().Be("hoy de 9:00 a. m. a 5:00 p. m.");
    }

    [Fact]
    public async Task ResolveAsync_WhenOutsideBusinessHours_ExposesNoEffectiveTools()
    {
        var businessId = Guid.NewGuid();
        var registry = new AgentToolRegistry(
            [new TestTool("search_products"), new TestTool("set_fact")],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentToolRegistry>.Instance);
        var policy = new OperatingHoursTurnPolicy(WorkingHours(OpenOnTuesdayOnly()));
        var resolver = new AgentTurnToolResolver(registry, policy);

        var result = await resolver.ResolveAsync(
            Config(businessId, enabledTools: ["search_products", "set_fact"]),
            Clock(businessId, new DateOnly(2026, 6, 22), new TimeOnly(20, 0)),
            CancellationToken.None);

        result.ConfiguredTools.Select(t => t.Name).Should().BeEquivalentTo("search_products", "set_fact");
        result.EffectiveTools.Should().BeEmpty();
        result.OperatingHours.IsOutsideOperatingHours.Should().BeTrue();
    }

    [Fact]
    public void Compose_WhenOutsideHours_ReturnsOnlyClosedBusinessPrompt()
    {
        var composer = new AgentPromptComposer(
            new Application.Agents.Composition.FlowStageDetector(),
            new Application.Agents.Composition.GuardEvaluator(new ConversationVerificationService()));
        var session = new AgentToolContext
        {
            OperatingHours = new OperatingHoursTurnContext(
                true,
                true,
                "hoy de 1:00 p. m. a 9:00 p. m.")
        };

        var prompt = composer.Compose(new Application.Agents.Composition.PromptCompositionInput
        {
            Config = Config(Guid.NewGuid()),
            History = [],
            Temporal = new TemporalReferenceBuilder().Build(Clock(Guid.NewGuid(), new DateOnly(2026, 6, 22), new TimeOnly(20, 0))),
            Session = session,
            EnabledTools = [new TestTool("search_products")]
        });

        prompt.Should().Contain("## DISPONIBILIDAD ACTUAL");
        prompt.Should().Contain("fuera de horario laboral");
        prompt.Should().Contain("proximo_horario_habil: hoy de 1:00 p. m. a 9:00 p. m.");
        prompt.Should().Contain("no repitas literalmente la misma plantilla");
        prompt.Should().Contain("agradece el contacto");
        prompt.Should().Contain("Si empieza por hoy, no agregues fecha ni dia");
        prompt.Should().Contain("Eres un agente de prueba.");
        prompt.Should().Contain("gestiones operativas");
        prompt.Should().Contain("No solicites datos");
        prompt.Should().Contain("no termines con preguntas");
        prompt.Should().NotContain("comprar");
        prompt.Should().NotContain("agendar");
        prompt.Should().NotContain("## ETAPA ACTUAL");
        prompt.Should().NotContain("## ACCIONES DISPONIBLES");
        prompt.Should().NotContain("search_products");
    }

    private static AgentConfig Config(
        Guid businessId,
        IReadOnlyList<string>? enabledTools = null,
        bool enforceHours = true) => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = businessId,
        Name = "Test",
        Persona = "Eres un agente de prueba.",
        EnabledToolNames = enabledTools ?? [],
        OperatingHours = new OperatingHoursDefinitions
        {
            Enforce = enforceHours
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

    private static Func<DateOnly, IReadOnlyList<TimeBlock>> OpenOn(DateOnly openDate, string open, string close) =>
        date => date == openDate
            ? [new TimeBlock { Open = open, Close = close }]
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
        public TestTool(string name) => Name = name;

        public string Name { get; }
        public string Description => "Test tool";
        public string ParametersSchema => """{"type":"object"}""";

        public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }
}
