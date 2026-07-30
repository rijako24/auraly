namespace Auraly.BuildingBlocks.Domain.Identifiers;

public readonly record struct TenantId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct BusinessId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct WarehouseId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct RegisterId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct UserId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct ProductId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct DocumentId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}

public readonly record struct DraftId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
