using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class RequestContextServiceTests
{
    [Fact]
    public async Task CompleteAsync_ClearsRequestFactsAndKeepsCustomerFacts()
    {
        var conversationId = Guid.NewGuid();
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.CustomerName] = "Richard",
            [ConversationFactKeys.CustomerPhone] = "573012926660",
            [ConversationFactKeys.Service] = "Plan Marineritos",
            [ConversationFactKeys.DesiredDate] = "2026-06-20",
            [ConversationFactKeys.DesiredTime] = "08:00",
            ["availability_checked"] = "true",
            ["payment_method"] = "cash",
            [ConversationFactKeys.AddOns] = "Decoracion sencilla"
        };

        var factService = new Mock<IConversationFactsService>();
        factService.Setup(f => f.GetAllRecordsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Record(ConversationFactKeys.CustomerName, "Richard"),
                Record(ConversationFactKeys.CustomerPhone, "573012926660"),
                Record(ConversationFactKeys.Service, "Plan Marineritos"),
                Record(ConversationFactKeys.DesiredDate, "2026-06-20"),
                Record(ConversationFactKeys.DesiredTime, "08:00"),
                Record("availability_checked", "true"),
                Record("payment_method", "cash"),
                Record(ConversationFactKeys.AddOns, "Decoracion sencilla")
            ]);
        factService.Setup(f => f.ClearFieldsAsync(
                conversationId,
                It.Is<IReadOnlyCollection<string>>(keys =>
                    keys.Contains(ConversationFactKeys.Service)
                    && keys.Contains(ConversationFactKeys.DesiredDate)
                    && keys.Contains(ConversationFactKeys.DesiredTime)
                    && keys.Contains("availability_checked")
                    && keys.Contains("payment_method")
                    && keys.Contains(ConversationFactKeys.AddOns)
                    && !keys.Contains(ConversationFactKeys.CustomerName)
                    && !keys.Contains(ConversationFactKeys.CustomerPhone)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                ConversationFactKeys.Service,
                ConversationFactKeys.DesiredDate,
                ConversationFactKeys.DesiredTime,
                "availability_checked",
                "payment_method",
                ConversationFactKeys.AddOns
            ]);

        var state = new ConversationState();
        state.Verifications["availability_checked"] = new VerificationEntry(
            DateTime.UtcNow,
            null,
            VerificationSnapshot.Serialize(VerificationSnapshot.Of(
                facts,
                ConversationFactKeys.Service,
                ConversationFactKeys.DesiredDate,
                ConversationFactKeys.DesiredTime)));
        state.StageFactSnapshots["scheduling"] = new Dictionary<string, string> { ["service"] = "Plan Marineritos" };

        var service = new RequestContextService(
            factService.Object,
            NullLogger<RequestContextService>.Instance);

        var result = await service.CompleteAsync(
            conversationId,
            CreateConfig(),
            state,
            facts,
            "reservation_created",
            CancellationToken.None);

        result.ClearedFacts.Should().BeEquivalentTo([
            ConversationFactKeys.Service,
            ConversationFactKeys.DesiredDate,
            ConversationFactKeys.DesiredTime,
            "availability_checked",
            "payment_method",
            ConversationFactKeys.AddOns
        ]);
        facts.Should().Contain(ConversationFactKeys.CustomerName, "Richard");
        facts.Should().Contain(ConversationFactKeys.CustomerPhone, "573012926660");
        facts.Should().NotContainKey(ConversationFactKeys.Service);
        facts.Should().NotContainKey(ConversationFactKeys.DesiredDate);
        facts.Should().NotContainKey(ConversationFactKeys.DesiredTime);
        facts.Should().NotContainKey("availability_checked");
        facts.Should().NotContainKey("payment_method");
        facts.Should().NotContainKey(ConversationFactKeys.AddOns);
        state.Verifications.Should().BeEmpty();
        state.StageFactSnapshots.Should().BeEmpty();
        state.ActiveRequestStartedAtUtc.Should().NotBeNull();

        facts[ConversationFactKeys.Service] = "Corte Kids";
        facts[ConversationFactKeys.DesiredDate] = "2026-06-21";
        facts[ConversationFactKeys.DesiredTime] = "10:00";

        var verifications = new ConversationVerificationService();
        var toolContext = new AgentToolContext
        {
            BusinessId = Guid.NewGuid(),
            ConversationId = conversationId,
            ConversationState = state,
            Facts = facts
        };

        verifications.Record(
            toolContext,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.Of(
                facts,
                ConversationFactKeys.Service,
                ConversationFactKeys.DesiredDate,
                ConversationFactKeys.DesiredTime),
            null);

        verifications.IsActive(state, VerificationFactTypes.AvailabilityChecked, facts)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ApplyRetentionAsync_OnNewBusinessDay_RetainsFreshServiceAndClearsPastDate()
    {
        var conversationId = Guid.NewGuid();
        var lastActivity = new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["baby_age_months"] = "5",
            ["service"] = "Plan Marineritos",
            ["desired_date"] = "2026-06-16",
            ["desired_time"] = "09:00",
            ["availability_checked"] = "true"
        };

        var factService = new Mock<IConversationFactsService>();
        factService.Setup(f => f.GetAllRecordsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Record("baby_age_months", "5", now.UtcDateTime.AddDays(-1)),
                Record("service", "Plan Marineritos", now.UtcDateTime.AddDays(-1)),
                Record("desired_date", "2026-06-16", now.UtcDateTime.AddDays(-1)),
                Record("desired_time", "09:00", now.UtcDateTime.AddDays(-1)),
                Record("availability_checked", "true", now.UtcDateTime.AddDays(-1))
            ]);
        factService.Setup(f => f.ClearFieldsAsync(
                conversationId,
                It.Is<IReadOnlyCollection<string>>(keys =>
                    keys.Contains("desired_date")
                    && keys.Contains("desired_time")
                    && keys.Contains("availability_checked")
                    && !keys.Contains("service")
                    && !keys.Contains("baby_age_months")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["desired_date", "desired_time", "availability_checked"]);

        var state = new ConversationState();
        state.Verifications["availability_checked"] = new VerificationEntry(DateTime.UtcNow, null, "{}");

        var service = new RequestContextService(
            factService.Object,
            NullLogger<RequestContextService>.Instance);

        var result = await service.ApplyRetentionAsync(
            new Conversation
            {
                ConversationId = conversationId,
                LastActivityAt = lastActivity
            },
            CreateConfig(),
            state,
            facts,
            new BusinessClockSnapshot(Guid.NewGuid(), now, new DateOnly(2026, 6, 17), TimeZoneInfo.Utc),
            CancellationToken.None);

        result.BusinessDayChanged.Should().BeTrue();
        result.ClearedFacts.Should().BeEquivalentTo(["desired_date", "desired_time", "availability_checked"]);
        facts.Should().Contain("service", "Plan Marineritos");
        facts.Should().Contain("baby_age_months", "5");
        facts.Should().NotContainKey("desired_date");
        facts.Should().NotContainKey("desired_time");
        facts.Should().NotContainKey("availability_checked");
        state.Verifications.Should().BeEmpty();
        state.ActiveRequestStartedAtUtc.Should().Be(now.UtcDateTime);
    }

    [Fact]
    public async Task ApplyRetentionAsync_WhenOnlyEphemeralFactClears_DoesNotMoveActiveRequestBoundary()
    {
        var conversationId = Guid.NewGuid();
        var lastActivity = new DateTime(2026, 6, 16, 14, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);
        var originalBoundary = now.UtcDateTime.AddDays(-3);
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["availability_checked"] = "true"
        };

        var factService = new Mock<IConversationFactsService>();
        factService.Setup(f => f.GetAllRecordsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record("availability_checked", "true", now.UtcDateTime.AddDays(-2))]);
        factService.Setup(f => f.ClearFieldsAsync(
                conversationId,
                It.Is<IReadOnlyCollection<string>>(keys => keys.Contains("availability_checked")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["availability_checked"]);

        var state = new ConversationState { ActiveRequestStartedAtUtc = originalBoundary };
        state.Verifications["availability_checked"] = new VerificationEntry(DateTime.UtcNow, null, "{}");

        var service = new RequestContextService(
            factService.Object,
            NullLogger<RequestContextService>.Instance);

        var result = await service.ApplyRetentionAsync(
            new Conversation
            {
                ConversationId = conversationId,
                LastActivityAt = lastActivity
            },
            CreateConfig(),
            state,
            facts,
            new BusinessClockSnapshot(Guid.NewGuid(), now, new DateOnly(2026, 6, 17), TimeZoneInfo.Utc),
            CancellationToken.None);

        result.ClearedFacts.Should().BeEquivalentTo(["availability_checked"]);
        facts.Should().NotContainKey("availability_checked");
        state.Verifications.Should().BeEmpty();
        state.ActiveRequestStartedAtUtc.Should().Be(originalBoundary);
    }

    private static AgentConfig CreateConfig() => new()
    {
        FactSchema =
        [
            new FactSchemaEntry
            {
                Key = ConversationFactKeys.CustomerName,
                Role = "customer.name",
                Scope = FactScopes.Customer,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = ConversationFactKeys.CustomerPhone,
                Role = "customer.phone",
                Scope = FactScopes.Customer,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = "baby_age_months",
                Role = "baby.age_months",
                Scope = FactScopes.Customer,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = ConversationFactKeys.Service,
                Role = "booking.service",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = ConversationFactKeys.DesiredDate,
                Role = "booking.date",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = ConversationFactKeys.DesiredTime,
                Role = "booking.time",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = "payment_method",
                Role = "checkout.payment_method",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = ConversationFactKeys.AddOns,
                Role = "booking.add_ons",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = "availability_checked",
                Role = "booking.availability_checked",
                Source = "system",
                Scope = FactScopes.Ephemeral,
                RetentionDays = 1,
                DependsOn = ["service", "desired_date", "desired_time"]
            }
        ]
    };

    private static ConversationFactRecord Record(
        string key,
        string value,
        DateTime? touchedAt = null)
    {
        var timestamp = touchedAt ?? DateTime.UtcNow;
        return new ConversationFactRecord(key, value, timestamp, timestamp);
    }
}
