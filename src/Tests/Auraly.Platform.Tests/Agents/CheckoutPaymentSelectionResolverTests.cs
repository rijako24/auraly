using FluentAssertions;
using Auraly.Platform.Application.Agents.Configuration;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class CheckoutPaymentSelectionResolverTests
{
    [Fact]
    public void Resolve_WithSingleMethod_AutoSelectsMethod()
    {
        var mode = new CheckoutModeDefinition
        {
            PaymentMethods =
            {
                ["transferencia"] = new CheckoutPaymentMethodDefinition
                {
                    Label = "transferencia con link de pago",
                    Payment = new CheckoutPaymentDefinition { Percentage = 50 },
                    Template = "checkout_with_deposit",
                    ConfirmationOutcome = "reservation_created"
                }
            }
        };

        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, "reservation", 100_000, rawPaymentMethod: null);

        selection.Error.Should().BeNull();
        selection.MissingPaymentMethod.Should().BeFalse();
        selection.MethodKey.Should().Be("transferencia");
        selection.PayableCents.Should().Be(50_000);
        selection.TemplateId.Should().Be("checkout_with_deposit");
        selection.ConfirmationOutcome.Should().Be("reservation_created");
    }

    [Fact]
    public void Resolve_WithSingleMethodAndInvalidExplicitInput_ReturnsRecoverableError()
    {
        var mode = new CheckoutModeDefinition
        {
            PaymentMethods =
            {
                ["transferencia"] = new CheckoutPaymentMethodDefinition
                {
                    Label = "transferencia con link de pago",
                    Payment = new CheckoutPaymentDefinition { Percentage = 100 },
                    Template = "checkout_with_deposit",
                    ConfirmationOutcome = "reservation_created"
                }
            }
        };

        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, "reservation", 25_000, rawPaymentMethod: "default");

        selection.MissingPaymentMethod.Should().BeFalse();
        selection.Error.Should().NotBeNull();
        selection.Error!.Code.Should().Be("invalid_payment_method");
        selection.Error.Recoverable.Should().BeTrue();
        selection.Error.AvailablePaymentMethods.Should().Equal("transferencia con link de pago");
    }

    [Fact]
    public void Resolve_WithMultipleMethodsAndNoInput_RequiresPaymentMethod()
    {
        var mode = new CheckoutModeDefinition
        {
            PaymentMethods =
            {
                ["efectivo"] = new CheckoutPaymentMethodDefinition { Template = "cash_template" },
                ["transferencia"] = new CheckoutPaymentMethodDefinition
                {
                    Payment = new CheckoutPaymentDefinition { Percentage = 100 },
                    Template = "payment_template",
                    ConfirmationOutcome = "order_paid"
                }
            }
        };

        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, "order", 100_000, rawPaymentMethod: null);

        selection.MissingPaymentMethod.Should().BeTrue();
        selection.Error.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithNoPaymentMethodPayment_DoesNotRequirePayment()
    {
        var mode = new CheckoutModeDefinition
        {
            PaymentMethods =
            {
                ["efectivo"] = new CheckoutPaymentMethodDefinition
                {
                    Label = "efectivo al recibir",
                    Aliases = ["cash"],
                    Template = "order_checkout_no_payment"
                }
            }
        };

        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, "order", 100_000, "cash");

        selection.Error.Should().BeNull();
        selection.RequiresPayment.Should().BeFalse();
        selection.PayableCents.Should().Be(0);
        selection.TemplateId.Should().Be("order_checkout_no_payment");
        selection.ConfirmationOutcome.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WithInvalidPercentage_ReturnsConfigurationError()
    {
        var mode = new CheckoutModeDefinition
        {
            PaymentMethods =
            {
                ["transferencia"] = new CheckoutPaymentMethodDefinition
                {
                    Payment = new CheckoutPaymentDefinition { Percentage = 0 },
                    Template = "payment_template",
                    ConfirmationOutcome = "order_paid"
                }
            }
        };

        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, "order", 100_000, "transferencia");

        selection.Error.Should().NotBeNull();
        selection.Error!.Code.Should().Be("checkout_payment_percentage_invalid");
    }

    [Fact]
    public void Resolve_WithManualConfirmationMethod_DefaultsToFullPendingPayment()
    {
        var mode = new CheckoutModeDefinition
        {
            PaymentMethods =
            {
                ["transferencia"] = new CheckoutPaymentMethodDefinition
                {
                    Label = "transferencia manual",
                    ManualConfirmationRequired = true,
                    ManualExpirationMinutes = 1440,
                    Template = "order_checkout_manual_transfer",
                    ConfirmationOutcome = "order_paid"
                }
            }
        };

        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, "order", 100_000, "transferencia");

        selection.Error.Should().BeNull();
        selection.RequiresManualConfirmation.Should().BeTrue();
        selection.RequiresPaymentLink.Should().BeFalse();
        selection.PayableCents.Should().Be(100_000);
        selection.PaymentPercentage.Should().Be(100);
        selection.TemplateId.Should().Be("order_checkout_manual_transfer");
        selection.ConfirmationOutcome.Should().Be("order_paid");
        selection.ManualExpirationMinutes.Should().Be(1440);
    }}
