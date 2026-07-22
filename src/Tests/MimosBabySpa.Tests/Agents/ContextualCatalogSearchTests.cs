using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.Services;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ContextualCatalogSearchTests
{
    [Theory]
    [InlineData("acero", "PIEZA DE MANO CERAMIK ACERO")]
    [InlineData("luz", "PIEZA DE MANO TORCH CON LUZ")]
    public async Task ForegroundCatalogSubject_FiltersUnrelatedAttributeMatches(
        string query,
        string expectedProduct)
    {
        var handpiece = Product(expectedProduct, "Piezas de mano", query);
        var unrelated = Product(
            query == "luz" ? "SCALER P6 CON LUZ" : "ACERO INOXIDABLE PARA ORTODONCIA",
            query == "luz" ? "Escalers" : "Ortodoncia",
            query);
        var commerce = new QueryCommerceService(request =>
        {
            if (request.Query?.Contains("pieza de mano", StringComparison.OrdinalIgnoreCase) == true)
                return Result(handpiece, unrelated);
            return query == "luz" ? Result(handpiece, unrelated) : Result();
        });
        var (operationContext, _) = CatalogContext("pieza de mano");

        var outcome = await new SearchProductsOperation(commerce).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { queries = new[] { query } }),
            operationContext);

        outcome.Code.Should().Be("products.found");
        outcome.Data.GetProperty("search_terms")[0].GetString()
            .Should().Be($"pieza de mano {query}");
        outcome.Data.GetProperty("products").EnumerateArray()
            .Select(product => product.GetProperty("name").GetString())
            .Should().Equal(expectedProduct);
        commerce.Queries.Should().Contain(query);
        commerce.Queries.Should().Contain($"pieza de mano {query}");
    }

    [Fact]
    public async Task ExplicitNewCatalogSubject_FallsBackToStandaloneSearch()
    {
        var gloves = Product("GUANTES DE NITRILO", "Guantes", "nitrilo");
        var oldSubject = Product("PIEZA DE MANO CERAMIK", "Piezas de mano", "ceramica");
        var commerce = new QueryCommerceService(request =>
        {
            if (request.Query?.Equals("guantes", StringComparison.OrdinalIgnoreCase) == true)
                return Result(gloves);
            if (request.Query?.Contains("pieza de mano", StringComparison.OrdinalIgnoreCase) == true)
                return Result(oldSubject);
            return Result();
        });
        var (operationContext, _) = CatalogContext("pieza de mano");

        var outcome = await new SearchProductsOperation(commerce).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { queries = new[] { "guantes" } }),
            operationContext);

        outcome.Code.Should().Be("products.found");
        outcome.Data.GetProperty("search_terms")[0].GetString().Should().Be("guantes");
        outcome.Data.GetProperty("products")[0].GetProperty("name").GetString()
            .Should().Be("GUANTES DE NITRILO");
    }

    [Fact]
    public async Task ContextualSearch_IsNotAppliedWhenPreviousOfferIsNotForeground()
    {
        var steel = Product("ACERO INOXIDABLE", "Ortodoncia", "acero");
        var commerce = new QueryCommerceService(request =>
            request.Query?.Equals("acero", StringComparison.OrdinalIgnoreCase) == true
                ? Result(steel)
                : Result());
        var (operationContext, session) = CatalogContext("pieza de mano");
        session.ConversationState.LastBotMessage = "Conversacion cerrada.";

        var outcome = await new SearchProductsOperation(commerce).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { queries = new[] { "acero" } }),
            operationContext);

        outcome.Data.GetProperty("search_terms")[0].GetString().Should().Be("acero");
        commerce.Queries.Should().NotContain("pieza de mano acero");
    }

    [Fact]
    public async Task CatalogMemory_PreservesRootAnchorAfterARefinedSearch()
    {
        var facts = new Mock<IConversationFactsService>();
        facts.Setup(service => service.SetAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var context = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Config = new AgentConfig
            {
                Commerce = new CommerceConfig { Enabled = true }
            },
            ConversationState = new ConversationState()
        };
        var products = new[]
        {
            Product("PIEZA DE MANO CERAMIK", "Piezas de mano", "Acero")
        };

        await CatalogOfferMemory.RememberAsync(
            facts.Object, context, products, ["pieza de mano"], CancellationToken.None);
        await CatalogOfferMemory.RememberAsync(
            facts.Object, context, products, ["pieza de mano luz"], CancellationToken.None);

        var memory = CatalogOfferMemory.Read(context.Facts);
        var latest = memory!.Snapshots.MaxBy(snapshot => snapshot.Sequence);
        latest!.ContextAnchorTerms.Should().Equal("pieza de mano");
    }


    private static (OperationContext Operation, AgentConversationContext Session) CatalogContext(
        string anchor)
    {
        var config = new AgentConfig
        {
            Commerce = new CommerceConfig { Enabled = true }
        };
        var state = new ConversationState
        {
            LastBotMessage = "Opciones: PIEZA DE MANO CERAMIK y PIEZA DE MANO TORCH."
        };
        var session = new AgentConversationContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Config = config,
            ConversationState = state
        };
        session.Facts[CatalogOfferMemory.FactKey] = JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            sequence = 1,
            snapshots = new[]
            {
                new
                {
                    sequence = 1,
                    searchTerms = new[] { anchor },
                    contextAnchorTerms = new[] { anchor },
                    products = new[]
                    {
                        new
                        {
                            productId = (Guid?)null,
                            externalProductId = "P1",
                            sku = "PM-1",
                            name = "PIEZA DE MANO CERAMIK",
                            unitPrice = 100m,
                            currency = "COP",
                            stockQuantity = 5m
                        },
                        new
                        {
                            productId = (Guid?)null,
                            externalProductId = "P2",
                            sku = "PM-2",
                            name = "PIEZA DE MANO TORCH",
                            unitPrice = 120m,
                            currency = "COP",
                            stockQuantity = 5m
                        }
                    }
                }
            }
        });

        return (new OperationContext
        {
            BusinessId = session.BusinessId,
            ConversationId = session.ConversationId,
            Config = config,
            ConversationState = state,
            Session = session
        }, session);
    }

    private static ProductReference Product(
        string name,
        string category,
        string description) =>
        new(
            Guid.NewGuid(),
            null,
            name,
            name,
            description,
            category,
            100m,
            "COP",
            5m);

    private static ProductSearchResult Result(params ProductReference[] products) =>
        new(products, "test");

    private sealed class QueryCommerceService(
        Func<ProductSearchRequest, ProductSearchResult> search) : ICommerceService
    {
        public List<string> Queries { get; } = [];

        public Task<ProductSearchResult> SearchProductsAsync(
            AgentConversationContext ctx,
            ProductSearchRequest request,
            CancellationToken ct = default)
        {
            Queries.Add(request.Query ?? string.Empty);
            return Task.FromResult(search(request));
        }

        public Task<OrderSnapshot> GetDraftAsync(
            AgentConversationContext ctx,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OrderSnapshot> AddItemAsync(
            AgentConversationContext ctx,
            AddOrderItemRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OrderSnapshot> RemoveItemAsync(
            AgentConversationContext ctx,
            Guid orderItemId,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OrderSnapshot> UpdateItemQuantityAsync(
            AgentConversationContext ctx,
            Guid orderItemId,
            decimal quantity,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> DiscardDraftsAsync(
            Guid businessId,
            Guid conversationId,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OrderSnapshot> CreateOrderAsync(
            AgentConversationContext ctx,
            CreateOrderRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<OrderSnapshot> ConfirmPaidOrderAsync(
            Guid businessId,
            Guid paymentTransactionId,
            AgentConfig config,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}

