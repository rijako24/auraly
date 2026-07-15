using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Internal;

public sealed class SearchManualPaymentsOperation : IAgentOperation
{
    private readonly IUnitOfWork _unitOfWork;

    public SearchManualPaymentsOperation(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public OperationDescriptor Descriptor { get; } = new(
        "internal.search_manual_payments",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": { "type": ["string", "null"] }
          }
        }
        """,
        ["payment.single_pending", "payment.multiple_pending", "payment.none_pending"],
        [],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        OperationJsonHelper.TryGetString(input, "query", out var query);
        var candidates = await ManualPaymentCandidateResolver.FindPendingAsync(
            _unitOfWork,
            context.BusinessId,
            query,
            cancellationToken);

        var payload = new
        {
            count = candidates.Count,
            selected_payment_transaction_id = candidates.Count == 1
                ? candidates[0].PaymentTransactionId.ToString()
                : null,
            payments = candidates.Select(ManualPaymentCandidateResolver.ToPayload).ToList()
        };

        var code = candidates.Count switch
        {
            0 => "payment.none_pending",
            1 => "payment.single_pending",
            _ => "payment.multiple_pending"
        };
        return OperationOutcome.Ok(code, payload);
    }
}

/// <summary>
/// Confirms exactly the manual payment identified by a button or an unambiguous spoken query.
/// Authorization is provided by inbound-contact routing; business ownership is rechecked here.
/// </summary>
public sealed class ConfirmManualPaymentOperation : IAgentOperation
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public ConfirmManualPaymentOperation(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    public OperationDescriptor Descriptor { get; } = new(
        "internal.confirm_manual_payment",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "payment_transaction_id": { "type": ["string", "null"] },
            "query": { "type": ["string", "null"] }
          }
        }
        """,
        [
            "payment.confirmed",
            "payment.already_confirmed",
            "payment.not_found",
            "payment.ambiguous",
            "payment.not_manual",
            "payment.not_pending",
            "payment.expired",
            "payment.confirmation_failed",
            "input.invalid"
        ],
        ["payment.confirm_manual"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolvePaymentAsync(input, context.BusinessId, cancellationToken);
        if (resolution.Failure is not null)
            return resolution.Failure;

        var payment = resolution.Payment!;
        if (!ManualPaymentCandidateResolver.IsManual(payment))
            return OperationOutcome.Fail("payment.not_manual", "Este pago no requiere confirmacion manual.");

        if (payment.Status == PaymentTransactionStatus.Confirmed)
            return OperationOutcome.Ok("payment.already_confirmed", BuildResult(payment, alreadyConfirmed: true));

        if (payment.Status != PaymentTransactionStatus.Created)
        {
            return OperationOutcome.Fail(
                "payment.not_pending",
                $"El pago ya no esta pendiente; su estado actual es {payment.Status}.");
        }

        if (payment.ExpiresAt.HasValue && payment.ExpiresAt.Value <= DateTime.UtcNow)
            return OperationOutcome.Fail("payment.expired", "El pago ya vencio y no puede confirmarse.");

        var actorPhone = context.Session?.ChannelPhone ?? string.Empty;
        var payload = JsonSerializer.Serialize(new
        {
            source = "whatsapp_manual_approval",
            approving_agent_id = context.AgentId,
            approving_phone = actorPhone,
            payment_transaction_id = payment.PaymentTransactionId,
            confirmed_at = DateTime.UtcNow
        });

        // Resuelto al ejecutar para evitar el ciclo de DI con el registro de operaciones.
        var paymentConfirmation = _serviceProvider.GetRequiredService<IPaymentConfirmationHandler>();
        var confirmation = await paymentConfirmation.HandleAsync(
            payment.PaymentReferenceId,
            $"manual-whatsapp:{payment.PaymentTransactionId:N}",
            payment.AmountInCents,
            payload,
            cancellationToken,
            PaymentTransactionSource.Manual);

        if (!confirmation.Success)
        {
            return OperationOutcome.Fail(
                "payment.confirmation_failed",
                confirmation.ErrorMessage ?? "No se pudo confirmar el pago.",
                recoverable: true);
        }

        var updated = await _unitOfWork.PaymentTransactions.GetByIdAsync(payment.PaymentTransactionId, cancellationToken)
            ?? payment;
        return OperationOutcome.Ok("payment.confirmed", BuildResult(updated, alreadyConfirmed: false));
    }

