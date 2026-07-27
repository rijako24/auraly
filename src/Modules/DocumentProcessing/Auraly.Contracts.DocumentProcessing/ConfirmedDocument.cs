using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Contracts.DocumentProcessing;

public sealed record ConfirmedDocument(
    TenantId TenantId,
    BusinessId BusinessId,
    DocumentId DocumentId,
    string DocumentType,
    string Payload,
    DateTimeOffset ConfirmedAt);
