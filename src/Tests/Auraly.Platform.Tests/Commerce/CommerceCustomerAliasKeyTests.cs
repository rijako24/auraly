using FluentAssertions;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class CommerceCustomerAliasKeyTests
{
    [Fact]
    public void Resolve_WithExternalCustomer_UsesStableProviderIdentity()
    {
        var customer = new CommerceCustomerReference(
            CommerceProvider.Mantis,
            " 10013 ",
            " 6826 ",
            "Cliente",
            "3001234567");

        var key = CommerceCustomerAliasKey.Resolve(
            customer,
            "+57 300 123 4567");

        key.Should().Be("mantis:10013:6826");
    }

    [Fact]
    public void Resolve_WithoutExternalCustomer_PreservesLegacyPhoneKey()
    {
        var key = CommerceCustomerAliasKey.Resolve(
            null,
            "+57 300 123 4567");

        key.Should().Be("573001234567");
    }

    [Fact]
    public void FromExternalCustomer_LongIdentity_IsDeterministicAndFitsDatabase()
    {
        var value = new string('A', 120);
        var customer = new CommerceCustomerReference(
            CommerceProvider.Mantis,
            value,
            value,
            null,
            string.Empty);

        var first = CommerceCustomerAliasKey.FromExternalCustomer(customer);
        var second = CommerceCustomerAliasKey.FromExternalCustomer(customer);

        first.Should().Be(second);
        first.Should().StartWith("mantis:");
        first.Length.Should().BeLessThanOrEqualTo(100);
    }
}
