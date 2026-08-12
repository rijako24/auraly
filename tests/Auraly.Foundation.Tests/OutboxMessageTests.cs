using Auraly.BuildingBlocks.Application.Outbox;
using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Foundation.Tests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void Failed_message_can_retry_and_publish_once()
    {
        var message = new OutboxMessage(
            Guid.NewGuid(),
            new TenantId(Guid.NewGuid()),
            "sales.confirmed",
            "{}",
            DateTimeOffset.UtcNow);

        message.StartAttempt();
        message.MarkFailed("offline");
        message.StartAttempt();
        message.MarkPublished(DateTimeOffset.UtcNow);

        Assert.Equal(2, message.AttemptCount);
        Assert.Equal(OutboxMessageStatus.Published, message.Status);
        Assert.Throws<InvalidOperationException>(message.StartAttempt);
    }
}
