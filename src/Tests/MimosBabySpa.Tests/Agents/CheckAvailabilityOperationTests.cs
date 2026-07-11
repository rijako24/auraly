using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Availability;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CheckAvailabilityOperationTests
{
    [Fact]
    public async Task WithoutTime_ReturnsOptionsAsRequiredExclusiveTemplate()
    {
        var outcome = await BuildOperation(AvailableOptions("09:00", "10:00")).ExecuteAsync(
            Json(new { service = "Corte infantil", date = "2026-07-11" }),
            Context());

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be(AvailabilityOutcomeCodes.OptionsAvailable);
        outcome.Presentations.Should().ContainSingle();
        outcome.Presentations[0].TemplateId.Should().Be("availability_slots");
        outcome.Presentations[0].Mode.Should().Be(FragmentRenderMode.Exclusive);
        outcome.Presentations[0].Priority.Should().Be(FragmentPriority.Required);
        outcome.Effects.Should().BeEmpty();
    }

    [Fact]
    public async Task WithAvailableTime_ReturnsExactOutcomeWithoutTemplateAndSavesVerification()
    {
        var result = new AvailabilityResult
        {
            IsAvailable = true,
            RequestServiceName = "Corte infantil",
            RequestDateString = "2026-07-11",
            RequestTimeString = "10:00",
            Option = new AvailabilityOption("10:00", "11:00")
        };

        var outcome = await BuildOperation(result).ExecuteAsync(
            Json(new { service = "Corte infantil", date = "2026-07-11", time = "10:00" }),
            Context());

        outcome.Code.Should().Be(AvailabilityOutcomeCodes.ExactTimeAvailable);
        outcome.Presentations.Should().BeEmpty();
        outcome.Effects.Should().ContainSingle().Which.Should().BeOfType<SaveVerificationEffect>();
        var verification = (SaveVerificationEffect)outcome.Effects[0];
        verification.VerificationType.Should().Be(VerificationFactTypes.AvailabilityChecked);
        verification.Dependencies["service"].Should().Be("Corte infantil");
        verification.Dependencies["desired_date"].Should().Be("2026-07-11");
        verification.Dependencies["desired_time"].Should().Be("10:00");
    }

    [Fact]
    public async Task WithUnavailableTimeAndAlternatives_ReturnsExclusiveTemplateWithIntro()
    {
        var result = AvailableOptions("11:00", "12:00");
        result.IsAvailable = false;
        result.RequestTimeString = "10:00";
        result.RequestedOption = new AvailabilityOption("10:00", "11:00");

        var outcome = await BuildOperation(result).ExecuteAsync(
            Json(new { service = "Corte infantil", date = "2026-07-11", time = "10:00" }),
            Context());

        outcome.Code.Should().Be(AvailabilityOutcomeCodes.RequestedTimeUnavailable);
        outcome.Presentations.Should().ContainSingle();
        outcome.Presentations[0].Mode.Should().Be(FragmentRenderMode.Exclusive);
        outcome.Presentations[0].Data.Should().ContainKey("intro_message");
        outcome.Effects.Should().BeEmpty();
    }

    private static CheckAvailabilityOperation BuildOperation(AvailabilityResult result)
    {
        var service = new Service
        {
            ServiceId = Guid.NewGuid(), BusinessId = BusinessId,
            ServiceName = "Corte infantil", DurationMinutes = 60, IsActive = true
        };
        var services = new Mock<IServiceRepository>();
        services.Setup(repository => repository.GetActiveByBusinessIdAsync(BusinessId)).ReturnsAsync([service]);
        services.Setup(repository => repository.GetByBusinessIdAndNameAsync(BusinessId, service.ServiceName)).ReturnsAsync(service);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Services).Returns(services.Object);
        var availability = new Mock<IAvailabilityService>();
        availability.Setup(value => value.CheckAvailabilityAsync(
                BusinessId, service.ServiceName, It.IsAny<DateTime>(), It.IsAny<TimeSpan?>(),
                It.IsAny<AvailabilityParams?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        var scheduling = new Mock<ISchedulingPolicyProvider>();
        scheduling.Setup(value => value.GetAsync(BusinessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvailabilityParams.Default);
        var employees = new Mock<IEmployeeAssignmentService>();
        employees.Setup(value => value.FindBestAvailableEmployeeAsync(
                BusinessId, service.ServiceId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ReturnsAsync((Employee?)null);

        return new CheckAvailabilityOperation(
            availability.Object,
            scheduling.Object,
            employees.Object,
            unitOfWork.Object,
            new ServiceNameResolver(unitOfWork.Object, Mock.Of<ILogger<ServiceNameResolver>>()));
    }

    private static AvailabilityResult AvailableOptions(params string[] starts) => new()
    {
        IsAvailable = true,
        RequestServiceName = "Corte infantil",
        RequestDateString = "2026-07-11",
        AvailableOptions = starts.Select(start =>
            new AvailabilityOption(start, TimeOnly.Parse(start).AddHours(1).ToString("HH:mm"))).ToList()
    };

    private static OperationContext Context() => new()
    {
        AgentId = Guid.NewGuid(), BusinessId = BusinessId, ConversationId = Guid.NewGuid(),
        BusinessToday = new DateOnly(2026, 7, 10),
        BusinessNow = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.FromHours(-5)),
        Config = new AgentConfig
        {
            FactSchema =
            [
                Fact("service", "booking.service"),
                Fact("desired_date", "booking.date"),
                Fact("desired_time", "booking.time")
            ]
        }
    };

    private static FactSchemaEntry Fact(string key, string role) => new()
    {
        Key = key, Role = role, Label = key, Type = "string", Source = "user"
    };

    private static JsonElement Json(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private static readonly Guid BusinessId = Guid.Parse("9A93FC1B-255D-4C80-B3A3-27EF42CF08CD");
}
