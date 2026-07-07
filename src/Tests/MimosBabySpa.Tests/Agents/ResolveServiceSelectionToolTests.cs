using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ResolveServiceSelectionToolTests
{
    private readonly Mock<IConversationFactsService> _facts = new();
    private readonly Mock<IServiceRepository> _services = new();
    private readonly Mock<IAddOnCatalogService> _addOnCatalog = new();
    private readonly ResolveServiceSelectionTool _tool;

    public ResolveServiceSelectionToolTests()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(_services.Object);

        var resolver = new ServiceSelectionResolver(
            unitOfWork.Object,
            NullLogger<ServiceSelectionResolver>.Instance);

        _tool = new ResolveServiceSelectionTool(resolver, _facts.Object, _addOnCatalog.Object);
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousSelection_DoesNotPersistService()
    {
        var businessId = Guid.NewGuid();
        SetupServices(businessId,
            "Corte de adulto",
            "Corte + barba",
            "Corte de niño");
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"text":"corte"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("service_selection_ambiguous");
        error.GetProperty("message").GetString().Should().Be("Service selection is ambiguous.");
        error.GetProperty("hint").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("recoverable").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("llm").GetProperty("next_action").GetString().Should().Be("get_service_catalog");
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse();
        ctx.Facts.Should().NotContainKey(ConversationFactKeys.Service);
        _facts.Verify(f => f.SetAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NotFoundSelection_ReturnsRecoverableError()
    {
        var businessId = Guid.NewGuid();
        SetupServices(businessId,
            "Corte de adulto",
            "Corte + barba");
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"text":"manicure"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("service_selection_not_found");
        error.GetProperty("message").GetString().Should().Be("Service selection was not found.");
        error.GetProperty("hint").ValueKind.Should().Be(JsonValueKind.Null);
        error.GetProperty("recoverable").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("llm").GetProperty("next_action").GetString().Should().Be("get_service_catalog");
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse();
        ctx.Facts.Should().NotContainKey(ConversationFactKeys.Service);
        _facts.Verify(f => f.SetAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithManageableReservation_StillResolvesCatalogSelectionWithoutIntentHardcode()
    {
        var businessId = Guid.NewGuid();
        SetupServices(businessId, "Corte premium de adulto");
        var ctx = CreateContext(businessId);
        ctx.LatestUserMessage = "Quiero cambiar el servicio a corte premium de adulto";
        ctx.ManageableReservations =
        [
            new Reservation
            {
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = new DateTime(2026, 9, 2, 11, 0, 0),
                Service = new Service { ServiceName = "Corte basico de adulto" }
            }
        ];

        using var args = JsonDocument.Parse("""{"text":"corte premium de adulto"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Corte premium de adulto");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, ConversationFactKeys.Service, "Corte premium de adulto",
            false, It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task ExecuteAsync_UniqueSelection_PersistsCanonicalService()
    {
        var businessId = Guid.NewGuid();
        SetupServices(businessId,
            "Corte de adulto",
            "Corte + barba",
            "Corte de niño");
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"text":"corte adulto"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"selection_status\":\"resolved\"");
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Corte de adulto");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, ConversationFactKeys.Service, "Corte de adulto",
            false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_KeywordsResolveHaircutForChild()
    {
        var businessId = Guid.NewGuid();
        SetupServices(businessId,
            new Service { ServiceName = "Corte basico", Keywords = "corte adulto, corte de cabello adulto" },
            new Service { ServiceName = "Corte + barba", Keywords = "corte barba, arreglo de barba" },
            new Service { ServiceName = "Corte infantil", Keywords = "corte nino, corte niño, corte de cabello niño, cabello niño" },
            new Service { ServiceName = "Corte puntas", Keywords = "corte bebe, corte bebés, solo puntas" });
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"text":"corte de cabello para niño"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"selection_status\":\"resolved\"");
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Corte infantil");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, ConversationFactKeys.Service, "Corte infantil",
            false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredBookingServiceFactKey()
    {
        var businessId = Guid.NewGuid();
        SetupServices(businessId, "Spa Premium");
        var ctx = CreateContext(businessId);
        ctx.Config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "selected_service", Role = "booking.service", Source = "user" }
            ]
        };

        using var args = JsonDocument.Parse("""{"text":"Spa Premium"}""");
        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        ctx.Facts["selected_service"].Should().Be("Spa Premium");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, "selected_service", "Spa Premium",
            false, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupServices(Guid businessId, params string[] names) =>
        SetupServices(businessId, names.Select(name => new Service { ServiceName = name }).ToArray());

    private void SetupServices(Guid businessId, params Service[] services)
    {
        _services
            .Setup(s => s.GetActiveByBusinessIdAsync(businessId))
            .ReturnsAsync(services.Select(service =>
            {
                service.BusinessId = businessId;
                service.Description ??= string.Empty;
                service.IsActive = true;
                return service;
            }).ToList());
    }

    private static AgentToolContext CreateContext(Guid businessId) => new()
    {
        BusinessId = businessId,
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
