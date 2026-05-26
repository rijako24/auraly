using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Identity;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Única vía de persistencia de facts de usuario: clave canónica del factSchema del tenant.
/// </summary>
public sealed class SetFactTool : IAgentTool
{
    private readonly IConversationFactsService _factsService;
    private readonly IFactAccessor _facts;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly IConversationVerificationService _verifications;
    private readonly IIdentityAttributeService _identityAttributes;
    private readonly IUnitOfWork _unitOfWork;

    public SetFactTool(
        IConversationFactsService factsService,
        IFactAccessor facts,
        IAddOnCatalogService addOnCatalog,
        IConversationVerificationService verifications,
        IIdentityAttributeService identityAttributes,
        IUnitOfWork unitOfWork)
    {
        _factsService = factsService;
        _facts = facts;
        _addOnCatalog = addOnCatalog;
        _verifications = verifications;
        _identityAttributes = identityAttributes;
        _unitOfWork = unitOfWork;
    }

    public string Name => "set_fact";

    public string Description =>
        "Persists one user fact (canonical key + structured value) into conversation state. " +
        "Call once per fact. Use only keys from the current stage fact list in the system prompt.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "key": {
              "type": "string",
              "description": "Canonical fact key from the tenant schema (snake_case)"
            },
            "value": {
              "type": "string",
              "description": "Structured value only (name, number, YYYY-MM-DD, HH:mm — not a full sentence)"
            }
          },
          "required": ["key", "value"]
        }
        """;

    public async Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(invocation.Arguments, "key", out var rawKey)
            || !ToolResultHelper.TryGetString(invocation.Arguments, "value", out var rawValue))
        {
            return ToolResultHelper.MissingPrerequisites(["key", "value"]);
        }

        var ctx = invocation.Context;
        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var canonicalKey = roleIndex.NormalizeKey(rawKey.Trim());

        if (!FactKeyNormalizer.TryNormalizeKey(canonicalKey, out var key))
        {
            return ToolResultHelper.Error(
                "invalid_key",
                "Fact key must be a short snake_case identifier.",
                ValidKeysRemediation(ctx));
        }

        if (!FactKeyNormalizer.TryNormalizeValue(rawValue, out var value))
        {
            return ToolResultHelper.Error(
                "invalid_value",
                "Fact value cannot be empty.",
                "Provide a structured value, not a full sentence.");
        }

        var schemaEntry = roleIndex.EntryFor(key);

        if (schemaEntry is null && ctx.Config?.FactSchema.Count > 0)
        {
            return ToolResultHelper.Error(
                "unknown_key",
                $"'{key}' is not in this agent's fact schema.",
                ValidKeysRemediation(ctx));
        }

        if (schemaEntry is not null
            && !schemaEntry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Error(
                "non_user_source",
                $"Fact '{key}' cannot be set by the assistant.",
                "Use only user-sourced facts from the schema.");
        }

        if (schemaEntry is not null)
        {
            var shape = FactShapeValidator.Validate(schemaEntry, value);
            if (!shape.Ok)
            {
                return ToolResultHelper.Error(
                    "invalid_value_shape",
                    shape.ErrorCode ?? "invalid_shape",
                    shape.Remediation ?? "Provide a value matching the fact type.");
            }
        }

        var role = _facts.GetRoleForKey(ctx, key);

        if (string.Equals(role, FactRoles.BookingService, StringComparison.OrdinalIgnoreCase))
        {
            var services = await _unitOfWork.Services.GetActiveByBusinessIdAsync(ctx.BusinessId);
            var canonical = ActiveServiceCatalogMatcher.MatchExact(services, value);
            if (canonical is null)
            {
                return ToolResultHelper.Error(
                    "unknown_service",
                    $"Service '{value}' is not in the active catalog.",
                    "Call get_service_catalog and use an exact service name from the list.");
            }

            value = canonical;
        }

        if (string.Equals(role, FactRoles.BookingAddOns, StringComparison.OrdinalIgnoreCase))
        {
            var service = _facts.GetByRole(ctx, FactRoles.BookingService)
                ?? ctx.GetPackContext<IBookingPackContext>()?.ActiveReservation?.Service?.ServiceName;

            if (string.IsNullOrWhiteSpace(service))
            {
                return ToolResultHelper.MissingPrerequisites(["service"]);
            }

            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, service, value, cancellationToken);

            if (!validation.IsValid)
            {
                return ToolResultHelper.Error(
                    "invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    validation.Hint);
            }

            if (!string.IsNullOrWhiteSpace(validation.NormalizedCsv))
                value = validation.NormalizedCsv;
        }

        await _factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, key, value, cancellationToken);
        ctx.Facts[key] = value;

        if (schemaEntry is not null)
            await _identityAttributes.SyncFromFactAsync(ctx, schemaEntry, value, cancellationToken);

        await TryRecordCustomerIdentifiedAsync(ctx);

        return ToolResultHelper.Ok(new { key, value, storage = "fact" });
    }

    private static string ValidKeysRemediation(AgentToolContext ctx)
    {
        var validKeys = ctx.Config?.FactSchema
            .Where(e => e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        return validKeys.Count > 0
            ? $"Use one of: {string.Join(", ", validKeys)}"
            : "Configure factSchema keys for this agent.";
    }

    private Task TryRecordCustomerIdentifiedAsync(AgentToolContext ctx)
    {
        var name = _facts.GetByRole(ctx, FactRoles.CustomerName) ?? ctx.Conversation.CustomerName;
        var phone = ConversationContactPhone.Resolve(ctx);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone))
            return Task.CompletedTask;

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            SlotVerificationScope.UniversalScope,
            ttl: null);

        return Task.CompletedTask;
    }
}
