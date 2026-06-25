using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Services;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class EscalationNotifierTests
{
    [Fact]
    public async Task NotifyAsync_DoesNotIncludePaymentConfirmationAction()
    {
        var whatsApp = new Mock<IWhatsAppService>();
        string? sentMessage = null;
        whatsApp
            .Setup(w => w.SendTextMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<Guid, string, string>((_, _, message) => sentMessage = message)
            .Returns(Task.CompletedTask);

        var notifier = new EscalationNotifier(whatsApp.Object, NullLogger<EscalationNotifier>.Instance);

        await notifier.NotifyAsync(
            Guid.NewGuid(),
            ["+57 300 999 8888"],
            new EscalationNotification(Guid.NewGuid(), "573001112233", "explicit_human_request", "Ayuda"));

        sentMessage.Should().NotBeNull();
        sentMessage.Should().NotContain("Confirmar pago");
        sentMessage.Should().NotContain("confirm-payment");
    }
}