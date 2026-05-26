using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Identity;

public sealed class IdentityAttributeService : IIdentityAttributeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILeadService _leadService;

    public IdentityAttributeService(IUnitOfWork unitOfWork, ILeadService leadService)
    {
        _unitOfWork = unitOfWork;
        _leadService = leadService;
    }

    public string? GetByRole(AgentToolContext ctx, string role)
    {
        var attrs = Deserialize(ctx.Conversation.IdentityAttributesJson);
        if (attrs.TryGetValue(role, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        return role switch
        {
            FactRoles.CustomerName => ctx.Conversation.CustomerName,
            FactRoles.CustomerEmail => ctx.Conversation.CustomerEmail,
            _ => null
        };
    }

    public async Task SyncFromFactAsync(
        AgentToolContext ctx,
        FactSchemaEntry schemaEntry,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (!schemaEntry.PersistsAcrossConversations
            || string.IsNullOrWhiteSpace(schemaEntry.Role))
        {
            return;
        }

        var attrs = Deserialize(ctx.Conversation.IdentityAttributesJson);
        attrs[schemaEntry.Role] = value.Trim();
        ctx.Conversation.IdentityAttributesJson = Serialize(attrs);

        if (string.Equals(schemaEntry.Role, FactRoles.CustomerName, StringComparison.OrdinalIgnoreCase))
            ctx.Conversation.CustomerName = value;
        if (string.Equals(schemaEntry.Role, FactRoles.CustomerEmail, StringComparison.OrdinalIgnoreCase))
            ctx.Conversation.CustomerEmail = value;

        await _unitOfWork.Conversations.UpdateAsync(ctx.Conversation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (string.Equals(schemaEntry.Role, FactRoles.CustomerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(schemaEntry.Role, FactRoles.CustomerEmail, StringComparison.OrdinalIgnoreCase))
        {
            await _leadService.SyncCustomerIdentityAsync(
                ctx.BusinessId,
                ctx.Conversation.UserNumber,
                string.Equals(schemaEntry.Role, FactRoles.CustomerName, StringComparison.OrdinalIgnoreCase) ? value : null,
                string.Equals(schemaEntry.Role, FactRoles.CustomerEmail, StringComparison.OrdinalIgnoreCase) ? value : null,
                cancellationToken);
        }
    }

    private static Dictionary<string, string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Serialize(Dictionary<string, string> attrs) =>
        JsonSerializer.Serialize(attrs, JsonOptions);
}
