using FluentAssertions;
using MimosBabySpa.Application.Agents.Planning;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CommerceSelectionPlanningContextTests
{
    [Fact]
    public void ProjectsPendingInteractionAndAllOfferSnapshots()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["system.catalog_products"] = """
                {
                  "schemaVersion":2,
                  "sequence":2,
                  "snapshots":[
                    {
                      "sequence":1,
                      "offeredAtUtc":"2026-07-12T10:00:00Z",
                      "searchTerms":["pechuga"],
                      "products":[{"externalProductId":"PO63","sku":"PO63","name":"PECHUGA CRIOLLA","unitPrice":14033.67,"currency":"COP"}]
                    },
                    {
                      "sequence":2,
                      "offeredAtUtc":"2026-07-12T10:01:00Z",
                      "searchTerms":["cerdo"],
                      "products":[{"externalProductId":"CE10","sku":"CE10","name":"PIERNA DE CERDO CON PIEL Y HUESO","unitPrice":10319.16,"currency":"COP"}]
                    }
                  ]
                }
                """,
            ["system.pending_cart_commands"] = """
                {
                  "schemaVersion":1,
                  "commands":[
                    {"operation":"add","productText":"pechuga","quantity":2,"destinationReference":null},
                    {"operation":"add","productText":"cerdo","quantity":1,"destinationReference":null}
                  ],
                  "ambiguousProductText":"cerdo",
                  "productCandidates":[],
                  "expiresAtUtc":"2099-01-01T00:00:00Z"
                }
                """
        };

        var fragment = CommerceSelectionPlanningContextEnricher.Build(facts);

        fragment.Should().NotBeNull();
        fragment!.Key.Should().Be("shoppingContext");
        var interaction = fragment.Value.GetProperty("interaction");
        interaction.GetProperty("expected_reply").GetString().Should().Be("resolve_pending_cart_selection");
        interaction.GetProperty("requested_product").GetString().Should().Be("cerdo");
        interaction.GetProperty("quantity").GetDecimal().Should().Be(1);
        fragment.Value.GetProperty("offers").GetArrayLength().Should().Be(2);
        fragment.Value.GetProperty("offers")[0].GetProperty("products")[0].GetString().Should().Be("PECHUGA CRIOLLA");
        fragment.Value.GetProperty("offers")[1].GetProperty("is_latest").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ReadsLegacyCatalogAsTheLatestOffer()
    {
        var facts = new Dictionary<string, string>
        {
            ["system.catalog_products"] =
                """[{"external_product_id":"CF59","sku":"CF59","name":"SALCHICHA LONG X 550GR","unit_price":16023.21,"currency":"COP"}]"""
        };

        var fragment = CommerceSelectionPlanningContextEnricher.Build(facts);

        fragment.Should().NotBeNull();
        fragment!.Value.GetProperty("latest_offer_sequence").GetInt64().Should().Be(1);
        fragment.Value.GetProperty("offers").GetArrayLength().Should().Be(1);
        fragment.Value.GetProperty("offers")[0].GetProperty("products")[0].GetString()
            .Should().Be("SALCHICHA LONG X 550GR");
    }
}
