using FluentAssertions;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CommerceDeliveryAddressTests
{
    [Fact]
    public void ComposeDeliveryAddress_AppendsComplementaryReference()
    {
        var result = PrepareCommerceCheckoutOperation.ComposeDeliveryAddress(
            "Calle 5",
            "Barrio Centro, casa 12 frente al parque");

        result.Should().Be("Calle 5. Barrio Centro, casa 12 frente al parque");
    }

    [Fact]
    public void ComposeDeliveryAddress_DoesNotDuplicateReferenceAlreadyInAddress()
    {
        var result = PrepareCommerceCheckoutOperation.ComposeDeliveryAddress(
            "Carrera 12 # 8-45, apartamento 302",
            "apartamento 302");

        result.Should().Be("Carrera 12 # 8-45, apartamento 302");
    }

    [Theory]
    [InlineData(" Calle 5 ", null, "Calle 5")]
    [InlineData(null, " Barrio Centro ", "Barrio Centro")]
    public void ComposeDeliveryAddress_NormalizesMissingParts(string? address, string? reference, string expected) =>
        PrepareCommerceCheckoutOperation.ComposeDeliveryAddress(address, reference).Should().Be(expected);
}