    private async Task<PaymentResolution> ResolvePaymentAsync(
        JsonElement input,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        if (OperationJsonHelper.TryGetString(input, "payment_transaction_id", out var paymentIdText))
        {
            if (!Guid.TryParse(paymentIdText, out var paymentTransactionId))
                return PaymentResolution.Fail("input.invalid", "El identificador del pago no es valido.");

            var exact = await _unitOfWork.PaymentTransactions.GetByIdAsync(paymentTransactionId, cancellationToken);
            return exact is null || exact.BusinessId != businessId
                ? PaymentResolution.Fail("payment.not_found", "El pago indicado no pertenece a este negocio.")
                : PaymentResolution.Ok(exact);
        }

        if (!OperationJsonHelper.TryGetString(input, "query", out var query))
            return PaymentResolution.Fail("input.invalid", "Indica cual pedido o pago deseas confirmar.");

        var matches = await ManualPaymentCandidateResolver.FindPendingAsync(
            _unitOfWork,
            businessId,
            query,
            cancellationToken);
        return matches.Count switch
        {
            0 => PaymentResolution.Fail("payment.not_found", "No encontre un pago manual pendiente con esos datos."),
            1 => PaymentResolution.Ok(matches[0]),
            _ => PaymentResolution.Fail("payment.ambiguous", "Hay varios pagos pendientes que coinciden; indica el pedido exacto.")
        };
    }

    private static object BuildResult(PaymentTransaction payment, bool alreadyConfirmed) => new
    {
        payment_transaction_id = payment.PaymentTransactionId,
        payment_reference = payment.PaymentReferenceId,
        amount_in_cents = payment.AmountInCents,
        amount = (payment.AmountInCents / 100m).ToString("N0", CultureInfo.InvariantCulture),
        currency = payment.Currency,
        status = payment.Status.ToString(),
        already_confirmed = alreadyConfirmed,
        confirmed_at = payment.ConfirmedAt
    };

    private sealed record PaymentResolution(PaymentTransaction? Payment, OperationOutcome? Failure)
    {
        public static PaymentResolution Ok(PaymentTransaction payment) => new(payment, null);

        public static PaymentResolution Fail(string code, string message) =>
            new(null, OperationOutcome.Fail(code, message));
    }
}

internal static class ManualPaymentCandidateResolver
{
    public static async Task<IReadOnlyList<PaymentTransaction>> FindPendingAsync(
        IUnitOfWork unitOfWork,
        Guid businessId,
        string? query,
        CancellationToken cancellationToken)
    {
        var (items, _) = await unitOfWork.PaymentTransactions.GetPagedByBusinessIdAsync(
            businessId,
            page: 1,
            pageSize: 100,
            search: null,
            status: PaymentTransactionStatus.Created,
            cancellationToken);

        var pending = items
            .Where(IsManual)
            .Where(payment => !payment.ExpiresAt.HasValue || payment.ExpiresAt.Value > DateTime.UtcNow)
            .OrderByDescending(payment => payment.CreatedAt);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim();
            pending = pending.Where(payment => Matches(payment, normalized))
                .OrderByDescending(payment => payment.CreatedAt);
        }

        return pending.ToList();
    }

    public static bool IsManual(PaymentTransaction payment) =>
        string.IsNullOrWhiteSpace(payment.LinkUrl)
        && payment.PaymentReferenceId.StartsWith("manual-", StringComparison.OrdinalIgnoreCase);

    public static object ToPayload(PaymentTransaction payment)
    {
        var snapshot = ReadSnapshot(payment.CheckoutSnapshotJson);
        return new
        {
            payment_transaction_id = payment.PaymentTransactionId,
            payment_code = payment.PaymentTransactionId.ToString("N")[..8].ToUpperInvariant(),
            payment_reference = payment.PaymentReferenceId,
            order_number = Read(snapshot, "order_number"),
            customer_name = Read(snapshot, "payer_name"),
            customer_phone = Read(snapshot, "payment_phone"),
            delivery_address = Read(snapshot, "delivery_address"),
            amount = (payment.AmountInCents / 100m).ToString("N0", CultureInfo.InvariantCulture),
            amount_in_cents = payment.AmountInCents,
            currency = payment.Currency,
            created_at = payment.CreatedAt,
            expires_at = payment.ExpiresAt
        };
    }

    private static bool Matches(PaymentTransaction payment, string query)
    {
        var values = new[]
        {
            payment.PaymentTransactionId.ToString(),
            payment.PaymentTransactionId.ToString("N")[..8],
            payment.PaymentReferenceId,
            payment.CheckoutSnapshotJson
        };
        return values.Any(value => !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Read(JsonElement? snapshot, string property) =>
        snapshot is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out var result)
            ? result.ValueKind == JsonValueKind.String ? result.GetString() : result.ToString()
            : null;
}
