using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Time;

internal sealed class TurnPlanPilotRunner
{
    private const string DatabaseCommand = "pilot-turn-plan";
    private const string SeedCommand = "pilot-seed-turn-plan";

    private readonly IAgentConfigProvider _configProvider;
    private readonly AgentConfigurationCompiler _compiler;
    private readonly ITurnPlanner _planner;
    private readonly IBusinessClock _businessClock;

    public TurnPlanPilotRunner(
        IAgentConfigProvider configProvider,
        AgentConfigurationCompiler compiler,
        ITurnPlanner planner,
        IBusinessClock businessClock)
    {
        _configProvider = configProvider;
        _compiler = compiler;
        _planner = planner;
        _businessClock = businessClock;
    }

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Any(arg => arg.Equals(DatabaseCommand, StringComparison.OrdinalIgnoreCase)
            || arg.Equals(SeedCommand, StringComparison.OrdinalIgnoreCase));

    public async Task<int> RunAsync(Guid agentId, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var request = await ResolveRequestAsync(agentId, args, ct);
        if (request is null)
            return 2;

        var compilation = _compiler.Compile(request.Config);
        if (!compilation.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] Configuracion rechazada por compilador:");
            foreach (var diagnostic in compilation.Diagnostics)
                Console.WriteLine($"  - {diagnostic.Path}:{diagnostic.Code}: {diagnostic.Message}");
            Console.ResetColor();
            return 1;
        }

        var stage = EffectiveFlows(request.Config)
            .SelectMany(flow => flow.Stages)
            .FirstOrDefault(candidate => candidate.Id.Equals(request.StageId, StringComparison.OrdinalIgnoreCase));
        if (stage is null)
        {
            var available = EffectiveFlows(request.Config)
                .SelectMany(flow => flow.Stages)
                .Select(candidate => candidate.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"Etapa '{request.StageId}' no encontrada. Disponibles: {string.Join(", ", available)}");
            return 2;
        }

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scope = TurnPlanScopeBuilder.Build(request.Config, stage, facts);
        var context = new TurnPlanningContext(
            request.Config,
            stage,
            scope,
            facts,
            request.Message,
            request.BusinessNow,
            []);

        Console.WriteLine($"[pilot] source={request.Label} stage={stage.Id}");
        Console.WriteLine($"[pilot] structured_output={TurnPlanJsonSchemaBuilder.SchemaName} strict=true");
        Console.WriteLine($"[pilot] flows={string.Join(",", scope.Flows.Keys)} primary={scope.PrimaryFlowId}");
        Console.WriteLine($"[pilot] candidate_stages={string.Join(",", scope.Stages.Select(value => $"{value.FlowId}:{value.StageId}"))}");
        Console.WriteLine($"[pilot] allowed_facts={string.Join(",", scope.Facts.Keys)}");
        Console.WriteLine($"[pilot] allowed_signals={string.Join(",", scope.Signals.Keys)}");

        var proposal = await _planner.PlanAsync(context, ct);
        if (!proposal.Success || proposal.Plan is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL] TurnPlan rechazado:");
            if (proposal.Plan is not null)
                Console.WriteLine(JsonSerializer.Serialize(proposal.Plan, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var error in proposal.Errors)
                Console.WriteLine($"  - {error}");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[PASS] TurnPlan valido");
        Console.ResetColor();
        Console.WriteLine(JsonSerializer.Serialize(proposal.Plan, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[pilot] tokens={proposal.PromptTokens + proposal.CompletionTokens}");
        Console.WriteLine("[pilot] shadow_only=true; no se ejecutaron operaciones ni se escribio estado.");
        return 0;
    }

    private async Task<PilotRequest?> ResolveRequestAsync(
        Guid agentId,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var seedMarker = FindMarker(args, SeedCommand);
        if (seedMarker >= 0)
        {
            if (seedMarker + 3 >= args.Count)
            {
                Console.WriteLine("Uso: pilot-seed-turn-plan <seed.sql> <stage-id> <mensaje>");
                return null;
            }

            var seedPath = Path.GetFullPath(args[seedMarker + 1]);
            var config = LoadSeedConfig(seedPath);
            var message = string.Join(' ', args.Skip(seedMarker + 3));
            return new PilotRequest(
                config,
                args[seedMarker + 2],
                message,
                DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)),
                Path.GetFileName(seedPath));
        }

        var marker = FindMarker(args, DatabaseCommand);
        if (marker < 0 || marker + 2 >= args.Count)
        {
            Console.WriteLine("Uso: <agente> pilot-turn-plan <stage-id> <mensaje>");
            return null;
        }

        var loaded = await _configProvider.GetConfigAsync(agentId, cancellationToken);
        var clock = await _businessClock.GetSnapshotAsync(loaded.BusinessId, cancellationToken);
        return new PilotRequest(
            loaded,
            args[marker + 1],
            string.Join(' ', args.Skip(marker + 2)),
            clock.Now,
            loaded.Name);
    }

    private static AgentConfig LoadSeedConfig(string path)
    {
        var sql = File.ReadAllText(path);
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException($"No se encontro @SettingsJson en {path}.");

        var json = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        return JsonSerializer.Deserialize<AgentConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        }) ?? throw new InvalidOperationException($"No se pudo deserializar @SettingsJson de {path}.");
    }

    private static int FindMarker(IReadOnlyList<string> args, string command)
    {
        for (var index = 0; index < args.Count; index++)
            if (args[index].Equals(command, StringComparison.OrdinalIgnoreCase))
                return index;
        return -1;
    }

    private static IReadOnlyList<AgentFlowDefinition> EffectiveFlows(AgentConfig config) =>
        config.Flows.Count > 0
            ? config.Flows
            : config.Flow.Stages.Count > 0
                ? [config.Flow]
                : [];

    private sealed record PilotRequest(
        AgentConfig Config,
        string StageId,
        string Message,
        DateTimeOffset BusinessNow,
        string Label);
}