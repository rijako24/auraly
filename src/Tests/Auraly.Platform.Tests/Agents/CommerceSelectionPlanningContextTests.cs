using FluentAssertions;
using Auraly.Platform.Application.Agents.Planning;
using Auraly.Platform.Application.Commerce;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

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
    public void ProjectsEveryUnresolvedCartItemInsteadOfOnlyTheFirstOne()
    {
        var facts = new Dictionary<string, string>
        {
            ["system.pending_cart_commands"] = """
                {
                  "schemaVersion":2,
                  "items":[
                    {
                      "command":{"operation":"add","productText":"paquetes de chorizo Salsan","quantity":5,"destinationReference":null},
                      "originalProductText":"paquetes de chorizo Salsan",
                      "issue":{"code":"product_unavailable","productText":"paquetes de chorizo Salsan","candidates":["CHORIZO SALSAN"],"productCandidates":[{"name":"CHORIZO SALSAN","unitPrice":0,"currency":"COP","isAvailable":false}]},
                      "requiresResolution":true,
                      "alreadyApplied":false
                    },
                    {
                      "command":{"operation":"add","productText":"tocinetas","quantity":3,"destinationReference":null},
                      "originalProductText":"tocinetas",
                      "issue":{"code":"product_ambiguous","productText":"tocinetas","candidates":["SALSA TOCINETA 1000GR","SALSA TOCINETA 200 GR"],"productCandidates":[{"name":"SALSA TOCINETA 1000GR","unitPrice":10,"currency":"COP","isAvailable":true},{"name":"SALSA TOCINETA 200 GR","unitPrice":5,"currency":"COP","isAvailable":true}]},
                      "requiresResolution":true,
                      "alreadyApplied":false
                    }
                  ],
                  "expiresAtUtc":"2099-01-01T00:00:00Z"
                }
                """
        };

        var fragment = CommerceSelectionPlanningContextEnricher.Build(
            facts,
            new CommerceConfig
            {
                PendingCart = new PendingCartPolicy
                {
                    DiscardOnFinalizeIssueCodes = ["supplier_backorder"]
                }
            });

        var interaction = fragment!.Value.GetProperty("interaction");
        interaction.GetProperty("deferred_command_count").GetInt32().Should().Be(2);
        interaction.GetProperty("discard_on_finalize_issue_codes")[0].GetString()
            .Should().Be("supplier_backorder");
        var pendingItems = interaction.GetProperty("pending_items");
        pendingItems.GetArrayLength().Should().Be(2);
        pendingItems[0].GetProperty("issue_code").GetString().Should().Be("product_unavailable");
        pendingItems[1].GetProperty("requested_product").GetString().Should().Be("tocinetas");
        pendingItems[1].GetProperty("candidates").GetArrayLength().Should().Be(2);
    }
    [Fact]
    public void IgnoresUnsupportedLegacyCatalogFacts()
    {
        var facts = new Dictionary<string, string>
        {
            ["system.catalog_products"] =
                """[{"external_product_id":"CF59","sku":"CF59","name":"SALCHICHA LONG X 550GR","unit_price":16023.21,"currency":"COP"}]"""
        };

        var fragment = CommerceSelectionPlanningContextEnricher.Build(facts);

        fragment.Should().BeNull();
    }
    [Fact]
    public void LatestPresentedOffer_TakesForegroundWithoutDiscardingPendingCartWork()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["system.catalog_products"] = """
                {
                  "schemaVersion":2,
                  "sequence":1,
                  "snapshots":[{
                    "sequence":1,
                    "searchTerms":["papas"],
                    "products":[
                      {"externalProductId":"PA10","sku":"PA10","name":"PAPA MARQUISE X 2.5 KG","unitPrice":21000,"currency":"COP"},
                      {"externalProductId":"PA11","sku":"PA11","name":"PAPA FRENCH FRIES X 2.5 KG","unitPrice":22000,"currency":"COP"}
                    ]
                  }]
                }
                """,
            ["system.pending_cart_commands"] = """
                {
                  "schemaVersion":2,
                  "items":[{
                    "command":{"operation":"add","productText":"salchicha","quantity":2},
                    "originalProductText":"salchicha",
                    "issue":{"code":"product_ambiguous","productText":"salchicha","productCandidates":[{"name":"SALCHICHA RANCHERA","unitPrice":10000,"currency":"COP","isAvailable":true}]},
                    "requiresResolution":true,
                    "alreadyApplied":false
                  }],
                  "expiresAtUtc":"2099-01-01T00:00:00Z"
                }
                """
        };
        const string lastBotMessage =
            "Opciones: PAPA MARQUISE X 2.5 KG y PAPA FRENCH FRIES X 2.5 KG.";

        var fragment = CommerceSelectionPlanningContextEnricher.Build(
            facts,
            new CommerceConfig(),
            lastBotMessage);

        var interaction = fragment!.Value.GetProperty("interaction");
        interaction.GetProperty("expected_reply").GetString().Should().Be("catalog_follow_up");
        interaction.GetProperty("deferred_command_count").GetInt32().Should().Be(1);
        interaction.GetProperty("pending_items").GetArrayLength().Should().Be(1);
        interaction.GetProperty("pending_items")[0].GetProperty("requested_product")
            .GetString().Should().Be("salchicha");
    }


}
