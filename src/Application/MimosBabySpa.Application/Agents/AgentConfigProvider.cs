using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Lee la configuración del agente desde BD y la cachea 10 minutos.
/// Construye el system prompt final ensamblando AgentPromptSections + KnowledgeSources.
/// </summary>
public sealed class AgentConfigProvider : IAgentConfigProvider
{
    private readonly IAgentRepository _agentRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentConfigProvider> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CachePrefix = "agent_config_";

    // Nombres de tools habilitadas por defecto si el agente no especifica la lista
    private static readonly IReadOnlyList<string> DefaultEnabledTools =
    [
        "check_availability", "resolve_pricing", "create_reservation",
        "reschedule_reservation", "suspend_reservation",
        "generate_payment_link", "verify_payment",
        "escalate_to_human", "get_service_catalog"
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

        // Construir system prompt desde secciones ordenadas + KS auto-inject
        var systemPrompt = BuildSystemPrompt(agent, settings);

        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = agent.BusinessId,
            Name = agent.Name,
            SystemPrompt = systemPrompt,
            Model = settings.Model ?? "gpt-4o-mini",
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

    private static string BuildSystemPrompt(Domain.Entities.Agent agent, AgentSettings settings)
    {
        var sb = new StringBuilder();

        // 1. Secciones de prompt ordenadas por DisplayOrder e InjectionPoint
        var sections = agent.PromptSections
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ToList();

        foreach (var section in sections.Where(s => s.InjectionPoint == "system_header"))
            AppendSection(sb, section.Title, section.Content);

        foreach (var section in sections.Where(s => s.InjectionPoint == "before_instructions"))
            AppendSection(sb, section.Title, section.Content);

        // 2. KnowledgeSources marcados como AutoInject
        var autoKs = agent.KnowledgeSources
            .Where(aks => aks.AutoInject && aks.KnowledgeSource.IsActive)
            .OrderBy(aks => aks.DisplayOrder)
            .Select(aks => aks.KnowledgeSource)
            .ToList();

        if (autoKs.Count > 0)
        {
            sb.AppendLine("## KNOWLEDGE BASE");
            foreach (var ks in autoKs)
            {
                sb.AppendLine($"### {ks.Name}");
                sb.AppendLine(ks.Content);
                sb.AppendLine();
            }
        }

        foreach (var section in sections.Where(s => s.InjectionPoint == "after_instructions"))
            AppendSection(sb, section.Title, section.Content);

        foreach (var section in sections.Where(s => s.InjectionPoint == "context_footer"))
            AppendSection(sb, section.Title, section.Content);

        return sb.ToString().Trim();
    }

    private static void AppendSection(StringBuilder sb, string title, string content)
    {
        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine($"## {title.ToUpperInvariant()}");
        sb.AppendLine(content.Trim());
        sb.AppendLine();
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
        public EscalationSettings? Escalation { get; set; }
        public IReadOnlyList<string>? EscalationContacts =>
            Escalation?.Contacts ?? [];
    }

    private sealed class EscalationSettings
    {
        public IReadOnlyList<string>? Contacts { get; set; }
    }
}
