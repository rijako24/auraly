using System.Reflection;
using FluentAssertions;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ApplyOrderChangesPresentationRegressionTests
{
    [Fact]
    public void PartialOutcome_SeparatesAppliedCommandsFromCompleteCart()
    {
        var oldProduct = Product("PRODUCTO ANTERIOR", "OLD");
        var appliedProduct = Product("TOCINETA AHUMADA 500 G", "NEW");
        var snapshot = new OrderSnapshot(
            Guid.NewGuid(), OrderStatus.Draft, "COP", 110m, 0m, 0m, 110m,
            [
                new OrderItemSnapshot(Guid.NewGuid(), oldProduct.ProductId, oldProduct.ExternalProductId,
                    oldProduct.Sku, oldProduct.Name, 1m, 10m, 10m),
                new OrderItemSnapshot(Guid.NewGuid(), appliedProduct.ProductId, appliedProduct.ExternalProductId,
                    appliedProduct.Sku, appliedProduct.Name, 2m, 50m, 100m)
            ]);
        var issues = new[]
        {
            new CartCommandIssue("product_suggestion", "jamonada", ["JAMON CUNIT"])
            {
                ProductCandidates = [new("JAMON CUNIT", 10m, "COP")]
            },
            new CartCommandIssue("product_unavailable", "chorizo", ["CHORIZO SALSAN"])
            {
                ProductCandidates = [new("CHORIZO SALSAN", 0m, "COP")]
            },
            new CartCommandIssue("product_ambiguous", "maiz", ["MAIZ DULCE", "MAIZ CONGELADO"])
            {
                ProductCandidates = [new("MAIZ DULCE", 8m, "COP"), new("MAIZ CONGELADO", 9m, "COP")]
            }
        };
        var applied = new[]
        {
            new ResolvedCartCommand(
                CartCommandOperations.Add, appliedProduct, null, 2m, "tocinetas")
        };
        var method = typeof(ApplyOrderChangesOperation).GetMethod(
            "ClarificationOutcome", BindingFlags.NonPublic | BindingFlags.Static);

        var outcome = (OperationOutcome)method!.Invoke(
            null,
            new object?[] { "cart.partially_applied", snapshot, issues, applied })!;
        var context = outcome.Error!.Context!.Value;

        context.GetProperty("applied_item_count").GetInt32().Should().Be(1);
        context.GetProperty("unresolved_item_count").GetInt32().Should().Be(3);
        context.GetProperty("item_result_count").GetInt32().Should().Be(4);
        var appliedItems = context.GetProperty("applied_items").EnumerateArray().ToList();
        appliedItems.Should().ContainSingle();
        appliedItems[0].GetProperty("name").GetString().Should().Be(appliedProduct.Name);
        context.GetProperty("items").EnumerateArray().ToList().Should().HaveCount(2);
        context.GetProperty("suggested_options").GetArrayLength().Should().Be(1);
        context.GetProperty("unavailable_items").GetArrayLength().Should().Be(1);
        context.GetProperty("ambiguous_options").GetArrayLength().Should().Be(2);
        context.GetProperty("not_found_items").GetArrayLength().Should().Be(0);
    }

    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);
}
