using FluentAssertions;
using Auraly.Platform.Application.Agents.Operations.Internal;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class UpdateProductOfferPricesOperationTests
{
    [Fact]
    public void Parse_CelumارcasStyleMessage_PreservesConditionCapacityPriceAndVariant()
    {
        const string message = """
            *💯CELUMARCAS💯*
            *IPHONE NUEVOS 👇🏻*
            *IPHONE 17 PRO MAX (512GB) $5'000.000 ESIM NARANJA*
            *IPHONE 16 (128GB) $2'400.000 ESIM AZUL CPO CON GARANTIA 1 AÑO*
            *IPHONE USADOS GRADO 👇🏻*
            🔥 *IPHONE 14 (128GB) $1'400.000 SIM FÍSICA*
            🔥 *IPHONE 16 PRO MAX (1TB) $3'600.000 ESIM*
            """;

        var rows = UpdateProductOfferPricesOperation.Parse(message);

        rows.Should().HaveCount(4);
        rows[0].Should().BeEquivalentTo(new
        {
            Model = "iPhone 17 Pro Max",
            Condition = "new",
            StorageGb = 512,
            VariantLabel = "ESIM NARANJA",
            Price = 5_000_000m
        }, options => options.ExcludingMissingMembers());
        rows[1].VariantLabel.Should().Be("ESIM AZUL CPO CON GARANTIA 1 AÑO");
        rows[2].Condition.Should().Be("used");
        rows[2].VariantLabel.Should().Be("SIM FÍSICA");
        rows[3].StorageGb.Should().Be(1024);
        rows[3].Price.Should().Be(3_600_000m);
    }

    [Fact]
    public void Parse_IgnoresOtherBrandsAndRowsWithoutCondition()
    {
        const string message = """
            GALAXY A17 (256GB) $670.000
            IPHONE 15 (128GB) $2'200.000
            """;

        UpdateProductOfferPricesOperation.Parse(message).Should().BeEmpty();
    }
}
