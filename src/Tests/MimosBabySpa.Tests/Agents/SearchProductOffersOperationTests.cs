using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class SearchProductOffersOperationTests
{
    [Fact]
    public async Task ExecuteAsync_EmitsOnePrimaryImageForEachDistinctProductThatHasOne()
    {
        var businessId = Guid.NewGuid();
        var firstProduct = CreateProduct(Guid.NewGuid(), businessId, "iPhone 17 Pro Max");
        var secondProduct = CreateProduct(Guid.NewGuid(), businessId, "iPhone 17");
        var offers = new[]
        {
            CreateOffer(firstProduct, businessId, 256, "products/17-pro-max-secondary.jpg"),
            CreateOffer(firstProduct, businessId, 512, "products/17-pro-max-primary.jpg", true),
            CreateOffer(secondProduct, businessId, 256, "products/17-primary.jpg", true)
        };
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.SearchOffersAsync(
                businessId, "iPhone", "used", It.IsAny<CancellationToken>()))
            .ReturnsAsync(offers);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var operation = new SearchProductOffersOperation(unitOfWork.Object);
        using var input = JsonDocument.Parse(
            """{"product_query":"iPhone","condition":"used"}""");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            new OperationContext { BusinessId = businessId });

        outcome.Code.Should().Be("offers.found");
        outcome.Effects.Should().BeEquivalentTo(
            [
                new OutboundMediaOperationEffect(
                    "products/17-pro-max-primary.jpg", "image", "iPhone 17 Pro Max"),
                new OutboundMediaOperationEffect(
                    "products/17-primary.jpg", "image", "iPhone 17")
            ],
            options => options.WithStrictOrdering());
        outcome.Data.GetProperty("available_colors").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDistinctAvailableColorsWithoutAnImageRequirement()
    {
        var businessId = Guid.NewGuid();
        var product = CreateProduct(Guid.NewGuid(), businessId, "iPhone 15");
        var blue = CreateOffer(product, businessId, 128, color: "Azul");
        var duplicateBlue = CreateOffer(product, businessId, 256, color: "azul");
        var black = CreateOffer(product, businessId, 512, color: "Negro");
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.SearchOffersAsync(
                businessId, "iPhone 15", "new", It.IsAny<CancellationToken>()))
            .ReturnsAsync([blue, duplicateBlue, black]);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var operation = new SearchProductOffersOperation(unitOfWork.Object);
        using var input = JsonDocument.Parse(
            """{"product_query":"iPhone 15","condition":"new"}""");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            new OperationContext { BusinessId = businessId });

        outcome.Data.GetProperty("available_colors")
            .EnumerateArray().Select(value => value.GetString())
            .Should().Equal("Azul", "Negro");
        outcome.Effects.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredOfferAttributesWithoutInjectingTenantPolicy()
    {
        var businessId = Guid.NewGuid();
        var product = CreateProduct(Guid.NewGuid(), businessId, "Generic Phone");
        var offer = CreateOffer(product, businessId, 128);
        offer.MinimumBatteryHealthPercent = 73;
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.SearchOffersAsync(
                businessId, "Generic Phone", "used", It.IsAny<CancellationToken>()))
            .ReturnsAsync([offer]);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Products).Returns(products.Object);
        var operation = new SearchProductOffersOperation(unitOfWork.Object);
        using var input = JsonDocument.Parse(
            """{"product_query":"Generic Phone","condition":"used"}""");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            new OperationContext { BusinessId = businessId });

        outcome.Data.GetProperty("offers")[0]
            .GetProperty("minimum_battery_health_percent").GetInt32().Should().Be(73);
        outcome.Data.GetProperty("response_guidance").GetString()
            .Should().NotContain("90%");
        outcome.Data.GetProperty("response_guidance").GetString()
            .Should().NotContain("Digital Shop");
    }

    private static Product CreateProduct(Guid productId, Guid businessId, string name) =>
        new()
        {
            ProductId = productId,
            BusinessId = businessId,
            Name = name
        };

    private static ProductOffer CreateOffer(
        Product product,
        Guid businessId,
        int storageGb,
        string? mediaUrl = null,
        bool isPrimary = false,
        string? color = null)
    {
        var offer = new ProductOffer
        {
            ProductOfferId = Guid.NewGuid(),
            ProductId = product.ProductId,
            BusinessId = businessId,
            Product = product,
            Condition = "used",
            StorageGb = storageGb,
            Color = color,
            UnitPrice = 1_000_000m
        };
        if (mediaUrl is not null)
        {
            offer.Images.Add(new ProductImage
            {
                ProductImageId = Guid.NewGuid(),
                ProductId = product.ProductId,
                ProductOfferId = offer.ProductOfferId,
                BusinessId = businessId,
                MediaUrl = mediaUrl,
                AltText = product.Name,
                IsPrimary = isPrimary,
                IsActive = true
            });
        }
        return offer;
    }
}