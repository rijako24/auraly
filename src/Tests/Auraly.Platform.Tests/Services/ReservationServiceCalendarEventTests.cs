using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Services;

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
    public void BuildCalendarEvent_UsesOnlyCollectedInfoFromReservationAttributeEnvelope()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.CustomerName] = "Geraldine",
            [ConversationFactKeys.Service] = "Demo AURALY",
            [ConversationFactKeys.DesiredDate] = "2026-07-07",
            [ConversationFactKeys.DesiredTime] = "16:00",
            ["company_name"] = "Luna Bebe",
            ["business_type"] = "Spa",
            ["pain_point"] = "Agendar demos desde WhatsApp",
            ["business_profile_url"] = "@lunabebe.qa",
            ["availability_checked"] = "true"
        };
        var schema = new List<FactSchemaEntry>
        {
            new() { Key = ConversationFactKeys.CustomerName, Label = "nombre del cliente", Source = "user" },
            new() { Key = ConversationFactKeys.Service, Label = "servicio", Source = "user" },
            new() { Key = ConversationFactKeys.DesiredDate, Label = "fecha deseada", Source = "user" },
            new() { Key = ConversationFactKeys.DesiredTime, Label = "hora deseada", Source = "user" },
            new() { Key = "company_name", Label = "empresa", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "business_type", Label = "Tipo de negocio", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "pain_point", Label = "Problematica que quiere resolver", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "business_profile_url", Label = "Facebook e Instagram", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "availability_checked", Label = "disponibilidad validada", Source = "system" }
        };
        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ReservationDateTime = new DateTime(2026, 7, 7, 16, 0, 0),
            DurationMinutes = 60,
            Status = ReservationStatus.Confirmed,
            CustomerNameSnapshot = "Geraldine",
            CustomerPhoneSnapshot = "573042052007",
            CustomAttributesJson = ReservationCustomAttributes.BuildJson(facts, schema)
        };

        var calendarEvent = BuildCalendarEvent(reservation, "Demo AURALY", []);

        calendarEvent.Description.Should().Contain("Servicio: Demo AURALY");
        calendarEvent.Description.Should().Contain("Fecha: 07/07/2026");
        calendarEvent.Description.Should().Contain("Hora: 16:00");
        calendarEvent.Description.Should().Contain("empresa: Luna Bebe");
        calendarEvent.Description.Should().Contain("Tipo de negocio: Spa");
        calendarEvent.Description.Should().Contain("Problematica que quiere resolver: Agendar demos desde WhatsApp");
        calendarEvent.Description.Should().Contain("Facebook e Instagram: @lunabebe.qa");
        calendarEvent.Description.Should().NotContain("nombre del cliente: Geraldine");
        calendarEvent.Description.Should().NotContain("servicio: Demo AURALY");
        calendarEvent.Description.Should().NotContain("fecha deseada: 2026-07-07");
        calendarEvent.Description.Should().NotContain("hora deseada: 16:00");
        calendarEvent.Description.Should().NotContain("disponibilidad validada: true");
    }

    [Fact]
    public void ReservationCustomAttributes_BuildJson_PersistsUserFactsAndSeparatesCollectedInfo()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConversationFactKeys.CustomerName] = "Geraldine",
            [ConversationFactKeys.Service] = "Demo AURALY",
            [ConversationFactKeys.DesiredDate] = "2026-07-07",
            [ConversationFactKeys.DesiredTime] = "16:00",
            ["company_name"] = "Luna Bebe",
            ["business_type"] = "Spa",
            ["pain_point"] = "Agendar demos desde WhatsApp",
            ["business_profile_url"] = "@lunabebe.qa",
            ["availability_checked"] = "true",
            ["system.internal_context"] = "web"
        };
        var schema = new List<FactSchemaEntry>
        {
            new() { Key = ConversationFactKeys.CustomerName, Role = "customer.name", Label = "nombre del cliente", Source = "user" },
            new() { Key = ConversationFactKeys.Service, Role = "booking.service", Label = "servicio", Source = "user" },
            new() { Key = ConversationFactKeys.DesiredDate, Role = "booking.date", Label = "fecha deseada", Source = "user" },
            new() { Key = ConversationFactKeys.DesiredTime, Role = "booking.time", Label = "hora deseada", Source = "user" },
            new() { Key = "company_name", Label = "empresa", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "business_type", Label = "Tipo de negocio", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "pain_point", Label = "Problematica que quiere resolver", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "business_profile_url", Label = "Facebook e Instagram", Source = "user", ShowInCollectedInfo = true },
            new() { Key = "availability_checked", Label = "disponibilidad validada", Source = "system" },
            new() { Key = "system.internal_context", Label = "contexto", Source = "session" }
        };

        var json = ReservationCustomAttributes.BuildJson(facts, schema);
        var payload = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json)!;

        payload[ReservationCustomAttributes.AttributesPropertyName].Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [ConversationFactKeys.CustomerName] = "Geraldine",
            [ConversationFactKeys.Service] = "Demo AURALY",
            [ConversationFactKeys.DesiredDate] = "2026-07-07",
            [ConversationFactKeys.DesiredTime] = "16:00",
            ["company_name"] = "Luna Bebe",
            ["business_type"] = "Spa",
            ["pain_point"] = "Agendar demos desde WhatsApp",
            ["business_profile_url"] = "@lunabebe.qa"
        });
        payload[ReservationCustomAttributes.CollectedInfoPropertyName].Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["empresa"] = "Luna Bebe",
            ["Tipo de negocio"] = "Spa",
            ["Problematica que quiere resolver"] = "Agendar demos desde WhatsApp",
            ["Facebook e Instagram"] = "@lunabebe.qa"
        });
        json.Should().NotContain("availability_checked");
        json.Should().NotContain("system.internal_context");
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
