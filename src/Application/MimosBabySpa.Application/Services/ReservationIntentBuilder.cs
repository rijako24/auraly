using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public interface IReservationIntentBuilder
{
    Task<ReservationIntentSnapshot?> BuildFromContextAsync(
        AgentToolContext ctx,
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

    public async Task<ReservationIntentSnapshot?> BuildFromContextAsync(
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var serviceName = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service);
        var dateStr = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate);
        var timeStr = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime);

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

        var addOnsCsv = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns);
        var addOnIds = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(addOnsCsv))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, serviceName, addOnsCsv, cancellationToken);
            if (!validation.IsValid)
                return null;

            addOnIds.AddRange(await ResolveAddOnIdsAsync(ctx.BusinessId, validation.NormalizedCsv, cancellationToken));
        }

        var customerName = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerName)
            ?? ctx.Conversation.CustomerName;
        var customerPhone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone);
        var customerEmail = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerEmail)
            ?? ctx.Conversation.CustomerEmail;

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
            ReservationCustomAttributes.BuildJson(ctx.Facts, ctx.Config?.FactSchema ?? []));
    }

    public string BuildCustomAttributesJson(IReadOnlyDictionary<string, string> facts) =>
        ReservationCustomAttributes.BuildJson(facts, null);

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
