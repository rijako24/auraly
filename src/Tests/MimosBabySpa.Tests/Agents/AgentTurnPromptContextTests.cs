using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class AgentTurnPromptContextTests
{
    private static readonly AgentConfig DefaultConfig = new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        Name = "Mimo",
        SystemPrompt = "## ROL\nEres Mimo.",
        FirstTurnGreetingHint = "¡Hola! Soy Mimo de Mimo's Baby Spa."
    };

    private static readonly TemporalReferenceContext DefaultTemporal = new TemporalReferenceBuilder()
        .Build(CreateSnapshot(new DateOnly(2026, 5, 21), new TimeOnly(9, 30)));

    private static BusinessClockSnapshot CreateSnapshot(DateOnly today, TimeOnly time)
    {
        var tz = BusinessTimeZoneResolver.Resolve(BusinessClock.DefaultTimeZoneId);
        var local = today.ToDateTime(time);
        return new BusinessClockSnapshot(
            Guid.NewGuid(),
            new DateTimeOffset(local, tz.GetUtcOffset(local)),
            today,
            tz);
    }

    [Fact]
    public void AppendTurnContext_WhenHistoryIsEmpty_InjectsFirstTurnInstructions()
    {
        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal);

        result.Should().Contain("## CONTEXTO TEMPORAL");
        result.Should().Contain("2026-05-21");
        result.Should().Contain("mañana → 2026-05-22");
        result.Should().Contain("## CONTEXTO DE ESTE TURNO");
        result.Should().Contain("primer mensaje");
        result.Should().Contain(DefaultConfig.FirstTurnGreetingHint);
        result.Should().Contain(DefaultConfig.SystemPrompt);
    }

    [Fact]
    public void AppendTurnContext_WhenOnlyUserMessages_InjectsFirstTurnInstructions()
    {
        var history = new[] { new Message { Sender = "user", MessageText = "hola" } };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, history, DefaultTemporal);

        result.Should().Contain("primer mensaje");
        result.Should().Contain("CONTEXTO TEMPORAL");
    }

    [Theory]
    [InlineData("bot")]
    [InlineData("assistant")]
    [InlineData("Bot")]
    public void AppendTurnContext_WhenBotAlreadyReplied_InjectsNoRepeatGreetingRule(string sender)
    {
        var history = new[]
        {
            new Message { Sender = "user", MessageText = "hola" },
            new Message { Sender = sender, MessageText = "¡Hola! Soy Mimo." }
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, history, DefaultTemporal);

        result.Should().Contain("NO repitas saludo completo");
        result.Should().NotContain("Plantilla sugerida");
        result.Should().Contain("CONTEXTO TEMPORAL");
    }

    [Fact]
    public void AppendTurnContext_WithFacts_RendersSessionState()
    {
        var session = new AgentToolContext
        {
            Conversation = new Conversation { CustomerName = "Ana" },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["baby_age_months"] = "5",
                [ConversationFactKeys.Service] = "Plan Marineritos",
                [ConversationFactKeys.AddOns] = "Decoración Sencilla"
            },
            ActiveReservation = new Reservation
            {
                Service = new Service { ServiceName = "Plan Marineritos" }
            }
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal, session);

        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("baby_age_months: 5");
        result.Should().Contain("servicio: Plan Marineritos");
        result.Should().Contain("add_ons: Decoración Sencilla");
        result.Should().Contain("cliente: Ana");
    }

    [Fact]
    public void AppendTurnContext_WithServiceFact_DoesNotShowAddOnOfferBlock()
    {
        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ConversationFactKeys.Service] = "Plan Marineritos"
            }
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal, session);

        result.Should().Contain("servicio: Plan Marineritos");
        result.Should().NotContain("Complementos disponibles");
    }

    [Fact]
    public void AppendTurnContext_WithBookingPolicy_ShowsDepositRequired()
    {
        var policy = new BookingPolicyParams
        {
            DepositRequired = true,
            DepositPercentage = 50
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal,
            new AgentToolContext { Conversation = new Conversation(), Facts = [] }, policy);

        result.Should().Contain("anticipo: requerido (50%");
    }

    [Fact]
    public void AppendTurnContext_WithBookingPolicyWithoutDeposit_ShowsExplicitNoDeposit()
    {
        var policy = new BookingPolicyParams { DepositRequired = false };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal,
            new AgentToolContext { Conversation = new Conversation(), Facts = [] }, policy);

        result.Should().Contain("anticipo: no requerido");
        result.Should().NotContain("pago:");
    }

    [Fact]
    public void AppendTurnContext_WithPendingPayment_ShowsGranularPaymentState()
    {
        var policy = new BookingPolicyParams { DepositRequired = true, DepositPercentage = 50 };
        var payment = new PaymentTransaction
        {
            Status = PaymentTransactionStatus.Created,
            AmountInCents = 6750000,
            Currency = "COP",
            ExpiresAt = DateTime.UtcNow.AddMinutes(45),
            LinkUrl = "https://pay.example/link"
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal,
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            policy,
            latestPayment: payment);

        result.Should().Contain("pago: link generado");
        result.Should().Contain("COP $67,500");
    }

    [Fact]
    public void AppendTurnContext_WithConfirmedPayment_ShowsConfirmedState()
    {
        var policy = new BookingPolicyParams { DepositRequired = true, DepositPercentage = 50 };
        var payment = new PaymentTransaction
        {
            Status = PaymentTransactionStatus.Confirmed,
            AmountInCents = 6750000,
            Currency = "COP"
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal,
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            policy,
            latestPayment: payment);

        result.Should().Contain("pago: confirmado");
    }

    [Fact]
    public void AppendTurnContext_WithoutFacts_ShowsEmptyPlaceholders()
    {
        var result = AgentTurnPromptContext.AppendTurnContext(
            DefaultConfig.SystemPrompt, DefaultConfig, [], DefaultTemporal,
            new AgentToolContext { Conversation = new Conversation(), Facts = [] });

        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("cliente: —");
        result.Should().NotContain("baby_age_months");
    }

    [Fact]
    public void AppendTurnContext_WithoutGreetingHint_UsesAgentName()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            SystemPrompt = DefaultConfig.SystemPrompt
        };

        var result = AgentTurnPromptContext.AppendTurnContext(
            config.SystemPrompt, config, [], DefaultTemporal);

        result.Should().Contain("Preséntate como **Mimo**");
    }
}
