using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PosCustomerView(
    Guid CustomerId,
    string Identification,
    string Name,
    Guid? PriceChannelId,
    bool RequiresElectronicInvoice,
    bool IsCreditEnabled,
    decimal? AvailableCredit,
    bool IsActive)
{
    public static PosCustomerView From(PosCustomerPricing customer) => new(
        customer.CustomerId,
        customer.Identification,
        customer.Name,
        customer.PriceChannelId,
        customer.RequiresElectronicInvoice,
        customer.IsCreditEnabled,
        customer.AvailableCredit,
        customer.IsActive);
}

public sealed record PosCustomerSelectionView(
    PosDraft Draft,
    PosCustomerView? Customer)
{
    public static PosCustomerSelectionView From(PosCustomerSelection selection) => new(
        selection.Draft,
        selection.Customer is null ? null : PosCustomerView.From(selection.Customer));
}
