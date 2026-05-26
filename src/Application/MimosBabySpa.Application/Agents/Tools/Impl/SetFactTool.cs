using System.Text.Json;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
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
    private readonly IConversationVerificationService _verifications;
    private readonly ILeadService _leadService;

    public SetFactTool(
        IConversationFactsService factsService,
        IAddOnCatalogService addOnCatalog,
        IConversationVerificationService verifications,
        ILeadService leadService)
    {
        _factsService = factsService;
        _addOnCatalog = addOnCatalog;
        _verifications = verifications;
        _leadService = leadService;
    }

    public string Name => "set_fact";

    public string Description =>
        "Persists a key-value pair into conversation state. " +
        "Input: fact key and value. Output: stored key and normalized value.";

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

        // Normalizar alias → key canónico usando el schema del tenant
        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var canonicalKey = roleIndex.NormalizeKey(rawKey.Trim());

        if (!FactKeyNormalizer.TryNormalizeKey(canonicalKey, out var key))
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

        // Validación de tipo basada en schema del tenant
        var schemaEntry = roleIndex.EntryFor(key);
        if (schemaEntry is null && ctx.Config?.FactSchema.Count > 0)
        {
            var validKeys = string.Join(", ", ctx.Config.FactSchema
                .Where(e => e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Key));

            return ToolResultHelper.Error(
                "unknown_fact_key",
                $"'{key}' no es una clave reconocida del esquema de este agente.",
                $"Usa exactamente una de estas claves: {validKeys}.");
        }

        if (schemaEntry is not null)
        {
            var typeError = ValidateType(key, value, schemaEntry.Type);
            if (typeError is not null)
                return typeError;
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

        if (key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase)
            || key.Equals(ConversationFactKeys.CustomerEmail, StringComparison.OrdinalIgnoreCase))
        {
            await _leadService.SyncCustomerIdentityAsync(
                ctx.BusinessId,
                ctx.Conversation.UserNumber,
                key.Equals(ConversationFactKeys.CustomerName, StringComparison.OrdinalIgnoreCase) ? value : null,
                key.Equals(ConversationFactKeys.CustomerEmail, StringComparison.OrdinalIgnoreCase) ? value : null,
                cancellationToken);
        }

        await TryRecordCustomerIdentifiedAsync(ctx);

        return ToolResultHelper.Ok(new { key, value, storage = "fact" });
    }

    private Task TryRecordCustomerIdentifiedAsync(AgentToolContext ctx)
    {
        var name = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerName)
            ?? ctx.Conversation.CustomerName;
        var phone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone))
            return Task.CompletedTask;

        _verifications.Record(
            ctx,
            VerificationFactTypes.CustomerIdentified,
            SlotVerificationScope.UniversalScope,
            ttl: null);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Valida el valor contra el tipo declarado en factSchema.
    /// Devuelve null si pasa la validación o un JSON de error si no.
    /// </summary>
    private static string? ValidateType(string key, string value, string type)
    {
        return type.ToLowerInvariant() switch
        {
            "number" when !decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                              System.Globalization.CultureInfo.InvariantCulture, out _) =>
                ToolResultHelper.Error(
                    "invalid_type",
                    $"'{key}' must be a number, got '{value}'.",
                    "Provide a numeric value (e.g. 5, 12)."),

            "date" when !System.DateOnly.TryParseExact(value, "yyyy-MM-dd", null,
                             System.Globalization.DateTimeStyles.None, out _) =>
                ToolResultHelper.Error(
                    "invalid_type",
                    $"'{key}' must be a date in YYYY-MM-DD format, got '{value}'.",
                    "Use format YYYY-MM-DD (e.g. 2026-05-23)."),

            "time" when !System.TimeSpan.TryParse(value, out _) =>
                ToolResultHelper.Error(
                    "invalid_type",
                    $"'{key}' must be a time in HH:mm format, got '{value}'.",
                    "Use 24-hour format (e.g. 08:00)."),

            _ => null
        };
    }
}
