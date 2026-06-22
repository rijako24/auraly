using System.Text.Json;

using MimosBabySpa.Application.Agents.Configuration;

using MimosBabySpa.Application.Agents.Facts;

using MimosBabySpa.Application.Agents.Gating;

using MimosBabySpa.Application.Agents.Tools;

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

    private readonly ServiceNameResolver _serviceNameResolver;



    public SetFactTool(

        IConversationFactsService factsService,

        IAddOnCatalogService addOnCatalog,

        IConversationVerificationService verifications,

        ILeadService leadService,

        ServiceNameResolver serviceNameResolver)

    {

        _factsService = factsService;

        _addOnCatalog = addOnCatalog;

        _verifications = verifications;

        _leadService = leadService;

        _serviceNameResolver = serviceNameResolver;

    }



    public string Name => "set_fact";


    public IReadOnlyList<string> Capabilities => [ToolCapabilities.FactWrite];



    public string Description =>
        "Registra en el estado de la conversación un dato aportado por el cliente. " +
        "Úsala SOLO cuando el cliente entregue un dato nuevo o cambie uno existente. " +
        "NUNCA la uses para reconfirmar o repetir un dato que ya aparece en '## ESTADO ACTUAL' con el mismo valor. " +
        "Normaliza fechas a YYYY-MM-DD y horas a HH:mm antes de registrar. " +
        "Input: clave (key) y valor (value). Output: clave y valor normalizado almacenados.";



    public string ParametersSchema => SetFactParametersSchemaBuilder.FallbackSchema;



    public string BuildParametersSchema(AgentConfig config) =>

        SetFactParametersSchemaBuilder.Build(config);



    public async Task<string> ExecuteAsync(

        JsonElement arguments,

        AgentToolContext ctx,

        CancellationToken cancellationToken = default)

    {

        if (!ToolResultHelper.TryGetString(arguments, "key", out var rawKey))

            return ToolResultHelper.MissingPrerequisites(["key"]);



        if (!arguments.TryGetProperty("value", out var valueElement))

            return ToolResultHelper.MissingPrerequisites(["value"]);



        if (!TryReadScalarValue(valueElement, out var rawValue))
        {
            return ToolResultHelper.Error(

                ToolErrorCodes.InvalidValue,

                "Fact value must be a scalar value.",

                "Provide a structured value like a name, number, date, or time.",

                recoverable: true);
        }



        // Normalizar alias → key canónico usando el schema del tenant

        var roleIndex = new FactRoleIndex(ctx.Config?.FactSchema ?? []);

        var canonicalKey = roleIndex.NormalizeKey(rawKey.Trim());



        if (!FactKeyNormalizer.TryNormalizeKey(canonicalKey, out var key))

        {

            return ToolResultHelper.Error(

                ToolErrorCodes.InvalidKey,

                "Fact key must be a short snake_case identifier.",

                "Use keys like customer_name, service, or baby_age_months.",

                recoverable: true);

        }



        // Validación de tipo basada en schema del tenant (antes de normalizar valor)

        var schemaEntry = roleIndex.EntryFor(key);

        if (schemaEntry is null && ctx.Config?.FactSchema.Count > 0)

        {

            var validKeys = string.Join(", ", ctx.Config.FactSchema

                .Where(e => e.Source.Equals("user", StringComparison.OrdinalIgnoreCase))

                .Select(e => e.Key));



            return ToolResultHelper.Error(

                ToolErrorCodes.UnknownFactKey,

                $"'{key}' no es una clave reconocida del esquema de este agente.",

                $"Usa exactamente una de estas claves: {validKeys}.",

                recoverable: true);

        }



        if (!FactKeyNormalizer.TryNormalizeValue(rawValue, out var value))

        {

            return ToolResultHelper.Error(

                ToolErrorCodes.InvalidValue,

                "Fact value cannot be empty.",

                "Provide a structured value, not a full sentence.",

                recoverable: true);

        }



        if (schemaEntry is not null)

        {

            var typeError = ValidateType(key, value, schemaEntry.Type);

            if (typeError is not null)

                return typeError;

        }

        if (key.Equals(ConversationFactKeys.Service, StringComparison.OrdinalIgnoreCase))

        {

            var canonicalService = await _serviceNameResolver.ResolveAsync(

                ctx.BusinessId, value, cancellationToken);

            if (canonicalService is null)

            {

                var candidates = await _serviceNameResolver.GetCandidateNamesAsync(

                    ctx.BusinessId, value, ct: cancellationToken);

                var hint = candidates.Count > 0

                    ? $"No guardes un servicio inventado. Usa exactamente uno de estos nombres del catalogo: {string.Join(", ", candidates)}."

                    : "Llama get_service_catalog y usa exactamente un nombre de servicio del catalogo.";

                return ToolResultHelper.Error(

                    ToolErrorCodes.ServiceNotResolved,

                    $"No canonical service was found for '{value}'.",

                    hint,

                    recoverable: true);

            }

            if (canonicalService is not null)

                value = canonicalService;

        }



        ctx.Facts.TryGetValue(key, out var previousValue);

        if (!string.IsNullOrWhiteSpace(previousValue)

            && string.Equals(previousValue.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))

        {

            return ToolResultHelper.Ok(new { key, value, unchanged = true, storage = "fact_unchanged" });

        }



        if (key.Equals(ConversationFactKeys.AddOns, StringComparison.OrdinalIgnoreCase))

        {

            var service = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service)

                ?? ctx.SingleManageableReservation?.Service?.ServiceName;



            if (string.IsNullOrWhiteSpace(service))

            {

                return ToolResultHelper.MissingPrerequisites(["service"]);

            }



            var validation = await _addOnCatalog.ValidateAsync(

                ctx.BusinessId, service, value, cancellationToken);



            if (!validation.IsValid)

            {

                return ToolResultHelper.Error(

                    validation.ErrorCode ?? ToolErrorCodes.InvalidAddOns,

                    validation.ErrorMessage ?? "Invalid add-on selection.",

                    validation.Hint,

                    recoverable: true);

            }



            if (!string.IsNullOrWhiteSpace(validation.NormalizedCsv))

                value = validation.NormalizedCsv;

        }



        await _factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, key, value,

            schemaEntry?.ShouldRememberAcrossRequests() ?? false, cancellationToken);

        ctx.Facts[key] = value;



        await ClearDerivedFlowCheckpointsAsync(ctx, key, cancellationToken);
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



    private async Task ClearDerivedFlowCheckpointsAsync(
        AgentToolContext ctx,
        string changedFactKey,
        CancellationToken cancellationToken)
    {
        var factsToClear = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, [changedFactKey]);
        if (factsToClear.Count == 0)
            return;

        var cleared = await _factsService.ClearFieldsAsync(ctx.ConversationId, factsToClear, cancellationToken);
        foreach (var factKey in cleared)
            ctx.Facts.Remove(factKey);
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

            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),

            ttl: null);



        return Task.CompletedTask;

    }



    private static bool TryReadScalarValue(JsonElement valueElement, out string value)

    {

        value = valueElement.ValueKind switch

        {

            JsonValueKind.Null => string.Empty,

            JsonValueKind.String => valueElement.GetString() ?? string.Empty,

            JsonValueKind.Number => valueElement.GetRawText(),

            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),

            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),

            _ => string.Empty

        };



        return valueElement.ValueKind is JsonValueKind.Null

            or JsonValueKind.String

            or JsonValueKind.Number

            or JsonValueKind.True

            or JsonValueKind.False;

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

                    ToolErrorCodes.InvalidType,

                    $"'{key}' must be a number, got '{value}'.",

                    "Provide a numeric value (e.g. 5, 12).",

                    recoverable: true),



            "date" when !System.DateOnly.TryParseExact(value, "yyyy-MM-dd", null,

                             System.Globalization.DateTimeStyles.None, out _) =>

                ToolResultHelper.Error(

                    ToolErrorCodes.InvalidType,

                    $"'{key}' must be a date in YYYY-MM-DD format, got '{value}'.",

                    "Use format YYYY-MM-DD (e.g. 2026-05-23).",

                    recoverable: true),



            "time" when !System.TimeSpan.TryParse(value, out _) =>

                ToolResultHelper.Error(

                    ToolErrorCodes.InvalidType,

                    $"'{key}' must be a time in HH:mm format, got '{value}'.",

                    "Use 24-hour format (e.g. 08:00).",

                    recoverable: true),



            _ => null

        };

    }

}

