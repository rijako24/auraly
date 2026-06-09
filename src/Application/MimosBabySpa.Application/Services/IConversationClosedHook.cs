using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IConversationClosedHook
{
    Task OnClosedAsync(Conversation conversation, string closeReason, CancellationToken ct = default);
}
