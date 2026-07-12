using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents;

public sealed class AgentConfigProvider : IAgentConfigProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const string CachePrefix = "agent_config_";
    private readonly IAgentRepository _agents;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentConfigProvider> _logger;
    private readonly AgentConfigurationCompiler _compiler;

    public AgentConfigProvider(
        IAgentRepository agents,
        IMemoryCache cache,
        ILogger<AgentConfigProvider> logger,
        AgentConfigurationCompiler compiler)
    {
        _agents = agents;
        _cache = cache;
        _logger = logger;
        _compiler = compiler;
    }

    public async Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken ct = default)
    {
        var cacheKey = $"{CachePrefix}{agentId}";
        if (_cache.TryGetValue<AgentConfig>(cacheKey, out var cached))
            return cached!;

        var agent = await _agents.GetByIdAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found.");
        var settings = ParseSettings(agent.SettingsJson, agentId);
        if (settings.Flows is not { Count: > 0 })
            throw new InvalidOperationException($"Agent configuration {agentId} must declare at least one flow.");

        var flows = settings.Flows.ToList();
        var config = new AgentConfig
        {
            AgentId = agentId,
            BusinessId = agent.BusinessId,
            Name = agent.Name,
            Persona = settings.Persona?.Trim() ?? string.Empty,
            Policies = settings.Policies?.Trim() ?? string.Empty,
            Flows = flows,
            GlobalActions = settings.GlobalActions ?? [],
            FactSchema = settings.FactSchema ?? [],
            Templates = settings.Templates ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ConversationOpening = settings.ConversationOpening ?? new ConversationOpeningDefinitions(),
            FailureResponses = settings.FailureResponses ?? new FailureResponseDefinitions(),
            Model = settings.Model ?? "gpt-4.1-mini",
            Temperature = settings.Temperature ?? 0.2f,
            HistoryWindowSize = settings.HistoryWindowSize ?? 20,
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

        var compilation = _compiler.Compile(config);
        if (!compilation.IsValid)
        {
            var diagnostics = string.Join("; ", compilation.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Path}:{diagnostic.Code}:{diagnostic.Message}"));
            _logger.LogError("AgentConfig {AgentId} rejected by compiler: {Diagnostics}", agentId, diagnostics);
            throw new InvalidOperationException($"Agent configuration {agentId} is invalid: {diagnostics}");
        }

        _cache.Set(cacheKey, config, CacheTtl);
        _logger.LogInformation(
            "Deterministic AgentConfig loaded: AgentId={AgentId}, Model={Model}, Flows={Flows}, Stages={Stages}",
            agentId,
            config.Model,
            string.Join(",", flows.Select(flow => flow.Id)),
            flows.Sum(flow => flow.Stages.Count));
        return config;
    }

    public void Invalidate(Guid agentId) => _cache.Remove($"{CachePrefix}{agentId}");

    private static AgentSettings ParseSettings(string? json, Guid agentId)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Agent configuration {agentId} is empty.");

        try
        {
            return JsonSerializer.Deserialize<AgentSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            }) ?? throw new InvalidOperationException($"Agent configuration {agentId} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Agent configuration {agentId} is not valid JSON: {exception.Message}", exception);
        }
    }

    private sealed class AgentSettings
    {
        public string? Persona { get; set; }
        public string? Policies { get; set; }
        public IReadOnlyList<AgentFlowDefinition>? Flows { get; set; }
        public IReadOnlyList<AgentGlobalAction>? GlobalActions { get; set; }
        public IReadOnlyList<FactSchemaEntry>? FactSchema { get; set; }
        public Dictionary<string, string>? Templates { get; set; }
        public ConversationOpeningDefinitions? ConversationOpening { get; set; }
        public FailureResponseDefinitions? FailureResponses { get; set; }
        public string? Model { get; set; }
        public float? Temperature { get; set; }
        public int? HistoryWindowSize { get; set; }
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
