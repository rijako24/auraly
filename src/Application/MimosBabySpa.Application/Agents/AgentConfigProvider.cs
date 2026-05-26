using MimosBabySpa.Application.Agents.Configuration;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents;

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
            PromptSections = settings.PromptSections ?? [],
            Flow = settings.Flow ?? new AgentFlowDefinition(),
            FactSchema = settings.FactSchema ?? [],
            Guards = settings.Guards ?? new Dictionary<string, GuardDefinition>(StringComparer.OrdinalIgnoreCase),
            Templates = settings.Templates ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SystemPrompt = agent.SystemPromptMarkdown?.Trim() ?? string.Empty,
            KillSwitchPhrases = settings.KillSwitchPhrases ?? [],
            HumanMessages = settings.HumanMessages ?? new AgentHumanMessages(),
            OperationalLimits = settings.OperationalLimits ?? new AgentOperationalLimits(),
            CapabilityPacks = settings.CapabilityPacks ?? [Packs.Booking.BookingPackIds.Booking],
            Model = settings.Model ?? string.Empty,
            Temperature = settings.Temperature ?? 0.7f,
            MaxToolIterations = settings.MaxToolIterations ?? 6,
            ConsecutiveErrorEscalationThreshold = settings.ConsecutiveErrorEscalationThreshold ?? 3,
            EnabledToolNames = settings.EnabledTools ?? [],
            EscalationContacts = settings.EscalationContacts ?? []
        };

        if (string.IsNullOrWhiteSpace(config.Model))
            _logger.LogWarning("AgentConfig {AgentId}: model is not configured.", agentId);

        if (string.IsNullOrWhiteSpace(config.HumanMessages.EscalationUserMessage))
            _logger.LogWarning("AgentConfig {AgentId}: humanMessages.escalationUserMessage is not configured.", agentId);

        _cache.Set(cacheKey, config, CacheTtl);

        _logger.LogInformation(
            "AgentConfig loaded: AgentId={Id} Model={Model} FlowStages={Stages} Templates={Tpls}",
            agentId, config.Model, config.Flow.Stages.Count, config.Templates.Count);

        return config;
    }

    private static AgentSettings ParseSettings(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return new AgentSettings();
        try
        {
            return JsonSerializer.Deserialize<AgentSettings>(settingsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AgentSettings();
        }
        catch { return new AgentSettings(); }
    }

    private sealed class AgentSettings
    {
        public IReadOnlyList<PromptSection>? PromptSections { get; set; }
        public AgentFlowDefinition? Flow { get; set; }
        public IReadOnlyList<FactSchemaEntry>? FactSchema { get; set; }
        public Dictionary<string, GuardDefinition>? Guards { get; set; }
        public Dictionary<string, string>? Templates { get; set; }
        public string? Model { get; set; }
        public float? Temperature { get; set; }
        public int? MaxToolIterations { get; set; }
        public int? ConsecutiveErrorEscalationThreshold { get; set; }
        public IReadOnlyList<string>? EnabledTools { get; set; }
        public IReadOnlyList<string>? CapabilityPacks { get; set; }
        public IReadOnlyList<string>? KillSwitchPhrases { get; set; }
        public AgentHumanMessages? HumanMessages { get; set; }
        public AgentOperationalLimits? OperationalLimits { get; set; }
        public EscalationSettings? Escalation { get; set; }
        public IReadOnlyList<string>? EscalationContacts =>
            Escalation?.Contacts ?? [];
    }

    private sealed class EscalationSettings
    {
        public IReadOnlyList<string>? Contacts { get; set; }
    }
}
