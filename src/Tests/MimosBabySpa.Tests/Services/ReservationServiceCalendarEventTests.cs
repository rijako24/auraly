using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public class ReservationServiceCalendarEventTests
{
    [Fact]
    public void BuildCalendarEvent_OnlyIncludesReservationDataAndRelevantBusinessFacts()
    {
        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ReservationDateTime = new DateTime(2026, 7, 7, 16, 0, 0),
            DurationMinutes = 60,
            Status = ReservationStatus.Confirmed,
            CustomerNameSnapshot = "Geraldine",
            CustomerPhoneSnapshot = "573042052007",
            CustomerEmailSnapshot = "geraldine@example.com",
            CustomAttributesJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["Tipo de negocio"] = "Spa",
                ["Problematica que quiere resolver"] = "Agendar demos desde WhatsApp"
            })
        };

        var calendarEvent = BuildCalendarEvent(reservation, "Demo AURALY", []);

        calendarEvent.Description.Should().Contain("Servicio: Demo AURALY");
        calendarEvent.Description.Should().Contain("Cliente: Geraldine");
        calendarEvent.Description.Should().Contain("Telefono: 573042052007");
        calendarEvent.Description.Should().Contain("Correo: geraldine@example.com");
        calendarEvent.Description.Should().Contain("Tipo de negocio: Spa");
        calendarEvent.Description.Should().Contain("Problematica que quiere resolver: Agendar demos desde WhatsApp");

        calendarEvent.Description.Should().NotContain("business_type");
        calendarEvent.Description.Should().NotContain("pain_point");
        calendarEvent.Description.Should().NotContain("main_channel");
        calendarEvent.Description.Should().NotContain("conversation_volume");
        calendarEvent.Description.Should().NotContain("irrelevant_internal_note");
        calendarEvent.Description.Should().NotContain("CustomerName:");
        calendarEvent.Description.Should().NotContain("Phone:");
        calendarEvent.Description.Should().NotContain("Email:");
    }

    [Fact]
    public void ReservationCustomAttributes_BuildJson_UsesOnlyFactsMarkedForCollectedInfo()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.CustomerName] = "Geraldine",
            [ConversationFactKeys.Service] = "Demo AURALY",
            ["business_type"] = "Spa",
            ["pain_point"] = "Agendar demos desde WhatsApp",
            ["availability_checked"] = "true",
            ["session.engagement"] = "web"
        };
        var schema = new List<FactSchemaEntry>
        {
            new() { Key = ConversationFactKeys.CustomerName, Role = "customer.name", Label = "nombre del cliente", Source = "user" },
            new() { Key = ConversationFactKeys.Service, Role = "booking.service", Label = "servicio", Source = "user" },
            new() { Key = "business_type", Label = "Tipo de negocio", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "pain_point", Label = "Problematica que quiere resolver", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "availability_checked", Label = "disponibilidad validada", Source = "system" },
            new() { Key = "session.engagement", Label = "contexto", Source = "session" }
        };

        var json = ReservationCustomAttributes.BuildJson(facts, schema);
        var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

        custom.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Tipo de negocio"] = "Spa",
            ["Problematica que quiere resolver"] = "Agendar demos desde WhatsApp"
        });
    }

    private static CalendarEvent BuildCalendarEvent(
        Reservation reservation,
        string serviceName,
        Dictionary<string, string> metadata)
    {
        var method = typeof(ReservationService).GetMethod(
            "BuildCalendarEvent",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (CalendarEvent)method!.Invoke(null, [reservation, serviceName, metadata])!;
    }
}
