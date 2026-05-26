using System.Text.Json;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MimosBabySpa.Tests.Agents;

internal static class AgentTestHelpers
{
    private static readonly RoleFactResolver RoleFactResolver = new();

    public static ToolInvocation Invoke(IAgentTool tool, JsonElement args, AgentToolContext ctx) =>
        new()
        {
            Arguments = args,
            Context = ctx,
            ResolvedFacts = RoleFactResolver.Resolve(tool, ctx)
        };

    public static ToolInvocation Invoke(JsonElement args, AgentToolContext ctx) =>
        new()
        {
            Arguments = args,
            Context = ctx,
            ResolvedFacts = EmptyFacts
        };

    public static void SetBookingPack(
        AgentToolContext ctx,
        BookingPolicyParams? bookingPolicy = null,
        Reservation? activeReservation = null,
        PaymentTransaction? activePayment = null) =>
        ctx.SetPackContext(new BookingPackContext
        {
            BookingPolicy = bookingPolicy,
            ActiveReservation = activeReservation,
            ActivePayment = activePayment
        });

    public static AgentHumanMessages DefaultHumanMessages() => new()
    {
        EscalationUserMessage = "Te conecto con un agente humano en un momento.",
        SemanticTriggerLineFormat = "- `{0}`: Úsalo cuando {1}.",
        PaidSlotRescheduleAction = "cuando el cliente confirme nuevo horario, llama assign_paid_slot",
        SemanticTriggers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_frustration"] = "el cliente expresa frustración o enojo",
            ["consecutive_errors"] = "hay 2 o más errores consecutivos sin resolución",
            ["out_of_scope_request"] = "el cliente pide algo fuera del alcance del bot",
            ["explicit_human_request"] = "el cliente pide explícitamente hablar con un humano"
        }
    };

    public static IReadOnlyList<PromptSection> DefaultPromptSections() =>
    [
        new PromptSection
        {
            Id = "persona",
            Order = 10,
            Content = "## ROL\nEres Mimi."
        }
    ];

    public static IReadOnlyList<FactSchemaEntry> MimiFactSchema { get; } =
    [
        new() { Key = "baby_name", Role = "baby.name", Label = "nombre del bebé", Type = "string", Source = "user" },
        new() { Key = "baby_age_months", Role = "baby.age_months", Label = "edad del bebé (meses)", Type = "number", Source = "user", Range = new FactNumericRange { Min = 0, Max = 60 } },
        new() { Key = "service", Role = FactRoles.BookingService, Label = "plan / servicio", Type = "string", Source = "user" },
        new() { Key = "add_ons", Role = FactRoles.BookingAddOns, Label = "complementos", Type = "string", Source = "user" },
        new() { Key = "desired_date", Role = FactRoles.BookingDate, Label = "fecha deseada", Type = "date", Source = "user" },
        new() { Key = "desired_time", Role = FactRoles.BookingTime, Label = "hora deseada", Type = "time", Source = "user" },
        new() { Key = "customer_name", Role = FactRoles.CustomerName, Label = "nombre del cliente", Type = "string", Source = "user" },
        new() { Key = "customer_phone", Role = FactRoles.CustomerPhone, Label = "teléfono del cliente", Type = "phone", Source = "channel" },
        new() { Key = "customer_email", Role = FactRoles.CustomerEmail, Label = "email del cliente", Type = "email", Source = "user" }
    ];

    private static IReadOnlyDictionary<string, string> EmptyFacts { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static AgentConfig MinimalConfig() => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        FactSchema = MimiFactSchema,
        HumanMessages = DefaultHumanMessages(),
        PromptSections = DefaultPromptSections()
    };

    public static AgentToolContext CreateSession(AgentConfig config)
    {
        var conversationId = Guid.NewGuid();
        return new AgentToolContext
        {
            AgentId = config.AgentId,
            BusinessId = config.BusinessId,
            ConversationId = conversationId,
            BusinessToday = DateOnly.FromDateTime(DateTime.UtcNow),
            BusinessNow = DateTimeOffset.UtcNow,
            ChannelPhone = "+573001234567",
            Config = config,
            ConversationState = new ConversationState { ConversationId = conversationId, BusinessId = config.BusinessId },
            Conversation = new Conversation
            {
                ConversationId = conversationId,
                BusinessId = config.BusinessId,
                UserNumber = "+573001234567"
            },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public static void FillBookingFacts(AgentToolContext session)
    {
        session.Facts["service"] = "Plan Marineritos";
        session.Facts["desired_date"] = "2026-05-25";
        session.Facts["desired_time"] = "09:00";
        session.Facts["customer_name"] = "Richard";
        session.Facts["add_ons"] = "Decoración Sencilla";
    }

    public static AgentToolRegistry CreateToolRegistry()
    {
        IAgentTool[] tools =
        [
            new StubTool("create_reservation"),
            new StubTool("prepare_checkout"),
            new StubTool("check_availability"),
            new StubTool("get_service_catalog")
        ];
        return new AgentToolRegistry(tools, NullLogger<AgentToolRegistry>.Instance);
    }

    public static IToolCapabilityGate CreateToolCapabilityGate() =>
        new ToolCapabilityGate(new GuardEvaluator(new ConversationVerificationService()));

    public static IRoleFactResolver CreateRoleFactResolver() => RoleFactResolver;

    private sealed class StubTool(string name) : IAgentTool
    {
        public string Name => name;
        public string Description => name;
        public string ParametersSchema => "{}";

        public Task<string> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default) =>
            Task.FromResult("""{"ok":true,"data":{}}""");
    }
}
