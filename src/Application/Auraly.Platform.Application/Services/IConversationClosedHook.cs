using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Application.Services;

public interface IConversationClosedHook
{
    Task OnClosedAsync(Conversation conversation, string closeReason, CancellationToken ct = default);
}
