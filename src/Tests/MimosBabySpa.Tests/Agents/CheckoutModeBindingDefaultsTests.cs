using FluentAssertions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CheckoutModeBindingDefaultsTests
{
    [Fact]
    public void ResolveMode_WithCaseSensitiveDictionary_MatchesIgnoringCase()
    {
        var reservationMode = new CheckoutModeDefinition();
        var checkout = new CheckoutDefinitions
        {
            Modes = new Dictionary<string, CheckoutModeDefinition>
            {
                ["reservation"] = reservationMode
            }
        };

        checkout.ResolveMode("Reservation").Should().BeSameAs(reservationMode);
    }

    [Fact]
    public void Resolve_ForReservation_ReturnsRoleBasedDefaults()
    {
        var bindings = CheckoutModeBindingDefaults.Resolve(
            CheckoutKind.Reservation,
            new CheckoutModeDefinition());

        bindings.RequiredFactRoles.Should().Contain("reservation_date", "booking.date");
        bindings.RequiredFactRoles.Should().Contain("reservation_time", "booking.time");
        bindings.SystemFactBindings.Should().Contain("payment_phone", "customer.phone");
        bindings.TemplateFactBindings.Should().Contain("date_formatted", "booking.date");
        bindings.TemplateFactBindings.Should().Contain("customer_name", "customer.name");
    }

    [Fact]
    public void Resolve_ForEnrollment_ReturnsFixedScheduleDefaults()
    {
        var bindings = CheckoutModeBindingDefaults.Resolve(
            CheckoutKind.Enrollment,
            new CheckoutModeDefinition());

        bindings.RequiredFactRoles.Should().Contain("fixed_schedule", "checkout.fixed_schedule");
        bindings.SystemFactBindings.Should().Contain("fixed_schedule", "checkout.fixed_schedule");
        bindings.TemplateFactBindings.Should().Contain("fixed_schedule", "checkout.fixed_schedule");
        bindings.TemplateFactBindings.Should().Contain("baby_birth_date", "baby.birth_date");
    }

    [Fact]
    public void Resolve_WithOverrides_MergesAdvancedBindings()
    {
        var mode = new CheckoutModeDefinition
        {
            TemplateFactBindings =
            {
                ["customer_phone"] = "customer.whatsapp"
            }
        };

        var bindings = CheckoutModeBindingDefaults.Resolve(CheckoutKind.Reservation, mode);

        bindings.TemplateFactBindings["customer_phone"].Should().Be("customer.whatsapp");
        bindings.TemplateFactBindings.Should().Contain("date", "booking.date");
    }
}
