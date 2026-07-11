using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed record ReservationIntentContext(
    Guid BusinessId,
    AgentConfig Config,
    IReadOnlyDictionary<string, string> Facts,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? ChannelPhone = null);

public interface IReservationIntentBuilder
{
    Task<ReservationIntentSnapshot?> BuildAsync(
        ReservationIntentContext context,
        CancellationToken cancellationToken = default);

    string BuildCustomAttributesJson(IReadOnlyDictionary<string, string> facts);
}

public sealed class ReservationIntentBuilder : IReservationIntentBuilder
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAddOnCatalogService _addOnCatalog;

    public ReservationIntentBuilder(IUnitOfWork unitOfWork, IAddOnCatalogService addOnCatalog)
    {
        _unitOfWork = unitOfWork;
        _addOnCatalog = addOnCatalog;
    }

    public async Task<ReservationIntentSnapshot?> BuildAsync(
        ReservationIntentContext context,
        CancellationToken cancellationToken = default)
    {
        var roles = new FactRoleIndex(context.Config.FactSchema);
        var serviceName = GetFact(roles, context.Facts, "booking.service", ConversationFactKeys.Service);
        var dateStr = GetFact(roles, context.Facts, "booking.date", ConversationFactKeys.DesiredDate);
        var timeStr = GetFact(roles, context.Facts, "booking.time", ConversationFactKeys.DesiredTime);

        if (string.IsNullOrWhiteSpace(serviceName)
            || !AgentDateRules.TryParseDate(dateStr, out var date)
            || !TimeOnly.TryParse(timeStr, out var time))
        {
            return null;
        }

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(context.BusinessId, serviceName);
        if (service is null)
            return null;

        var addOnsCsv = GetFact(roles, context.Facts, "booking.addons", ConversationFactKeys.AddOns);
        var addOnIds = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(addOnsCsv))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                context.BusinessId,
                serviceName,
                addOnsCsv,
                cancellationToken);
            if (!validation.IsValid)
                return null;

            addOnIds.AddRange(await ResolveAddOnIdsAsync(
                context.BusinessId,
                validation.NormalizedCsv,
                cancellationToken));
        }

        var customerName = GetFact(roles, context.Facts, "customer.name", ConversationFactKeys.CustomerName)
            ?? context.CustomerName;
        var customerPhone = ResolveCustomerPhone(context, roles);
        var customerEmail = GetFact(roles, context.Facts, "customer.email", ConversationFactKeys.CustomerEmail)
            ?? context.CustomerEmail;

        return new ReservationIntentSnapshot(
            service.ServiceId,
            service.ServiceName,
            date.ToDateTime(time),
            service.DurationMinutes > 0 ? service.DurationMinutes : 60,
            PreferredEmployeeId: null,
            customerName,
            customerEmail,
            customerPhone,
            addOnIds,
            ReservationCustomAttributes.BuildJson(context.Facts, context.Config.FactSchema));
    }

    public string BuildCustomAttributesJson(IReadOnlyDictionary<string, string> facts) =>
        ReservationCustomAttributes.BuildJson(facts, null);

    private static string? ResolveCustomerPhone(ReservationIntentContext context, FactRoleIndex roles)
    {
        var factPhone = GetFact(roles, context.Facts, "customer.phone", ConversationFactKeys.CustomerPhone);
        if (!string.IsNullOrWhiteSpace(factPhone))
            return factPhone.Trim();
        return string.IsNullOrWhiteSpace(context.ChannelPhone) ? null : context.ChannelPhone.Trim();
    }

    private static string? GetFact(
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string role,
        string fallbackKey) =>
        roles.GetByRole(facts, role) ?? ConversationFactKeys.Get(facts, fallbackKey);

    private async Task<IReadOnlyList<Guid>> ResolveAddOnIdsAsync(
        Guid businessId,
        string? addOnsCsv,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(addOnsCsv))
            return [];

        var ids = new List<Guid>();
        foreach (var name in addOnsCsv.Split(
                     [',', ';'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(name, "ninguno", StringComparison.OrdinalIgnoreCase))
                continue;
            var addOn = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, name);
            if (addOn is not null)
                ids.Add(addOn.ServiceId);
        }
        return ids;
    }
}
