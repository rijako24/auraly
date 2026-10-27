using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Services;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class TenantSubscriptionLifecycleProcess(
    ITenantSubscriptionLifecycleStore store,
    ITenantCommercialQuoteService quotes,
    TimeProvider time,
    ILogger<TenantSubscriptionLifecycleProcess> logger) : ITimedProcess
{
    public const string ProcessName = "tenant_subscription_lifecycle";
    public string Name => ProcessName;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        await store.ReconcileSchedulesAsync(now, ct);
        var candidates = await store.GetDueAsync(now, ct);
        if (candidates.Count == 0) return;
        var catalog = await quotes.GetCatalogAsync(ct);
        foreach (var candidate in candidates)
        {
            try
            {
                var request = BuildQuoteRequest(candidate);
                var quote = TenantCommercialQuoteService.Quote(request, catalog);
                var decision = Evaluate(candidate, now);
                await store.ApplyAsync(candidate, quote, decision, now, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Tenant subscription lifecycle failed for SubscriptionId={SubscriptionId} TenantId={TenantId}",
                    candidate.SubscriptionId, candidate.TenantId);
            }
        }
    }

    internal static TenantQuoteRequest BuildQuoteRequest(TenantSubscriptionLifecycleCandidate value)
    {
        static int Difference(int limit, int included, string name)
        {
            var result = limit - included;
            return result >= 0 ? result : throw new InvalidOperationException(
                $"La capacidad de {name} es menor que la incluida por el plan.");
        }

        static int Packs(int limit, int included, int size, string name)
        {
            if (size <= 0) throw new InvalidOperationException($"El paquete de {name} no tiene un tamaño válido.");
            var difference = Difference(limit, included, name);
            if (difference % size != 0) throw new InvalidOperationException(
                $"La capacidad de {name} no corresponde a paquetes completos de {size}.");
            return difference / size;
        }

        return new TenantQuoteRequest(
            value.PlanCode,
            value.BillingPeriod,
            Difference(value.FullUserLimit, value.IncludedFullUsers, "usuarios completos"),
            Difference(value.SellerUserLimit, value.IncludedSellerUsers, "usuarios vendedores"),
            Difference(value.PosDeviceLimit, value.IncludedPosDevices, "cajas"),
            Packs(value.DianDocumentMonthlyLimit, value.IncludedDianDocuments,
                value.DianDocumentPackSize, "documentos DIAN"),
            Packs(value.PayrollEmployeeLimit, value.IncludedPayrollEmployees,
                value.PayrollEmployeePackSize, "empleados de nómina"));
    }

    internal static TenantSubscriptionLifecycleDecision Evaluate(
        TenantSubscriptionLifecycleCandidate value, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(value.BillingTimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone).Date;
        var dueAt = value.RenewalPeriodStart ?? value.CurrentPeriodEnd;
        var localDue = TimeZoneInfo.ConvertTime(dueAt, zone).Date;
        var daysOverdue = (localNow - localDue).Days;
        DateTimeOffset Next(DateTimeOffset reminder) => value.NextRenewalEvaluationAt is { } next && next < reminder
            ? next : reminder;
        if (daysOverdue > 0)
        {
            var reminderDay = daysOverdue / value.OverdueReminderIntervalDays
                * value.OverdueReminderIntervalDays;
            return reminderDay > 0
                ? new("PastDue", $"Overdue:{reminderDay}", "Pago de suscripción vencido",
                    $"Tu renovación lleva {daysOverdue} días vencida. Puedes seguir operando. Los nuevos cupos DIAN se habilitan al confirmar el pago.",
                    value.EmailRemindersEnabled,
                    Next(dueAt.AddDays(reminderDay + value.OverdueReminderIntervalDays)))
                : new("PastDue", null, null, null, false,
                    Next(dueAt.AddDays(value.OverdueReminderIntervalDays)));
        }
        if (daysOverdue == 0) return new("PastDue", null, null, null, false,
            Next(dueAt.AddDays(value.OverdueReminderIntervalDays)));
        if (daysOverdue >= -value.PreDueReminderDays)
            return new(null, $"PreDue:{value.PreDueReminderDays}", "Próxima renovación de Auraly",
                $"Tu suscripción vence en {Math.Abs(daysOverdue)} días. Revisa el detalle y paga la renovación.",
                value.EmailRemindersEnabled, Next(dueAt));
        return new(null, null, null, null, false,
            Next(dueAt.AddDays(-value.PreDueReminderDays)));
    }
}
