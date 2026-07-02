using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
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
            ["baby_age_months"] = "5",
            ["service"] = "Plan Marineritos",
            ["desired_date"] = "2026-06-20"
        };

        var factService = new Mock<IConversationFactsService>();
        factService.Setup(f => f.GetAllRecordsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Record("baby_age_months", "5"),
                Record("service", "Plan Marineritos"),
                Record("desired_date", "2026-06-20")
            ]);
        factService.Setup(f => f.ClearFieldsAsync(
                conversationId,
                It.Is<IReadOnlyCollection<string>>(keys =>
                    keys.Contains("service") && keys.Contains("desired_date") && !keys.Contains("baby_age_months")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["service", "desired_date"]);

        var state = new ConversationState();
        state.Verifications["availability_checked"] = new VerificationEntry(DateTime.UtcNow, null, "{}");
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

        result.ClearedFacts.Should().BeEquivalentTo(["service", "desired_date"]);
        facts.Should().Contain("baby_age_months", "5");
        facts.Should().NotContainKey("service");
        facts.Should().NotContainKey("desired_date");
        state.Verifications.Should().BeEmpty();
        state.StageFactSnapshots.Should().BeEmpty();
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
    }

    private static AgentConfig CreateConfig() => new()
    {
        FactSchema =
        [
            new FactSchemaEntry
            {
                Key = "baby_age_months",
                Role = "baby.age_months",
                Scope = FactScopes.Customer,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = "service",
                Role = "booking.service",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = "desired_date",
                Role = "booking.date",
                Scope = FactScopes.Request,
                RetentionDays = 7
            },
            new FactSchemaEntry
            {
                Key = "desired_time",
                Role = "booking.time",
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
