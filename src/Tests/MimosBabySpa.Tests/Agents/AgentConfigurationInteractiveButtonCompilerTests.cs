using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentConfigurationInteractiveButtonCompilerTests
{
    [Fact]
    public void Compile_WithValidWhatsAppReplyButtons_Succeeds()
    {
        var config = Config(new MessageSequenceStep
        {
            Type = "text",
            Body = "¿Confirmas?",
            Buttons =
            [
                new MessageSequenceButton { Id = "reservation:confirm:123", Title = "Confirmar" },
                new MessageSequenceButton { Id = "reservation:cancel:123", Title = "Cancelar" }
            ]
        });

        var compilation = Compiler().Compile(config);

        compilation.IsValid.Should().BeTrue(
            string.Join("; ", compilation.Diagnostics.Select(value => value.Message)));
    }

    [Fact]
    public void Compile_WithInvalidWhatsAppReplyButtons_RejectsBeforeActivation()
    {
        var config = Config(new MessageSequenceStep
        {
            Type = "text",
            Body = "Selecciona",
            Buttons =
            [
                new MessageSequenceButton { Id = "duplicate", Title = "Primera" },
                new MessageSequenceButton { Id = "duplicate", Title = "Segunda" },
                new MessageSequenceButton { Id = "third", Title = "Este titulo supera veinte caracteres" },
                new MessageSequenceButton { Id = "fourth", Title = "Cuarta" }
            ]
        });

        var compilation = Compiler().Compile(config);

        compilation.IsValid.Should().BeFalse();
        compilation.Diagnostics.Should().Contain(value => value.Code == "too_many_buttons");
        compilation.Diagnostics.Should().Contain(value => value.Code == "duplicate_button_id");
        compilation.Diagnostics.Should().Contain(value => value.Code == "button_title_too_long");
    }

    [Fact]
    public void Compile_WithEnabledNotificationWithoutRecipients_RejectsBeforeActivation()
    {
        var config = Config(new MessageSequenceStep { Type = "text", Body = "Reserva creada" });
        config.Notifications["reservation_created"].Recipients = [];

        var compilation = Compiler().Compile(config);

        compilation.IsValid.Should().BeFalse();
        compilation.Diagnostics.Should().Contain(value => value.Code == "notification_recipients_required");
    }

    private static AgentConfigurationCompiler Compiler() =>
        new(new AgentOperationRegistry([]));

    private static AgentConfig Config(MessageSequenceStep step) => new()
    {
        Flows =
        [
            new AgentFlowDefinition
            {
                Id = "booking",
                Type = FlowTypes.Primary,
                Stages = [new AgentFlowStage { Id = "start" }]
            }
        ],
        MessageSequences = new MessageSequenceCatalog
        {
            ["reservation_created"] = new MessageSequence { Messages = [step] }
        },
        Notifications = new NotificationDefinitions
        {
            ["reservation_created"] = new EventNotificationConfig
            {
                Enabled = true,
                Recipients = ["573001112233"],
                SendMessageSequence = "reservation_created"
            }
        }
    };
}
