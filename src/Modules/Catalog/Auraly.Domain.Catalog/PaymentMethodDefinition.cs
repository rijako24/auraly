namespace Auraly.Domain.Catalog;

/// <summary>Identity and administrative invariants of a tenant payment form.</summary>
public static class PaymentMethodDefinition
{
    public static void Validate(Guid id, string code, string name, Guid typeId,
        Guid accountId, int sortOrder, byte[]? rowVersion)
    {
        if (id == Guid.Empty || typeId == Guid.Empty || accountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(code) || code.Length > 64 ||
            string.IsNullOrWhiteSpace(name) || name.Length > 160 || sortOrder < 0 ||
            rowVersion is { Length: not 8 })
            throw new ArgumentException("La forma de pago requiere código, nombre, tipo y auxiliar válidos.");
    }
}
