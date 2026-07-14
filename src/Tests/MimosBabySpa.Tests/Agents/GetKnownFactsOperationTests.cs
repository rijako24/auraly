using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Conversation;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class GetKnownFactsOperationTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyConfiguredCustomerReadableFacts()
    {
        var operation = new GetKnownFactsOperation();
        using var input = JsonDocument.Parse("""{"fact_keys":["delivery_address"]}""");
        var outcome = await operation.ExecuteAsync(input.RootElement, Context());

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be("known_facts.found");
        outcome.Data.GetProperty("facts")[0].GetProperty("value").GetString()
            .Should().Be("Calle 5 # 10-20");
    }

    [Theory]
    [InlineData("order_checkout_presented")]
    [InlineData("system.catalog_products")]
    public async Task ExecuteAsync_RejectsFactsThatAreNotExplicitlyCustomerReadable(string key)
    {
        var operation = new GetKnownFactsOperation();
        using var input = JsonDocument.Parse($$"""{"fact_keys":["{{key}}"]}""");

        var outcome = await operation.ExecuteAsync(input.RootElement, Context());

        outcome.Success.Should().BeFalse();
        outcome.Code.Should().Be("known_facts.forbidden");
    }

    private static OperationContext Context() => new()
    {
        Config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "delivery_address", Label = "direccion de entrega", CustomerReadable = true },
                new FactSchemaEntry { Key = "order_checkout_presented", Label = "checkout", CustomerReadable = false }
            ]
        },
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["delivery_address"] = "Calle 5 # 10-20",
            ["order_checkout_presented"] = "true",
            ["system.catalog_products"] = "secret"
        }
    };
}
