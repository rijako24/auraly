using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Lee la configuración del agente desde BD y la cachea 10 minutos.
/// El system prompt se obtiene directamente de Agents.SystemPromptMarkdown.
/// </summary>
public sealed class AgentConfigProvider : IAgentConfigProvider
{
    private readonly IAgentRepository _agentRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentConfigProvider> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CachePrefix = "agent_config_";

    private static readonly IReadOnlyList<string> DefaultEnabledTools =
    [
        "set_fact",
        "check_availability",
        "prepare_checkout",
        "create_reservation",
        "reschedule_reservation",
        "suspend_reservation",
        "verify_payment",
        "escalate_to_human",
        "get_service_catalog"
    ];

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
            SystemPrompt = agent.SystemPromptMarkdown?.Trim() ?? string.Empty,
            FirstTurnGreetingHint = settings.Messages?.FirstTurnGreetingHint?.Trim(),
            Model = settings.Model ?? "gpt-4.1-mini",
            Temperature = settings.Temperature ?? 0.7f,
            MaxToolIterations = settings.MaxToolIterations ?? 6,
            ConsecutiveErrorEscalationThreshold = settings.ConsecutiveErrorEscalationThreshold ?? 3,
            EnabledToolNames = settings.EnabledTools ?? DefaultEnabledTools,
            EscalationContacts = settings.EscalationContacts ?? []
        };

        _cache.Set(cacheKey, config, CacheTtl);

        _logger.LogInformation(
            "AgentConfig loaded: AgentId={Id}, Model={Model}, Tools={Tools}",
            agentId, config.Model, string.Join(",", config.EnabledToolNames));

        return config;
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
        public string? Model { get; set; }
        public float? Temperature { get; set; }
        public int? MaxToolIterations { get; set; }
        public int? ConsecutiveErrorEscalationThreshold { get; set; }
        public IReadOnlyList<string>? EnabledTools { get; set; }
        public AgentMessagesSettings? Messages { get; set; }
        public EscalationSettings? Escalation { get; set; }
        public IReadOnlyList<string>? EscalationContacts =>
            Escalation?.Contacts ?? [];
    }

    private sealed class AgentMessagesSettings
    {
        public string? FirstTurnGreetingHint { get; set; }
    }

    private sealed class EscalationSettings
    {
        public IReadOnlyList<string>? Contacts { get; set; }
    }
}
