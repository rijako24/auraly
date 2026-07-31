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
            Guid.NewGuid(), OrderStatus.Draft, "COP", 110m, 0m, 110m,
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
            new object?[] { "cart.partially_applied", snapshot, issues, applied, false, false })!;
        var context = outcome.Error!.Context!.Value;

        context.GetProperty("applied_item_count").GetInt32().Should().Be(1);
        context.GetProperty("unresolved_item_count").GetInt32().Should().Be(3);
context.GetProperty("item_result_count").GetInt32().Should().Be(4);
        context.GetProperty("can_finalize_with_pending").GetBoolean().Should().BeFalse();
        var appliedItems = context.GetProperty("applied_items").EnumerateArray().ToList();
        appliedItems.Should().ContainSingle();
        appliedItems[0].GetProperty("name").GetString().Should().Be(appliedProduct.Name);
        context.GetProperty("items").EnumerateArray().ToList().Should().HaveCount(2);
        context.GetProperty("suggested_options").GetArrayLength().Should().Be(1);
        context.GetProperty("unavailable_items").GetArrayLength().Should().Be(1);
        var ambiguousGroups = context.GetProperty("ambiguous_groups").EnumerateArray().ToList();
        ambiguousGroups.Should().ContainSingle();
        ambiguousGroups[0].GetProperty("product_text").GetString().Should().Be("maiz");
        ambiguousGroups[0].GetProperty("options_text").GetString()
            .Should().ContainAll("MAIZ DULCE", "MAIZ CONGELADO");
        context.GetProperty("not_found_items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void FollowUpRemoval_IsIncludedInConciseAppliedChanges()
    {
        var snapshot = new OrderSnapshot(
            Guid.NewGuid(), OrderStatus.Draft, "COP", 0m, 0m, 0m, []);
        var removed = new ResolvedCartCommand(
            CartCommandOperations.Remove, Product("CHICHARRON X 500 GR", "CHI"), Guid.NewGuid(), null, "chicharrón");
        var method = typeof(ApplyOrderChangesOperation).GetMethod(
            "ClarificationOutcome", BindingFlags.NonPublic | BindingFlags.Static);

        var outcome = (OperationOutcome)method!.Invoke(
            null,
            new object?[]
            {
                "cart.partially_applied",
                snapshot,
                new[] { new CartCommandIssue("product_not_found", "otro producto", []) },
                new[] { removed },
                false,
                true
            })!;
        var displayed = outcome.Error!.Context!.Value
            .GetProperty("display_applied_items").EnumerateArray().Should().ContainSingle().Subject;

        displayed.GetProperty("removed").GetBoolean().Should().BeTrue();
        displayed.GetProperty("name").GetString().Should().Be("CHICHARRON X 500 GR");
        displayed.GetProperty("requested_name").GetString().Should().Be("chicharrón");
        displayed.GetProperty("operation").GetString().Should().Be(CartCommandOperations.Remove);
    }
    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);
}
