using MimosBabySpa.Application.Agents.Configuration;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Lee la configuración del agente desde BD y la cachea 10 minutos.
/// </summary>
public sealed class AgentConfigProvider : IAgentConfigProvider
{
    private readonly IAgentRepository _agentRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentConfigProvider> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CachePrefix = "agent_config_";


    public AgentConfigProvider(
        IAgentRepository agentRepo,
        IMemoryCache cache,
        ILogger<AgentConfigProvider> logger)
    {
        _agentRepo = agentRepo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken ct = default)
    {
        var cacheKey = $"{CachePrefix}{agentId}";

        if (_cache.TryGetValue<AgentConfig>(cacheKey, out var cached))
            return cached!;

        var agent = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");

        var settings = ParseSettings(agent.SettingsJson);

        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = agent.BusinessId,
            Name = agent.Name,
            Persona = settings.Persona?.Trim() ?? string.Empty,
            Policies = settings.Policies?.Trim() ?? string.Empty,
            Flow = settings.Flow ?? new AgentFlowDefinition(),
            FactSchema = settings.FactSchema ?? [],
            Guards = settings.Guards ?? new Dictionary<string, GuardDefinition>(StringComparer.OrdinalIgnoreCase),
            Templates = settings.Templates ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SystemPrompt = agent.SystemPromptMarkdown?.Trim() ?? string.Empty,
            KillSwitchPhrases = settings.KillSwitchPhrases ?? [],
            Model = settings.Model ?? "gpt-4.1-mini",
            Temperature = settings.Temperature ?? 0.7f,
            MaxToolIterations = settings.MaxToolIterations ?? 6,
            HistoryWindowSize = settings.HistoryWindowSize ?? 20,
            ConsecutiveErrorEscalationThreshold = settings.ConsecutiveErrorEscalationThreshold ?? 3,
            EnabledToolNames = settings.EnabledTools ?? [],
            EscalationContacts = settings.EscalationContacts ?? [],
            MessageSequences = settings.MessageSequences ?? new MessageSequenceCatalog(),
            Webhooks = settings.Webhooks ?? new WebhookDefinitions(),
            Checkout = settings.Checkout ?? new CheckoutDefinitions()
        };

        if (config.EnabledToolNames.Count == 0)
        {
            _logger.LogWarning(
                "AgentConfig {AgentId}: enabledTools is empty — agent will have no tools available. Configure tools in SettingsJson.",
                agentId);
        }

        _cache.Set(cacheKey, config, CacheTtl);

        _logger.LogInformation(
            "AgentConfig loaded: AgentId={Id}, Model={Model}, Tools={Tools}, FlowStages={Stages}",
            agentId, config.Model, string.Join(",", config.EnabledToolNames), config.Flow.Stages.Count);

        ValidateConfig(config);

        return config;
    }

    /// <summary>
    /// Valida la coherencia de la configuración del agente y emite advertencias en log.
    /// No lanza excepciones — la config se acepta aunque tenga inconsistencias menores.
    /// </summary>
    private void ValidateConfig(AgentConfig config)
    {
        var schemaKeys = new HashSet<string>(
            config.FactSchema.Select(e => e.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var stage in config.Flow.Stages)
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

            if (stage.AutoSetOnSkip.Count > 0 && string.IsNullOrWhiteSpace(stage.SkipWhen))
            {
                _logger.LogWarning(
                    "AgentConfig {AgentId}: stage '{Stage}' has autoSetOnSkip but no skipWhen — auto-set will never trigger",
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

                if (string.IsNullOrWhiteSpace(rule.When.Path))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' afterTool for '{Tool}' has empty when.path",
                        config.AgentId, stage.Id, rule.Tool);
                }
            }

            foreach (var toolName in stage.AllowedTools)
            {
                if (!config.EnabledToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "AgentConfig {AgentId}: stage '{Stage}' allowedTools references '{Tool}' which is not in enabledTools",
                        config.AgentId, stage.Id, toolName);
                }
            }
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

        ValidateMessageSequences(config);
        ValidateTemplates(config);
    }

    private static HashSet<string> BuildEnabledCapabilities(AgentConfig config)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in config.EnabledToolNames)
        {
            foreach (var capability in ResolveKnownCapabilities(toolName))
                capabilities.Add(capability);
        }

        return capabilities;
    }

    private static IEnumerable<string> ResolveKnownCapabilities(string toolName)
    {
        if (toolName.Equals("set_fact", StringComparison.OrdinalIgnoreCase))
            yield return Tools.ToolCapabilities.FactWrite;
        else if (toolName.Equals("escalate_to_human", StringComparison.OrdinalIgnoreCase))
            yield return Tools.ToolCapabilities.HumanEscalate;
        else if (toolName.Equals("prepare_checkout", StringComparison.OrdinalIgnoreCase))
            yield return Tools.ToolCapabilities.CheckoutPrepare;
        else if (toolName.Equals("create_reservation", StringComparison.OrdinalIgnoreCase))
            yield return Tools.ToolCapabilities.ReservationCreate;
        else if (toolName.Equals("assign_paid_slot", StringComparison.OrdinalIgnoreCase))
            yield return Tools.ToolCapabilities.PaidSlotAssign;
    }

    /// <summary>
    /// Plantillas que las tools del motor pueden emitir según enabledTools.
    /// Deben existir en SettingsJson.templates (no hay fallback en código).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ToolRequiredTemplateIds =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["check_availability"] = ["availability_slots"]
        };

    private void ValidateTemplates(AgentConfig config)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in config.EnabledToolNames)
        {
            if (ToolRequiredTemplateIds.TryGetValue(toolName, out var templateIds))
            {
                foreach (var id in templateIds)
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

    private static AgentSettings ParseSettings(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return new AgentSettings();

        try
        {
            return JsonSerializer.Deserialize<AgentSettings>(settingsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
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
        public IReadOnlyList<FactSchemaEntry>? FactSchema { get; set; }
        public Dictionary<string, GuardDefinition>? Guards { get; set; }
        public Dictionary<string, string>? Templates { get; set; }
        public string? Model { get; set; }
        public float? Temperature { get; set; }
        public int? MaxToolIterations { get; set; }
        public int? HistoryWindowSize { get; set; }
        public int? ConsecutiveErrorEscalationThreshold { get; set; }
        public IReadOnlyList<string>? EnabledTools { get; set; }
        public IReadOnlyList<string>? KillSwitchPhrases { get; set; }
        public EscalationSettings? Escalation { get; set; }
        public MessageSequenceCatalog? MessageSequences { get; set; }
        public WebhookDefinitions? Webhooks { get; set; }
        public CheckoutDefinitions? Checkout { get; set; }
        public IReadOnlyList<string>? EscalationContacts =>
            Escalation?.Contacts ?? [];
    }

    private sealed class EscalationSettings
    {
        public IReadOnlyList<string>? Contacts { get; set; }
    }
}
