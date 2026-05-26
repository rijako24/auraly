using System.Text.Json;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public interface IReservationIntentBuilder
{
    Task<ReservationIntentSnapshot?> BuildFromContextAsync(
        AgentToolContext ctx,
        CancellationToken cancellationToken = default);

    string BuildCustomAttributesJson(IReadOnlyDictionary<string, string> facts, AgentToolContext ctx);
}

public sealed class ReservationIntentBuilder : IReservationIntentBuilder
{
    private static readonly string[] UniversalRoles =
    [
        FactRoles.CustomerName,
        FactRoles.CustomerPhone,
        FactRoles.CustomerEmail,
        FactRoles.BookingService,
        FactRoles.BookingDate,
        FactRoles.BookingTime,
        FactRoles.BookingAddOns
    ];

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly IFactAccessor _facts;

    public ReservationIntentBuilder(
        IUnitOfWork unitOfWork,
        IAddOnCatalogService addOnCatalog,
        IFactAccessor facts)
    {
        _unitOfWork = unitOfWork;
        _addOnCatalog = addOnCatalog;
        _facts = facts;
    }

    public async Task<ReservationIntentSnapshot?> BuildFromContextAsync(
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var serviceName = _facts.GetByRole(ctx, FactRoles.BookingService);
        var dateStr = _facts.GetByRole(ctx, FactRoles.BookingDate);
        var timeStr = _facts.GetByRole(ctx, FactRoles.BookingTime);

        if (string.IsNullOrWhiteSpace(serviceName)
            || string.IsNullOrWhiteSpace(dateStr)
            || string.IsNullOrWhiteSpace(timeStr))
            return null;

        if (!AgentDateRules.TryParseDate(dateStr, out var date))
            return null;
        if (!TimeOnly.TryParse(timeStr, out var time))
            return null;

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(ctx.BusinessId, serviceName);
        if (service is null)
            return null;

        var addOnsCsv = _facts.GetByRole(ctx, FactRoles.BookingAddOns);
        var addOnIds = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(addOnsCsv))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, serviceName, addOnsCsv, cancellationToken);
            if (!validation.IsValid)
                return null;

            addOnIds.AddRange(await ResolveAddOnIdsAsync(ctx.BusinessId, validation.NormalizedCsv, cancellationToken));
        }

        var customerName = _facts.GetByRole(ctx, FactRoles.CustomerName) ?? ctx.Conversation.CustomerName;
        var customerPhone = ConversationContactPhone.Resolve(ctx);
        var customerEmail = _facts.GetByRole(ctx, FactRoles.CustomerEmail) ?? ctx.Conversation.CustomerEmail;

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
            BuildCustomAttributesJson(ctx.Facts, ctx));
    }

    public string BuildCustomAttributesJson(IReadOnlyDictionary<string, string> facts, AgentToolContext ctx)
    {
        var universalKeys = BuildUniversalFactKeys(ctx);
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in facts)
        {
            if (universalKeys.Contains(key) || string.IsNullOrWhiteSpace(value))
                continue;
            custom[key] = value.Trim();
        }

        return custom.Count == 0 ? "{}" : JsonSerializer.Serialize(custom);
    }

    private HashSet<string> BuildUniversalFactKeys(AgentToolContext ctx)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in UniversalRoles)
        {
            var key = _facts.GetKeyByRole(ctx, role);
            if (!string.IsNullOrWhiteSpace(key))
                keys.Add(key);
        }

        return keys;
    }

    private async Task<IReadOnlyList<Guid>> ResolveAddOnIdsAsync(
        Guid businessId,
        string? addOnsCsv,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(addOnsCsv))
            return [];

        var ids = new List<Guid>();
        var names = addOnsCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in names)
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
