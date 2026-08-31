using Auraly.Contracts.TenantBilling;

namespace Auraly.Platform.Application.Identity.Services;

public interface ITenantCommercialQuoteService
{
    Task<TenantCommercialCatalogDto> GetCatalogAsync(CancellationToken cancellationToken);
    Task<TenantQuoteDto> QuoteAsync(TenantQuoteRequest request, CancellationToken cancellationToken);
}

public sealed class TenantCommercialQuoteService(ITenantCommercialCatalogStore store)
    : ITenantCommercialQuoteService
{
    public Task<TenantCommercialCatalogDto> GetCatalogAsync(CancellationToken cancellationToken) =>
        store.GetAsync(cancellationToken);

    public async Task<TenantQuoteDto> QuoteAsync(
        TenantQuoteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var catalog = await store.GetAsync(cancellationToken);
        var plan = catalog.Plans.SingleOrDefault(item =>
            string.Equals(item.Code, request.PlanCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("El plan seleccionado no está disponible.");
        var pricingPlan = plan;
        if (plan.IsCustom)
        {
            pricingPlan = catalog.Plans.SingleOrDefault(item =>
                string.Equals(item.Code, "company", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("El plan Empresa no está disponible como piso de Personalizado.");
            if (request.AdditionalFullUsers == 0 && request.SellerUsers == 0
                && request.AdditionalPosDevices == 0 && request.DianDocumentPacks == 0
                && request.PayrollEmployeePacks == 0)
                throw new ArgumentException(
                    "El plan Personalizado debe superar al plan Empresa en al menos una capacidad.");
        }
        var annual = string.Equals(request.BillingPeriod, "Annual", StringComparison.OrdinalIgnoreCase);
        if (!annual && !string.Equals(request.BillingPeriod, "Monthly", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La periodicidad debe ser Monthly o Annual.");
        var quantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["full_user"] = request.AdditionalFullUsers,
            ["seller_user"] = request.SellerUsers,
            ["pos_device"] = request.AdditionalPosDevices,
            ["dian_document_pack"] = request.DianDocumentPacks,
            ["payroll_employee_pack"] = request.PayrollEmployeePacks
        };
        if (quantities.Values.Any(value => value < 0))
            throw new ArgumentException("Las cantidades adicionales no pueden ser negativas.");

        var lines = new List<TenantQuoteLineDto>
        {
            new(plan.Code, plan.Name, 1, 1, pricingPlan.MonthlyPriceCop,
                pricingPlan.MonthlyPriceCop, pricingPlan.SalesTaxRate)
        };
        foreach (var addOn in catalog.AddOns)
        {
            var quantity = quantities.GetValueOrDefault(addOn.Code);
            if (quantity == 0) continue;
            lines.Add(new(addOn.Code, addOn.Name, quantity, addOn.UnitSize,
                addOn.MonthlyUnitPriceCop, Money(addOn.MonthlyUnitPriceCop * quantity), addOn.SalesTaxRate));
        }
        var monthly = Money(lines.Sum(line => line.MonthlyTotalCop));
        var periods = annual ? 12 : 1;
        var gross = Money(monthly * periods);
        var discountRate = annual ? pricingPlan.AnnualDiscountRate : 0m;
        var discount = Money(gross * discountRate);
        var taxable = Money(gross - discount);
        var tax = Money(lines.Sum(line =>
            line.MonthlyTotalCop * periods * (1m - discountRate) * line.SalesTaxRate / 100m));
        var payable = Money(taxable + tax);
        var addOns = catalog.AddOns.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        return new(plan.Code, plan.Name, annual ? "Annual" : "Monthly", monthly, periods,
            gross, discountRate, discount, tax, payable, Money(payable / periods),
            pricingPlan.IncludedFullUsers + request.AdditionalFullUsers,
            pricingPlan.IncludedSellerUsers + request.SellerUsers,
            pricingPlan.IncludedPosDevices + request.AdditionalPosDevices,
            pricingPlan.IncludedDianDocuments + request.DianDocumentPacks * addOns["dian_document_pack"].UnitSize,
            pricingPlan.IncludedPayrollEmployees + request.PayrollEmployeePacks * addOns["payroll_employee_pack"].UnitSize,
            lines);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
