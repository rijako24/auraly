using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Internal;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Console;

internal static class PaymentApprovalConsoleScenario
{
    public static async Task<int> RunAsync()
    {
        var businessId = Guid.NewGuid();
        var first = Payment(businessId, "PED-1001", 95_000);
        var second = Payment(businessId, "PED-1002", 175_000);
        var payments = new List<PaymentTransaction> { first, second };

        var repository = PaymentRepositoryProxy.Create(payments);
        var unitOfWork = UnitOfWorkProxy.Create(repository);
        var confirmation = PaymentConfirmationProxy.Create(payments);
        using var serviceProvider = new ServiceCollection()
            .AddSingleton(confirmation)
            .BuildServiceProvider();
        var confirmOperation = new ConfirmManualPaymentOperation(unitOfWork, serviceProvider);
        var searchOperation = new SearchManualPaymentsOperation(unitOfWork);
        var context = new OperationContext
        {
            AgentId = Guid.NewGuid(),
            BusinessId = businessId,
            Session = new AgentConversationContext
            {
                BusinessId = businessId,
                ChannelPhone = "+573001112233"
            }
        };

        var secondButton = $"manual_payment:confirm:{second.PaymentTransactionId}";
        if (!InteractivePayloadParser.TryParse(secondButton, out var action)
            || action.Scope != "manual_payment"
            || action.Outcome != "confirm")
        {
            return Fail("No se pudo interpretar el payload del boton.");
        }

        using (var input = JsonDocument.Parse($$"""{"payment_transaction_id":"{{action.SourceId}}"}"""))
        {
            var buttonResult = await confirmOperation.ExecuteAsync(input.RootElement, context);
            if (buttonResult.Code != "payment.confirmed"
                || first.Status != PaymentTransactionStatus.Created
                || second.Status != PaymentTransactionStatus.Confirmed)
            {
                return Fail("El boton no confirmo exclusivamente el pago PED-1002.");
            }
        }

        using (var repeatedInput = JsonDocument.Parse($$"""{"payment_transaction_id":"{{second.PaymentTransactionId}}"}"""))
        {
            var repeated = await confirmOperation.ExecuteAsync(repeatedInput.RootElement, context);
            if (repeated.Code != "payment.already_confirmed")
                return Fail("La repeticion del mismo boton no fue idempotente.");
        }

        using (var empty = JsonDocument.Parse("{}"))
        {
            var review = await searchOperation.ExecuteAsync(empty.RootElement, context);
            if (review.Code != "payment.single_pending"
                || review.Data.GetProperty("selected_payment_transaction_id").GetString() != first.PaymentTransactionId.ToString())
            {
                return Fail("La consulta sin identificador no preparo el unico pago pendiente para revision.");
            }
        }

        using (var spoken = JsonDocument.Parse("""{"query":"PED-1001"}"""))
        {
            var spokenResult = await confirmOperation.ExecuteAsync(spoken.RootElement, context);
            if (spokenResult.Code != "payment.confirmed" || first.Status != PaymentTransactionStatus.Confirmed)
                return Fail("La confirmacion hablada no resolvio PED-1001.");
        }

        global::System.Console.WriteLine("PASS boton correlacionado: PED-1002 confirmado sin afectar PED-1001.");
        global::System.Console.WriteLine("PASS boton repetido: respuesta idempotente payment.already_confirmed.");
        global::System.Console.WriteLine("PASS solicitud ambigua: el unico pendiente se muestra antes de confirmar.");
        global::System.Console.WriteLine("PASS confirmacion hablada: PED-1001 resuelto y confirmado por numero de pedido.");
        return 0;
    }

    private static int Fail(string message)
    {
        global::System.Console.Error.WriteLine($"FAIL {message}");
        return 1;
    }

    private static PaymentTransaction Payment(Guid businessId, string orderNumber, long amountInCents) => new()
    {
        PaymentTransactionId = Guid.NewGuid(),
        BusinessId = businessId,
        ConversationId = Guid.NewGuid(),
        PaymentReferenceId = $"manual-order-{Guid.NewGuid():N}",
        LinkUrl = string.Empty,
        AmountInCents = amountInCents,
        Currency = "COP",
        Status = PaymentTransactionStatus.Created,
        ExpiresAt = DateTime.UtcNow.AddHours(1),
        CreatedAt = DateTime.UtcNow,
        CheckoutKind = CheckoutKind.Order,
        CheckoutSnapshotJson = JsonSerializer.Serialize(new
        {
            order_number = orderNumber,
            payer_name = $"Cliente {orderNumber}",
            payment_phone = "3000000000",
            delivery_address = "Calle 1"
        })
    };

    private class UnitOfWorkProxy : DispatchProxy
    {
        private IPaymentTransactionRepository _payments = null!;

        public static IUnitOfWork Create(IPaymentTransactionRepository payments)
        {
            var proxy = Create<IUnitOfWork, UnitOfWorkProxy>();
            ((UnitOfWorkProxy)(object)proxy)._payments = payments;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_PaymentTransactions"
                ? _payments
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private class PaymentRepositoryProxy : DispatchProxy
    {
        private IReadOnlyList<PaymentTransaction> _payments = [];

        public static IPaymentTransactionRepository Create(IReadOnlyList<PaymentTransaction> payments)
        {
            var proxy = Create<IPaymentTransactionRepository, PaymentRepositoryProxy>();
            ((PaymentRepositoryProxy)(object)proxy)._payments = payments;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IPaymentTransactionRepository.GetByIdAsync))
            {
                var id = (Guid)args![0]!;
                return Task.FromResult(_payments.SingleOrDefault(payment => payment.PaymentTransactionId == id));
            }

            if (targetMethod?.Name == nameof(IPaymentTransactionRepository.GetPagedByBusinessIdAsync))
            {
                var businessId = (Guid)args![0]!;
                var status = (PaymentTransactionStatus?)args[4];
                var matches = _payments
                    .Where(payment => payment.BusinessId == businessId && (!status.HasValue || payment.Status == status))
                    .ToList();
                return Task.FromResult(((IReadOnlyList<PaymentTransaction>)matches, matches.Count));
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private class PaymentConfirmationProxy : DispatchProxy
    {
        private IReadOnlyList<PaymentTransaction> _payments = [];

        public static IPaymentConfirmationHandler Create(IReadOnlyList<PaymentTransaction> payments)
        {
            var proxy = Create<IPaymentConfirmationHandler, PaymentConfirmationProxy>();
            ((PaymentConfirmationProxy)(object)proxy)._payments = payments;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IPaymentConfirmationHandler.HandleAsync))
                throw new NotSupportedException(targetMethod?.Name);

            var reference = (string)args![0]!;
            var payment = _payments.Single(value => value.PaymentReferenceId == reference);
            payment.Status = PaymentTransactionStatus.Confirmed;
            payment.Source = PaymentTransactionSource.Manual;
            payment.ConfirmedAt = DateTime.UtcNow;
            return Task.FromResult(new PaymentConfirmationResult(true, null));
        }
    }
}
