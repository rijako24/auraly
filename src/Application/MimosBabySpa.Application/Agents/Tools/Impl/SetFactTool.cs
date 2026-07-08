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

[AgentToolMetadata("set_fact", Capabilities = new[] { ToolCapabilities.FactWrite })]
public sealed class SetFactTool : IAgentTool

{

    private readonly IConversationFactsService _factsService;

    private readonly ServiceSelectionResolver _serviceSelectionResolver;

    private readonly IAddOnCatalogService _addOnCatalog;

    private readonly IConversationVerificationService _verifications;

    private readonly ILeadService _leadService;
    public SetFactTool(

        IConversationFactsService factsService,

        ServiceSelectionResolver serviceSelectionResolver,

        IAddOnCatalogService addOnCatalog,

        IConversationVerificationService verifications,

        ILeadService leadService)

    {

        _factsService = factsService;

        _serviceSelectionResolver = serviceSelectionResolver;

        _addOnCatalog = addOnCatalog;

        _verifications = verifications;

        _leadService = leadService;
    }



    public string Name => "set_fact";


    public IReadOnlyList<string> Capabilities => [ToolCapabilities.FactWrite];



    public string Description =>
        "Registra en el estado de la conversacion un dato aportado por el cliente. " +
        "Usala SOLO cuando el cliente entregue un dato nuevo o cambie uno existente. " +
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



        // Normalizar alias -> key canonico usando el schema del tenant

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



        // Validacion de tipo basada en schema del tenant (antes de normalizar valor)

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

        if (IsBookingServiceFact(key, schemaEntry))
        {
            return await ResolveAndStoreServiceAsync(
                key,
                value,
                schemaEntry,
                ctx,
                cancellationToken);
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

                    validation.Remediation,

                    recoverable: true);

            }



            if (!string.IsNullOrWhiteSpace(validation.NormalizedCsv))

                value = validation.NormalizedCsv;

        }



        await _factsService.SetAsync(ctx.ConversationId, ctx.BusinessId, key, value,

            schemaEntry?.ShouldRememberAcrossRequests() ?? false, cancellationToken);

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





    private async Task<string> ResolveAndStoreServiceAsync(
        string key,
        string value,
        FactSchemaEntry? schemaEntry,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        var resolution = await _serviceSelectionResolver.ResolveAsync(ctx.BusinessId, value, ct: cancellationToken);
        if (resolution.Status != ServiceSelectionStatus.Resolved || string.IsNullOrWhiteSpace(resolution.ServiceName))
            return ServiceSelectionToolResults.Unresolved(resolution, value);

        ctx.Facts.TryGetValue(key, out var previousValue);
        if (!string.IsNullOrWhiteSpace(previousValue)
            && previousValue.Trim().Equals(resolution.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ToolResultHelper.Ok(new
            {
                key,
                value = resolution.ServiceName,
                selection_status = "resolved",
                unchanged = true,
                storage = "fact_unchanged"
            });
        }

        await _factsService.SetAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            key,
            resolution.ServiceName,
            schemaEntry?.ShouldRememberAcrossRequests() ?? false,
            cancellationToken);

        ctx.Facts[key] = resolution.ServiceName;

        return ToolResultHelper.Ok(new
        {
            key,
            value = resolution.ServiceName,
            selection_status = "resolved",
            storage = "fact"
        });
    }

    private static bool IsBookingServiceFact(string key, FactSchemaEntry? schemaEntry) =>
        key.Equals(ConversationFactKeys.Service, StringComparison.OrdinalIgnoreCase)
        || string.Equals(schemaEntry?.Role, "booking.service", StringComparison.OrdinalIgnoreCase);

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

    /// Devuelve null si pasa la validacion o un JSON de error si no.

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
