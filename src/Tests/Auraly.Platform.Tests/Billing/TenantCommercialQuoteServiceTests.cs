using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.Services;
using FluentAssertions;
using Xunit;

namespace Auraly.Platform.Tests.Billing;

public sealed class TenantCommercialQuoteServiceTests
{
    private readonly TenantCommercialQuoteService service = new(new Catalog());

    [Fact]
    public async Task Annual_is_exactly_fifteen_percent_cheaper_and_preserves_monthly_capacity()
    {
        var quote = await service.QuoteAsync(
            new("essential", "Annual", 2, 1, 1, 2), CancellationToken.None);

        quote.MonthlySubtotalCop.Should().Be(249_900m);
        quote.GrossPeriodAmountCop.Should().Be(2_998_800m);
        quote.DiscountRate.Should().Be(0.15m);
        quote.DiscountAmountCop.Should().Be(449_820m);
        quote.TaxAmountCop.Should().Be(484_306.20m);
        quote.PayableAmountCop.Should().Be(3_033_286.20m);
        quote.MonthlyEquivalentCop.Should().Be(252_773.85m);
        quote.FullUserLimit.Should().Be(5);
        quote.SellerUserLimit.Should().Be(1);
        quote.PosDeviceLimit.Should().Be(2);
        quote.DianDocumentMonthlyLimit.Should().Be(2_500);
        quote.PayrollEmployeeLimit.Should().Be(10);
    }

    [Fact]
    public async Task Monthly_has_no_discount() =>
        (await service.QuoteAsync(new("essential", "Monthly", 0, 0, 0, 0), default))
            .Should().Match<TenantQuoteDto>(quote =>
                quote.Periods == 1 && quote.DiscountAmountCop == 0 &&
                quote.TaxAmountCop == 22_781m && quote.PayableAmountCop == 142_681m);

    [Fact]
    public async Task Payroll_capacity_is_sold_only_in_ten_employee_packages()
    {
        var quote = await service.QuoteAsync(
            new("essential", "Monthly", 0, 0, 0, 0, 2), default);

        quote.PayrollEmployeeLimit.Should().Be(30);
        quote.MonthlySubtotalCop.Should().Be(169_900m);
    }

    [Fact]
    public async Task Starter_can_add_any_whole_number_of_one_thousand_document_packs()
    {
        var quote = await service.QuoteAsync(
            new("starter", "Monthly", 0, 0, 0, 3), default);

        quote.DianDocumentMonthlyLimit.Should().Be(3_100);
        quote.MonthlySubtotalCop.Should().Be(120_000m);
        quote.TaxAmountCop.Should().Be(22_800m);
        quote.PayableAmountCop.Should().Be(142_800m);
        quote.Lines.Single(line => line.Code == "dian_document_pack")
            .Quantity.Should().Be(3);
    }

    [Theory]
    [InlineData("corporate", "Annual", 0)]
    [InlineData("essential", "Weekly", 0)]
    [InlineData("essential", "Annual", -1)]
    public async Task Invalid_or_unpriced_quotes_are_rejected(
        string plan, string period, int fullUsers)
    {
        var action = () => service.QuoteAsync(new(plan, period, fullUsers, 0, 0, 0), default);
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Custom_plan_uses_company_as_floor_and_requires_a_strictly_higher_capacity()
    {
        var quote = await service.QuoteAsync(
            new("corporate", "Monthly", 1, 0, 0, 0), default);

        quote.PlanCode.Should().Be("corporate");
        quote.PlanName.Should().Be("Personalizado");
        quote.FullUserLimit.Should().Be(13);
        quote.SellerUserLimit.Should().Be(0);
        quote.PosDeviceLimit.Should().Be(5);
        quote.DianDocumentMonthlyLimit.Should().Be(3_000);
        quote.PayrollEmployeeLimit.Should().Be(100);
        quote.MonthlySubtotalCop.Should().Be(479_900m);
        quote.PayableAmountCop.Should().Be(571_081m);
    }

    private sealed class Catalog : ITenantCommercialCatalogStore
    {
        public Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TenantProvisioningGeographyDto>>([]);

        public Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetDivisionsAsync(Guid countryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TenantProvisioningGeographyDto>>([]);

        public Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetCitiesAsync(Guid divisionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TenantProvisioningGeographyDto>>([]);

        public Task<TenantCommercialCatalogDto> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new TenantCommercialCatalogDto(
                [
                    new(Guid.NewGuid(), "starter", "Inicio", 60_000m, 19m, 0.15m,
                        1, 0, 1, 100, 0, false, false, []),
                    new(Guid.NewGuid(), "essential", "Esencial", 119_900m, 19m, 0.15m,
                        3, 0, 1, 500, 10, false, false, []) ,
                    new(Guid.NewGuid(), "company", "Empresa", 449_900m, 19m, 0.15m,
                        12, 0, 5, 3_000, 100, false, false, []),
                    new(Guid.NewGuid(), "corporate", "Personalizado", 0m, 19m, 0.15m,
                        0, 0, 0, 0, 0, false, true, [])
                ],
                [
                    AddOn("full_user", 30_000m, 1), AddOn("seller_user", 10_000m, 1),
                    AddOn("pos_device", 20_000m, 1), AddOn("dian_document_pack", 20_000m, 1_000),
                    AddOn("payroll_employee_pack", 25_000m, 10)
                ]));

        private static TenantCommercialAddOnDto AddOn(string code, decimal price, int size) =>
            new(Guid.NewGuid(), code, code, code, size, price, 19m);
    }
}
