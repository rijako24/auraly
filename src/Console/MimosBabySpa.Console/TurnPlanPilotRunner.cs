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
    private const string EvaluationCommand = "eval-seed-extractor";

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
            || arg.Equals(SeedCommand, StringComparison.OrdinalIgnoreCase)
            || arg.Equals(EvaluationCommand, StringComparison.OrdinalIgnoreCase));

    public async Task<int> RunAsync(Guid agentId, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var evaluationMarker = FindMarker(args, EvaluationCommand);
        if (evaluationMarker >= 0)
            return await RunEvaluationSuiteAsync(args, evaluationMarker, ct);

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

    private async Task<int> RunEvaluationSuiteAsync(
        IReadOnlyList<string> args,
        int marker,
        CancellationToken cancellationToken)
    {
        if (marker + 1 >= args.Count)
        {
            Console.WriteLine("Uso: eval-seed-extractor <suite.json>");
            return 2;
        }

        var suitePath = Path.GetFullPath(args[marker + 1]);
        var suite = JsonSerializer.Deserialize<ExtractorEvaluationSuite>(
            await File.ReadAllTextAsync(suitePath, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Suite vacia: {suitePath}.");

        var failures = 0;
        var repetitions = Math.Max(1, suite.Repetitions);
        var total = suite.Cases.Count * repetitions;
        var suiteDirectory = Path.GetDirectoryName(suitePath) ?? Environment.CurrentDirectory;
        foreach (var test in suite.Cases)
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var caseLabel = repetitions == 1 ? test.Id : $"{test.Id}#{repetition}";
            var config = test.FactSchema.Count > 0
                ? CreateExtractionEvaluationConfig(test)
                : LoadSeedConfig(Path.GetFullPath(Path.Combine(suiteDirectory, test.Seed)));
            var compilation = _compiler.Compile(config);
            if (!compilation.IsValid)
            {
                failures++;
                Console.WriteLine($"[FAIL] {caseLabel}: configuracion invalida");
                foreach (var diagnostic in compilation.Diagnostics)
                    Console.WriteLine($"  - {diagnostic.Path}:{diagnostic.Code}: {diagnostic.Message}");
                continue;
            }

            var stage = config.Flows.SelectMany(flow => flow.Stages)
                .FirstOrDefault(candidate => candidate.Id.Equals(test.Stage, StringComparison.OrdinalIgnoreCase));
            if (stage is null)
            {
                failures++;
                Console.WriteLine($"[FAIL] {caseLabel}: etapa '{test.Stage}' no encontrada.");
                continue;
            }

            var businessNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5));
            var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var scope = TurnPlanScopeBuilder.Build(config, stage, facts);
            var proposal = await _planner.PlanAsync(
                new TurnPlanningContext(config, stage, scope, facts, test.Message, businessNow, []),
                cancellationToken);

            var errors = ValidateEvaluation(test, proposal, businessNow);
            if (errors.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {caseLabel}");
                Console.ResetColor();
            }
            else
            {
                failures++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {caseLabel}");
                Console.ResetColor();
                foreach (var error in errors)
                    Console.WriteLine($"  - {error}");
                if (proposal.Plan is not null)
                    Console.WriteLine(JsonSerializer.Serialize(proposal.Plan, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        Console.WriteLine($"[extractor-eval] total={total} passed={total - failures} failed={failures}");
        Console.WriteLine("[extractor-eval] Solo se consult? el LLM; no se ejecutaron operaciones ni se escribi? estado.");
        return failures == 0 ? 0 : 1;
    }

    private static AgentConfig CreateExtractionEvaluationConfig(ExtractorEvaluationCase test)
    {
        var flowId = string.IsNullOrWhiteSpace(test.ExpectedFlow) ? "extraction" : test.ExpectedFlow;
        var stageId = string.IsNullOrWhiteSpace(test.Stage) ? "extraction" : test.Stage;
        return new AgentConfig
        {
            AgentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            BusinessId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Name = "Extractor evaluation",
            Model = "gpt-4.1-mini",
            Temperature = 0,
            FactSchema = test.FactSchema,
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = flowId!,
                    Type = FlowTypes.Primary,
                    RoutingGuidance = "Extraccion aislada para evaluacion.",
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = stageId,
                            Goal = "Extraer ?nicamente los datos declarados.",
                            Collect = test.FactSchema.Select(entry => entry.Key).ToArray(),
                            ConversationGuidance = "No inventes datos. Extrae solo evidencia expl?cita del mensaje."
                        }
                    ]
                }
            ]
        };
    }

    private static List<string> ValidateEvaluation(
        ExtractorEvaluationCase test,
        TurnPlanProposal proposal,
        DateTimeOffset businessNow)
    {
        var errors = new List<string>();
        if (!proposal.Success || proposal.Plan is null)
        {
            errors.AddRange(proposal.Errors.Count > 0 ? proposal.Errors : ["El extractor no produjo un TurnPlan v?lido."]);
            return errors;
        }

        var plan = proposal.Plan;
        if (!string.IsNullOrWhiteSpace(test.ExpectedFlow)
            && !plan.FlowIntent.CandidateFlow.Equals(test.ExpectedFlow, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Flujo esperado '{test.ExpectedFlow}', recibido '{plan.FlowIntent.CandidateFlow}'.");

        foreach (var expected in test.ExpectedFacts)
        {
            var claim = plan.Facts.LastOrDefault(value =>
                value.Key.Equals(expected.Key, StringComparison.OrdinalIgnoreCase)
                && value.Operation.Equals(TurnPlanOperations.Set, StringComparison.OrdinalIgnoreCase));
            if (claim is null)
            {
                errors.Add($"No extrajo fact '{expected.Key}'.");
                continue;
            }

            if (expected.DateOffsetDays is int offset)
            {
                var expectedDate = DateOnly.FromDateTime(businessNow.Date).AddDays(offset).ToString("yyyy-MM-dd");
                if (!StringValue(claim.Value).Equals(expectedDate, StringComparison.Ordinal))
                    errors.Add($"Fact '{expected.Key}' esperaba fecha '{expectedDate}', recibi? {claim.Value.GetRawText()}.");
            }
            else if (expected.ExpectedValue.ValueKind != JsonValueKind.Undefined
                && !JsonValuesEqual(expected.ExpectedValue, claim.Value))
            {
                errors.Add($"Fact '{expected.Key}' esperaba {expected.ExpectedValue.GetRawText()}, recibi? {claim.Value.GetRawText()}.");
            }
        }

        foreach (var absent in test.AbsentFacts)
            if (plan.Facts.Any(value => value.Key.Equals(absent, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Fact '{absent}' deb?a permanecer sin extraer.");

        foreach (var signal in test.ExpectedSignals)
            if (!plan.Signals.Any(value => value.Type.Equals(signal, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"No extrajo signal '{signal}'.");

        foreach (var ambiguous in test.ExpectedAmbiguousFields)
            if (!plan.Response.AmbiguousFields.Contains(ambiguous, StringComparer.OrdinalIgnoreCase))
                errors.Add($"No marc? '{ambiguous}' como ambiguo.");

        return errors;
    }

    private static string StringValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static bool JsonValuesEqual(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
            return false;
        if (expected.ValueKind != JsonValueKind.String)
            return string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal);

        static string Normalize(string value)
        {
            var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            return new string(decomposed.Where(character =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                    != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
                .Normalize(System.Text.NormalizationForm.FormC);
        }

        return Normalize(expected.GetString() ?? string.Empty)
            .Equals(Normalize(actual.GetString() ?? string.Empty), StringComparison.Ordinal);
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
        config.Flows;

    private sealed class ExtractorEvaluationSuite
    {
        public int Repetitions { get; init; } = 1;
        public IReadOnlyList<ExtractorEvaluationCase> Cases { get; init; } = [];
    }

    private sealed class ExtractorEvaluationCase
    {
        public string Id { get; init; } = string.Empty;
        public string Seed { get; init; } = string.Empty;
        public IReadOnlyList<FactSchemaEntry> FactSchema { get; init; } = [];
        public string Stage { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string? ExpectedFlow { get; init; }
        public IReadOnlyList<ExpectedFact> ExpectedFacts { get; init; } = [];
        public IReadOnlyList<string> AbsentFacts { get; init; } = [];
        public IReadOnlyList<string> ExpectedSignals { get; init; } = [];
        public IReadOnlyList<string> ExpectedAmbiguousFields { get; init; } = [];
    }

    private sealed class ExpectedFact
    {
        public string Key { get; init; } = string.Empty;
        [JsonPropertyName("equals")]
        public JsonElement ExpectedValue { get; init; }
        public int? DateOffsetDays { get; init; }
    }

    private sealed record PilotRequest(
        AgentConfig Config,
        string StageId,
        string Message,
        DateTimeOffset BusinessNow,
        string Label);
}
