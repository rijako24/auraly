using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Persiste un hecho clave-valor en ConversationContexts (Facts).
/// </summary>
public sealed class SetFactTool : IAgentTool
{
    private readonly IConversationFactsService _factsService;
    private readonly IAddOnCatalogService _addOnCatalog;

    public SetFactTool(IConversationFactsService factsService, IAddOnCatalogService addOnCatalog)
    {
        _factsService = factsService;
        _addOnCatalog = addOnCatalog;
    }

    public string Name => "set_fact";

    public string Description =>
        "Persists a key-value fact into conversation state for later turns and tools. " +
        "Use for customer data (customer_name, customer_phone, customer_email), " +
        "booking fields (service, desired_date, desired_time, add_ons), " +
        "or tenant-specific facts (baby_age_months, party_size, etc.). " +
        "When key=service, the result includes compatible_add_ons from the catalog. " +
        "Call as soon as the customer provides structured data — do not wait until checkout.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "key": {
              "type": "string",
              "description": "Short snake_case identifier (e.g. customer_name, baby_age_months, service)"
            },
            "value": {
              "type": "string",
              "description": "Structured value (number, name, date YYYY-MM-DD, time HH:mm — not a full sentence)"
            }
          },
          "required": ["key", "value"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "key", out var rawKey)
            || !ToolResultHelper.TryGetString(arguments, "value", out var rawValue))
        {
            return ToolResultHelper.MissingPrerequisites(["key", "value"]);
        }

        if (!FactKeyNormalizer.TryNormalizeKey(rawKey, out var key))
        {
            return ToolResultHelper.Error(
                "invalid_key",
                "Fact key must be a short snake_case identifier.",
                "Use keys like customer_name, service, or baby_age_months.");
        }

        if (!FactKeyNormalizer.TryNormalizeValue(rawValue, out var value))
        {
            return ToolResultHelper.Error(
                "invalid_value",
                "Fact value cannot be empty.",
                "Provide a structured value, not a full sentence.");
        }

        if (key.Equals(ConversationFactKeys.AddOns, StringComparison.OrdinalIgnoreCase))
        {
            var service = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service)
                ?? ctx.ActiveReservation?.Service?.ServiceName;

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

        if (key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase))
            ctx.Conversation.CustomerName = value;
        if (key.Equals(ConversationFactKeys.CustomerEmail, StringComparison.OrdinalIgnoreCase))
            ctx.Conversation.CustomerEmail = value;

        if (key.Equals(ConversationFactKeys.Service, StringComparison.OrdinalIgnoreCase))
        {
            var compatibleAddOns = await _addOnCatalog.GetCompatibleAsync(
                ctx.BusinessId, value, cancellationToken);

            return ToolResultHelper.Ok(new
            {
                key,
                value,
                storage = "fact",
                compatible_add_ons = MapCompatibleAddOns(compatibleAddOns)
            });
        }

        return ToolResultHelper.Ok(new { key, value, storage = "fact" });
    }

    private static IReadOnlyList<object> MapCompatibleAddOns(IReadOnlyList<AddOnRuleInfo> addOns) =>
        addOns.Select(a => (object)new
        {
            name = a.AddOnName,
            price = a.AddOnPrice,
            price_formatted = a.AddOnPrice.ToString("N0", CultureInfo.InvariantCulture),
            description = string.IsNullOrWhiteSpace(a.AddOnDescription) ? null : a.AddOnDescription
        }).ToList();
}
