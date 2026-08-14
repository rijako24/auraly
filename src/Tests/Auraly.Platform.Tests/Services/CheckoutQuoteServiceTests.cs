using FluentAssertions;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class CheckoutQuoteServiceTests
{
    private readonly CheckoutQuoteService _service = new();

    [Fact]
    public void ComputeHash_WithSameBillableContent_IgnoresRuntimeMetadata()
    {
        var quote = CreateQuote();
        var sameBillableQuote = quote with
        {
            ConversationId = Guid.NewGuid(),
            IssuedAtUtc = quote.IssuedAtUtc.AddMinutes(5),
            ExpiresAtUtc = quote.ExpiresAtUtc.AddMinutes(5),
            SystemFactBindings = new Dictionary<string, string> { ["reservation_date"] = "booking.date" },
            TemplateFactBindings = new Dictionary<string, string> { ["date_formatted"] = "booking.date" }
        };

        _service.ComputeHash(sameBillableQuote).Should().Be(_service.ComputeHash(quote));
    }

    [Fact]
    public void ComputeHash_WithDifferentBillableContent_ChangesHash()
    {
        var quote = CreateQuote();
        var quoteWithAddOn = quote with
        {
            LineItems =
            [
                new CheckoutQuoteLineItem("Corte basico de adulto", 30000m),
                new CheckoutQuoteLineItem("Mascarilla de carbono", 15000m)
            ],
            TotalCents = 4500000,
            PayableCents = 4500000
        };

        _service.ComputeHash(quoteWithAddOn).Should().NotBe(_service.ComputeHash(quote));
    }

    private static CheckoutQuote CreateQuote() =>
        new(
            BusinessId: Guid.NewGuid(),
            ConversationId: Guid.NewGuid(),
            CheckoutKind: CheckoutKind.Reservation,
            ServiceId: Guid.NewGuid(),
            ServiceName: "Corte basico de adulto",
            ServiceCategory: "Corte",
            DurationMinutes: 30,
            LineItems: [new CheckoutQuoteLineItem("Corte basico de adulto", 30000m)],
            TotalCents: 3000000,
            PayableCents: 3000000,
            Currency: "COP",
            PaymentMethodKey: "transfer",
            PaymentMethodLabel: "Transferencia",
            PaymentPercentage: 100,
            TemplateId: "checkout_with_deposit",
            ConfirmationOutcome: "reservation",
            RequiredFactRoles: new Dictionary<string, string>(),
            SystemFactBindings: new Dictionary<string, string>(),
            TemplateFactBindings: new Dictionary<string, string>(),
            IssuedAtUtc: DateTime.UtcNow,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30));
}
