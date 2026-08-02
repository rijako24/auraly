using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Contracts.Sales;

public sealed record ConfirmedSale(
    TenantId TenantId,
    BusinessId BusinessId,
    WarehouseId WarehouseId,
    UserId UserId,
    DeviceId? DeviceId,
    WorkSessionId WorkSessionId,
    DocumentId DocumentId,
    string DocumentNumber,
    string FiscalNumber,
    string Cufe,
    decimal Total,
    DateTimeOffset IssuedAt);
