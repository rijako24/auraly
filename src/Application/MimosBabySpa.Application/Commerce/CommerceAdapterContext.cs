using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public sealed record CommerceAdapterContext(
    Guid BusinessId,
    Guid AgentId,
    Guid? ConversationId,
    CommerceProvider Provider,
    IntegrationConnection? Connection);
