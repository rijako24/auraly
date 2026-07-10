using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Commerce;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Lee la configuracion del agente desde BD y la cachea 10 minutos.
/// </summary>
public sealed class AgentConfigProvider : IAgentConfigProvider
{
    private readonly IAgentRepository _agentRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentConfigProvider> _logger;
    private readonly AgentToolMetadataRegistry _toolMetadataRegistry;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CachePrefix = "agent_config_";


    public AgentConfigProvider(
        IAgentRepository agentRepo,
        IMemoryCache cache,
        ILogger<AgentConfigProvider> logger,
        AgentToolMetadataRegistry toolMetadataRegistry)
    {
        _agentRepo = agentRepo;
        _cache = cache;
        _logger = logger;
        _toolMetadataRegistry = toolMetadataRegistry;
    }

    public async Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken ct = default)
    {
        var cacheKey = $"{CachePrefix}{agentId}";

        if (_cache.TryGetValue<AgentConfig>(cacheKey, out var cached))
            return cached!;

        var agent = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");

        var settings = ParseSettings(agent.SettingsJson);

        var normalizedFlows = NormalizeFlows(settings);

        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = agent.BusinessId,
            Name = agent.Name,
            Persona = settings.Persona?.Trim() ?? string.Empty,
            Policies = settings.Policies?.Trim() ?? string.Empty,
            Flow = normalizedFlows.FirstOrDefault(AgentFlowCatalog.IsPrimary) ?? normalizedFlows.FirstOrDefault() ?? new AgentFlowDefinition(),
            Flows = normalizedFlows,
            GlobalActions = settings.GlobalActions ?? [],
            FactSchema = settings.FactSchema ?? [],
            Guards = settings.Guards ?? new Dictionary<string, GuardDefinition>(StringComparer.OrdinalIgnoreCase),
            Templates = settings.Templates ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SystemPrompt = agent.SystemPromptMarkdown?.Trim() ?? string.Empty,
            Model = settings.Model ?? "gpt-4.1-mini",
            Temperature = settings.Temperature ?? 0.7f,
            MaxToolIterations = settings.MaxToolIterations ?? 6,
            HistoryWindowSize = settings.HistoryWindowSize ?? 20,
            ConsecutiveErrorEscalationThreshold = settings.ConsecutiveErrorEscalationThreshold ?? 3,
            EnabledToolNames = settings.EnabledTools ?? [],
            MessageSequences = settings.MessageSequences ?? new MessageSequenceCatalog(),
            Webhooks = settings.Webhooks ?? new WebhookDefinitions(),
            Notifications = settings.Notifications ?? new NotificationDefinitions(),
            Escalations = settings.Escalations ?? new EscalationDefinitions(),
            ReservationAutomations = settings.ReservationAutomations ?? new ReservationAutomationDefinitions(),
            ReservationManagement = settings.ReservationManagement ?? new ReservationManagementDefinitions(),
            Checkout = settings.Checkout ?? new CheckoutDefinitions(),
            Commerce = settings.Commerce ?? new CommerceConfig(),
            OperatingHours = settings.OperatingHours ?? new OperatingHoursDefinitions()
        };

        if (config.EnabledToolNames.Count == 0)
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: enabledTools is empty - agent will have no tools available. Configure tools in SettingsJson",
                agentId);
        }

        _cache.Set(cacheKey, config, CacheTtl);

        _logger.LogInformation(
            "AgentConfig loaded: AgentId={Id}, Model={Model}, Tools={Tools}, Flows={Flows}, FlowStages={Stages}",
            agentId,
            config.Model,
            string.Join(",", config.EnabledToolNames),
            string.Join(",", AgentFlowCatalog.EffectiveFlows(config).Select(flow => flow.Id)),
            AgentFlowCatalog.EffectiveFlows(config).Sum(flow => flow.Stages.Count));

        ValidateConfig(config);

        return config;
    }

    /// <summary>
    /// Valida la coherencia de la configuracion del agente y emite advertencias en log.
    /// No lanza excepciones; la config se acepta aunque tenga inconsistencias menores.
    /// </summary>
    private void ValidateConfig(AgentConfig config)
    {
        var schemaKeys = new HashSet<string>(
            config.FactSchema.Select(e => e.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in config.FactSchema)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: factSchema contains an entry without key",
                    config.AgentId);
                continue;
            }

            var dependencies = entry.DependsOn ?? [];
            foreach (var dependency in dependencies.Where(dep => !string.IsNullOrWhiteSpace(dep)))
            {
                if (entry.Key.Equals(dependency.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: fact '{Key}' dependsOn itself",
                        config.AgentId, entry.Key);
                }

                if (!schemaKeys.Contains(dependency))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: fact '{Key}' dependsOn unknown fact '{Dependency}'",
                        config.AgentId, entry.Key, dependency);
                }
            }

            if (entry.IsCustomerScoped() && dependencies.Count > 0)
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: fact '{Key}' is customer-scoped; dependsOn is ignored for customer facts",
                    config.AgentId, entry.Key);
            }
        }
        foreach (var flow in AgentFlowCatalog.EffectiveFlows(config))
        foreach (var stage in flow.Stages)
        {
            foreach (var factKey in stage.AdvanceWhenFacts)
            {
                if (!schemaKeys.Contains(factKey))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' advanceWhenFacts references unknown fact '{Key}'",
                        config.AgentId, stage.Id, factKey);
                }
            }

            foreach (var factKey in stage.ReentryOnFactChanged)
            {
                if (!schemaKeys.Contains(factKey))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' reentryOnFactChanged references unknown fact '{Key}'",
                        config.AgentId, stage.Id, factKey);
                }
            }

            ValidateEntryActions(
                config,
                $"stage '{stage.Id}'",
                stage.EntryActions,
                stage.AllowedActions,
                schemaKeys);
            if (stage.AutoSetOnSkip.Count > 0 && string.IsNullOrWhiteSpace(stage.SkipWhen))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: stage '{Stage}' has autoSetOnSkip but no skipWhen - auto-set will never trigger",
                    config.AgentId, stage.Id);
            }

            foreach (var rule in stage.AfterTool)
            {
                if (!config.EnabledToolNames.Contains(rule.Tool, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' afterTool references '{Tool}' which is not in enabledTools",
                        config.AgentId, stage.Id, rule.Tool);
                }

                if (!string.IsNullOrWhiteSpace(rule.SetFact.Key) && !schemaKeys.Contains(rule.SetFact.Key))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' afterTool setFact references unknown fact '{Key}'",
                        config.AgentId, stage.Id, rule.SetFact.Key);
                }

                foreach (var factKey in rule.SetFacts.Keys)
                {
                    if (!schemaKeys.Contains(factKey))
                    {
                        _logger.LogWarning(
                            "AgentConfig {AgentId}: stage '{Stage}' afterTool setFacts references unknown fact '{Key}'",
                        config.AgentId, stage.Id, factKey);
                    }
                }

                var sequenceName = rule.SendMessageSequence;
                if (!string.IsNullOrWhiteSpace(sequenceName)
                    && !config.MessageSequences.ContainsKey(sequenceName))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' afterTool references unknown sequence '{Sequence}'",
                        config.AgentId, stage.Id, sequenceName);
                }

                if (string.IsNullOrWhiteSpace(rule.When.Path))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' afterTool for '{Tool}' has empty when.path",
                        config.AgentId, stage.Id, rule.Tool);
                }
            }


        }
        foreach (var action in config.GlobalActions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: globalAction has empty id",
                    config.AgentId);
            }

            ValidateEntryActions(
                config,
                $"globalAction '{action.Id}'",
                action.EntryActions,
                action.AllowedActions,
                schemaKeys);

        }
        var enabledCapabilities = BuildEnabledCapabilities(config);

        // Verificar que los guards solo referencian capabilities habilitadas
        foreach (var (guardKey, _) in config.Guards)
        {
            if (!guardKey.StartsWith("capability:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: guard '{GuardKey}' is not capability-scoped; use 'capability:<id>'",
                    config.AgentId, guardKey);
                continue;
            }

            var capability = guardKey["capability:".Length..];
            if (!enabledCapabilities.Contains(capability))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: guard for capability '{Capability}' exists but no enabled tool exposes it",
                    config.AgentId, capability);
            }
        }

        ValidateAllowedActions(config);
        ValidateMessageSequences(config);
        ValidateNotifications(config);
        ValidateReservationAutomations(config);
        ValidateReservationManagement(config, enabledCapabilities);
        ValidateExternalEscalations(config);
        ValidateTemplates(config);
    }

    private void ValidateEntryActions(
        AgentConfig config,
        string scope,
        IReadOnlyList<StageEntryAction> entryActions,
        IReadOnlyList<string> allowedActions,
        IReadOnlySet<string> schemaKeys)
    {
        foreach (var entryAction in entryActions)
        {
            if (string.IsNullOrWhiteSpace(entryAction.Tool))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: {Scope} entryAction has empty tool name",
                    config.AgentId, scope);
                continue;
            }

            if (!config.EnabledToolNames.Contains(entryAction.Tool, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: {Scope} entryAction references '{Tool}' which is not in enabledTools",
                    config.AgentId, scope, entryAction.Tool);
            }

            if (allowedActions.Count > 0
                && !allowedActions.Contains(entryAction.Tool, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: {Scope} entryAction references '{Tool}' which is not in allowedActions",
                    config.AgentId, scope, entryAction.Tool);
            }

            foreach (var factKey in entryAction.When.RequiredFacts)
            {
                if (!schemaKeys.Contains(factKey))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: {Scope} entryAction requiredFacts references unknown fact '{Key}'",
                        config.AgentId, scope, factKey);
                }
            }

            foreach (var factKey in entryAction.When.MissingFacts)
            {
                if (!schemaKeys.Contains(factKey))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: {Scope} entryAction missingFacts references unknown fact '{Key}'",
                        config.AgentId, scope, factKey);
                }
            }

            foreach (var verificationType in entryAction.When.MissingVerifications)
            {
                if (string.IsNullOrWhiteSpace(verificationType))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: {Scope} entryAction missingVerifications contains an empty verification type",
                        config.AgentId, scope);
                }
            }

            foreach (var messageMatch in entryAction.When.MessageMatches)
            {
                if (messageMatch.AnyOf.Count == 0 || messageMatch.AnyOf.All(string.IsNullOrWhiteSpace))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: {Scope} entryAction messageMatches has no candidates",
                        config.AgentId, scope);
                }
            }
        }
    }
    private void ValidateAllowedActions(AgentConfig config)
    {
        var toolIds = new HashSet<string>(config.EnabledToolNames, StringComparer.OrdinalIgnoreCase);
        foreach (var flow in AgentFlowCatalog.EffectiveFlows(config))
        foreach (var stage in flow.Stages)
        {
            foreach (var toolName in stage.AllowedActions)
            {
                if (!toolIds.Contains(toolName))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' allowedActions references unknown enabled tool '{Tool}'",
                        config.AgentId,
                        stage.Id,
                        toolName);
                }
            }
        }

        foreach (var action in config.GlobalActions)
        {
            foreach (var toolName in action.AllowedActions)
            {
                if (!toolIds.Contains(toolName))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: globalAction '{GlobalAction}' allowedActions references unknown enabled tool '{Tool}'",
                        config.AgentId,
                        action.Id,
                        toolName);
                }
            }
        }
    }
    private HashSet<string> BuildEnabledCapabilities(AgentConfig config)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in _toolMetadataRegistry.GetTools(config.EnabledToolNames))
        {
            foreach (var capability in tool.Capabilities)
            {
                if (!string.IsNullOrWhiteSpace(capability))
                    capabilities.Add(capability);
            }
        }

        return capabilities;
    }

    private void ValidateTemplates(AgentConfig config)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in _toolMetadataRegistry.GetTools(config.EnabledToolNames))
        {
            foreach (var id in tool.RequiredTemplateIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    required.Add(id);
            }
        }

        foreach (var templateId in required)
        {
            if (!config.Templates.TryGetValue(templateId, out var body) || string.IsNullOrWhiteSpace(body))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: enabled tools require template '{TemplateId}' but it is missing or empty in SettingsJson.templates",
                    config.AgentId, templateId);
            }
        }
    }

    private void ValidateMessageSequences(AgentConfig config)
    {
        if (config.Webhooks.Wompi is null)
            return;

        foreach (var (outcomeKey, outcome) in config.Webhooks.Wompi)
        {
            var sequenceName = outcome.SendMessageSequence;
            if (string.IsNullOrWhiteSpace(sequenceName))
                continue;

            if (!config.MessageSequences.ContainsKey(sequenceName))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: webhooks.wompi['{Outcome}'] references unknown sequence '{Sequence}'",
                    config.AgentId, outcomeKey, sequenceName);
            }
        }
    }

    private void ValidateNotifications(AgentConfig config)
    {
        foreach (var (eventName, notification) in config.Notifications)
            if (notification.Enabled)
                ValidateNotification(
                    config,
                    $"notifications['{eventName}']",
                    notification.Recipients,
                    notification.SendMessageSequence);
    }

    private void ValidateNotification(
        AgentConfig config,
        string path,
        IReadOnlyList<string> recipients,
        string? sequenceName)
    {
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} is enabled but recipients is empty",
                config.AgentId,
                path);
        }

        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} is enabled but sendMessageSequence is empty",
                config.AgentId,
                path);
            return;
        }

        if (!config.MessageSequences.ContainsKey(sequenceName))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} references unknown sequence '{Sequence}'",
                config.AgentId,
                path,
                sequenceName);
        }
    }

    private void ValidateReservationAutomations(AgentConfig config)
    {
        ValidateReservationAutomation(config, "reservationAutomations.confirmation", config.ReservationAutomations.Confirmation);
        ValidateReservationAutomation(config, "reservationAutomations.reminder", config.ReservationAutomations.Reminder);
    }

    private void ValidateReservationAutomation(
        AgentConfig config,
        string path,
        ReservationAutomationConfig? automation)
    {
        if (automation is null || !automation.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(automation.SendMessageSequence))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} is enabled but sendMessageSequence is empty",
                config.AgentId,
                path);
        }
        else if (!config.MessageSequences.ContainsKey(automation.SendMessageSequence))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} references unknown sequence '{Sequence}'",
                config.AgentId,
                path,
                automation.SendMessageSequence);
        }

        foreach (var (outcome, action) in automation.Actions)
        {
            ValidateReservationAutomationAction(config, $"{path}.actions['{outcome}']", outcome, action);
        }
    }

    private void ValidateReservationAutomationAction(
        AgentConfig config,
        string path,
        string outcome,
        ReservationAutomationActionConfig action)
    {
        if (string.IsNullOrWhiteSpace(outcome))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} has an empty outcome",
                config.AgentId,
                path);
        }

        if (string.IsNullOrWhiteSpace(action.Tool))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} has an empty tool",
                config.AgentId,
                path);
        }
        else if (!config.EnabledToolNames.Contains(action.Tool, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} references tool '{Tool}' which is not in enabledTools",
                config.AgentId,
                path,
                action.Tool);
        }
        if (!string.IsNullOrWhiteSpace(action.SendMessageSequence)
            && !config.MessageSequences.ContainsKey(action.SendMessageSequence))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: {Path} references unknown sequence '{Sequence}'",
                config.AgentId,
                path,
                action.SendMessageSequence);
        }
    }

    private void ValidateReservationManagement(AgentConfig config, IReadOnlySet<string> enabledCapabilities)
    {
        if (!enabledCapabilities.Contains(ToolCapabilities.ReservationManage))
            return;

        var policy = config.ReservationManagement;
        if (policy.AutomaticChangeFields.Count == 0 && policy.EscalateChangeFields.Count == 0)
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: a reservation management tool is enabled but reservationManagement has no automaticChangeFields or escalateChangeFields",
                config.AgentId);
        }

        var duplicateFields = policy.AutomaticChangeFields
            .Intersect(policy.EscalateChangeFields, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (duplicateFields.Count > 0)
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: reservationManagement has fields configured as both automatic and escalation: {Fields}",
                config.AgentId,
                string.Join(",", duplicateFields));
        }

        if (policy.EscalateChangeFields.Count > 0 && string.IsNullOrWhiteSpace(policy.EscalationReasonCode))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: reservationManagement has escalateChangeFields but escalationReasonCode is empty",
                config.AgentId);
        }
    }
    private void ValidateExternalEscalations(AgentConfig config)
    {
        if (!config.Escalations.External.Enabled)
            return;

        foreach (var (eventName, definition) in config.Escalations.External.Events)
        {
            if (!definition.Enabled)
                continue;

            if (!string.IsNullOrWhiteSpace(definition.Tool)
                && !config.EnabledToolNames.Contains(definition.Tool, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: escalations.external.events['{Event}'] references tool '{Tool}' which is not in enabledTools",
                    config.AgentId,
                    eventName,
                    definition.Tool);
            }

            if (string.IsNullOrWhiteSpace(definition.SendMessageSequence))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: escalations.external.events['{Event}'] enabled but sendMessageSequence is empty",
                    config.AgentId,
                    eventName);
            }
            else if (!config.MessageSequences.ContainsKey(definition.SendMessageSequence))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: escalations.external.events['{Event}'] references unknown sequence '{Sequence}'",
                    config.AgentId,
                    eventName,
                    definition.SendMessageSequence);
            }

            foreach (var (outcomeKey, notificationEventName) in definition.OutcomeEvents)
            {
                ValidateExternalEscalationOutcomeEvent(config, eventName, outcomeKey, notificationEventName);
            }
            foreach (var contact in definition.Contacts)
            {
                if (!contact.BusinessInboundContactId.HasValue)
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: external escalation contact for event '{Event}' must reference businessInboundContactId",
                        config.AgentId,
                        eventName);
                }
            }
        }
    }

    private void ValidateExternalEscalationOutcomeEvent(
        AgentConfig config,
        string externalEventName,
        string outcomeKey,
        string? notificationEventName)
    {
        if (string.IsNullOrWhiteSpace(outcomeKey))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: escalations.external.events['{ExternalEvent}'].outcomeEvents has empty outcome key",
                config.AgentId,
                externalEventName);
            return;
        }

        if (string.IsNullOrWhiteSpace(notificationEventName))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: escalations.external.events['{ExternalEvent}'].outcomeEvents['{Outcome}'] has empty notification event",
                config.AgentId,
                externalEventName,
                outcomeKey);
            return;
        }

        if (!config.Notifications.ContainsKey(notificationEventName))
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: escalations.external.events['{ExternalEvent}'].outcomeEvents['{Outcome}'] references unknown notification event '{NotificationEvent}'",
                config.AgentId,
                externalEventName,
                outcomeKey,
                notificationEventName);
        }
    }

    private static IReadOnlyList<AgentFlowDefinition> NormalizeFlows(AgentSettings settings)
    {
        var configured = settings.Flows?
            .Where(flow => flow.Stages.Count > 0 || !string.IsNullOrWhiteSpace(flow.Id))
            .Select(flow => string.IsNullOrWhiteSpace(flow.Id)
                ? CopyFlow(flow, string.Empty, FlowTypes.Primary)
                : flow)
            .ToList();

        if (configured is { Count: > 0 })
            return configured;

        if (settings.Flow is null)
            return [];

        var legacyId = string.IsNullOrWhiteSpace(settings.Flow.Id)
            ? string.Empty
            : settings.Flow.Id.Trim();

        return [CopyFlow(settings.Flow, legacyId, FlowTypes.Primary)];
    }

    private static AgentFlowDefinition CopyFlow(AgentFlowDefinition source, string id, string type) => new()
    {
        Id = id,
        Type = string.IsNullOrWhiteSpace(source.Type) ? type : source.Type,
        RoutingGuidance = source.RoutingGuidance,
        TtlSeconds = source.TtlSeconds,
        StageDetection = source.StageDetection,
        Stages = source.Stages
    };

    private static AgentSettings ParseSettings(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return new AgentSettings();

        try
        {
            return JsonSerializer.Deserialize<AgentSettings>(settingsJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                })
                ?? new AgentSettings();
        }
        catch
        {
            return new AgentSettings();
        }
    }

    private sealed class AgentSettings
    {
        public string? Persona { get; set; }
        public string? Policies { get; set; }
        public AgentFlowDefinition? Flow { get; set; }
        public IReadOnlyList<AgentFlowDefinition>? Flows { get; set; }
        public IReadOnlyList<AgentGlobalAction>? GlobalActions { get; set; }
        public IReadOnlyList<FactSchemaEntry>? FactSchema { get; set; }
        public Dictionary<string, GuardDefinition>? Guards { get; set; }
        public Dictionary<string, string>? Templates { get; set; }
        public string? Model { get; set; }
        public float? Temperature { get; set; }
        public int? MaxToolIterations { get; set; }
        public int? HistoryWindowSize { get; set; }
        public int? ConsecutiveErrorEscalationThreshold { get; set; }
        public IReadOnlyList<string>? EnabledTools { get; set; }
        public EscalationDefinitions? Escalations { get; set; }
        public MessageSequenceCatalog? MessageSequences { get; set; }
        public WebhookDefinitions? Webhooks { get; set; }
        public NotificationDefinitions? Notifications { get; set; }
        public ReservationAutomationDefinitions? ReservationAutomations { get; set; }
        public ReservationManagementDefinitions? ReservationManagement { get; set; }
        public CheckoutDefinitions? Checkout { get; set; }
        public CommerceConfig? Commerce { get; set; }
        public OperatingHoursDefinitions? OperatingHours { get; set; }
    }

}
