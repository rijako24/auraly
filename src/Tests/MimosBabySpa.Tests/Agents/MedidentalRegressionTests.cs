using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class MedidentalRegressionTests
{
    [Fact]
    public void Seed_UsesLocalCommerceAndRoutesOpenCatalogQueriesToCategories()
    {
        var (config, _) = LoadSeed();

        config.Commerce.Enabled.Should().BeTrue();
        config.Commerce.Provider.Should().Be(CommerceProvider.Local);

        var catalog = config.GlobalActions.Single(action => action.Id == "catalog_lookup");
        catalog.Signal.Type.Should().Be("catalog_query");
        catalog.ConversationGuidance.Should()
            .NotContain("mode=")
            .And.Contain("resultados autoritativos");
        catalog.Signal.Description.Should()
            .Contain("Clasifica intent antes de extraer target")
            .And.Contain("sustantivos genericos");

        var intentBranches = catalog.Signal.ValueSchema
            .GetProperty("anyOf")
            .EnumerateArray()
            .ToList();
        intentBranches.Should().HaveCount(3);
        intentBranches
            .Select(branch => branch
                .GetProperty("properties")
                .GetProperty("intent")
                .GetProperty("enum")[0]
                .GetString())
            .Should()
            .Equal("explore_catalog", "search_target", "continue_results");

        var searchBranch = intentBranches[1];
        var targetSchema = searchBranch
            .GetProperty("properties")
            .GetProperty("target");
        targetSchema.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .Equal("kind", "text");

        var exploreTarget = intentBranches[0]
            .GetProperty("properties")
            .GetProperty("target");
        exploreTarget.GetProperty("type").GetString().Should().Be("null");

        var search = catalog.Actions.Single(action => action.Operation == "commerce.search_products");
        search.Arguments["query"].GetString().Should()
            .Be("{{signal.catalog_query.value.target.text}}");
        search.Arguments["mode"].GetString().Should()
            .Be("{{signal.catalog_query.value.intent}}");
        search.OnOutcome["products.found"].Effects.Should().ContainSingle(effect =>
            effect.Type == "presentation.add"
            && effect.Template == "catalog_results");
        search.OnOutcome["categories.found"].Effects.Should().ContainSingle(effect =>
            effect.Type == "presentation.add"
            && effect.Template == "catalog_categories");

    }

    [Fact]
    public void OpenCatalogTemplate_ListsOfficialCategoriesAndInvitesAConcreteRequest()
    {
        var (config, _) = LoadSeed();
        var categories = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "Implantologia"
            },
            new Dictionary<string, object?>
            {
                ["name"] = "Fotocurado"
            }
        };

        var rendered = new PromptTemplateRenderer().Render(
            config.Templates["catalog_categories"],
            new Dictionary<string, object?>
            {
                ["categories"] = categories
            });

        rendered.Should()
            .Contain("Implantologia")
            .And.Contain("Fotocurado")
            .And.Contain("Elija una para ver sus productos")
            .And.Contain("d\u00edgame directamente qu\u00e9 necesita");
        rendered.Should().NotContain("$").And.NotContain("0.00");
    }

    [Fact]
    public void Seed_UsesSemanticFinalizationAndKeepsPersonAndEstablishmentDistinct()
    {
        var (config, _) = LoadSeed();

        config.ConversationOpening.Guidance.Should()
            .Contain("¡Hola, Doc! Bienvenido a Medidental. Es un gusto atenderle 😊")
            .And.Contain("No agregues otra frase");

        config.Templates["catalog_results"].Should()
            .Contain("manejamos equipos, materiales y consumibles odontol\u00f3gicos")
            .And.Contain("D\u00edgame cu\u00e1l le interesa")
            .And.NotContain("Contamos con una gran variedad de productos odontologicos");
        config.Templates["cart_snapshot"].Should()
            .NotContain("solo dime que eso es todo")
            .And.Contain("Cuando haya terminado de elegir");

        var orderFlow = config.Flows.Single(flow => flow.Id == "order");
        orderFlow.Stages[0].Id.Should().Be("product_selection");
        orderFlow.Stages[0].Collect.Should().Contain("order_finalized",
            "the semantic closing fact must be available throughout product selection");

        var identityStage = orderFlow.Stages[1];
        identityStage.Id.Should().Be("customer_identity");
        identityStage.AdvanceWhenFacts.Should().Equal("customer_name");
        identityStage.Collect.Should().Contain(["customer_name", "company_name"]);
        identityStage.ConversationGuidance.Should()
            .Contain("registra ese dato como company_name y no como customer_name")
            .And.Contain("company_name es opcional");

        var customerName = config.FactSchema.Single(fact => fact.Key == "customer_name");
        customerName.Role.Should().Be("customer.name");
        customerName.Required.Should().BeTrue();
        customerName.Label.Should().Contain("persona");
        customerName.ExtractionGuidance.Should()
            .Contain("exclusivamente el nombre de la persona")
            .And.Contain("Nunca guardes aqui el nombre de un consultorio");

        var companyName = config.FactSchema.Single(fact => fact.Key == "company_name");
        companyName.Role.Should().Be("customer.company");
        companyName.Required.Should().BeFalse();
        companyName.Label.Should().Contain("establecimiento");
        companyName.ExtractionGuidance.Should()
            .Contain("exclusivamente el nombre del consultorio")
            .And.Contain("Nunca lo conviertas en customer_name");
    }

    [Fact]
    public void Seed_ProvidesARealLocalCatalogIncludingNumberedVariants()
    {
        var (_, sql) = LoadSeed();

        sql.Should().NotContain("Mantis");
        sql.Should().NotContain("CF127", "CJ recommendation identifiers do not belong to Medidental");
        sql.Should().Contain("MERGE dbo.Products");
        sql.Should()
            .Contain("Motor de implantes 3G Osseo 100")
            .And.Contain("Motor de implantes 3G Osseo 200")
            .And.Contain("Lampara de fotocurado 3G PowerLED L9")
            .And.Contain("Pieza de mano 3G Titanium 45P");
    }

    [Fact]
    public void Seed_CoversEveryPdfProductAndBuildsSearchAndAliasTraining()
    {
        var (_, sql) = LoadSeed();

        var productSkus = Regex.Matches(
                sql,
                @"\('D3E4A700-0000-0000-0000-000000000\d{3}',\s*N'(?<sku>MD-[^']+)'",
                RegexOptions.IgnoreCase)
            .Select(match => match.Groups["sku"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        productSkus.Should().HaveCount(51);
        productSkus.Should().Contain(
        [
            "MD-CHRO-MA",
            "MD-KIT-SYGD-MA",
            "MD-KIT-BISG-MA",
            "MD-KIT-CHRO-MA",
            "MD-KIT-TEGD-MA",
            "MD-KIT-AVVA-BULK",
            "MD-KIT-DV-SEAL",
            "MD-COMPULA-SYGD-MA",
            "MD-COMPULA-TEGD-MA",
            "MD-COMPULA-BISG-MA",
            "MD-COMPULA-CHRO-MA",
            "MD-COMPULA-AVVA-BULK",
            "MD-AUTOCURADO-3-15",
            "MD-CERAMIK-TRADICIONAL",
            "MD-CERAMIK-MINI",
            "MD-TITANIUM-3P",
            "MD-TITANIUM-3R",
            "MD-TITANIUM-45R",
            "MD-MICROMOTOR-SET-RECTA",
            "MD-CABEZA-CONTRAANGULO-PB",
            "MD-CABEZA-CONTRAANGULO-PESTILLO",
            "MD-CONTRAANGULO-PB",
            "MD-CONTRAANGULO-PESTILLO",
            "MD-SCALER-BLACK",
            "MD-SCALER-AS6000",
            "MD-SCALER-P5-MAX",
            "MD-CAVITRON-MAGPOWER",
            "MD-POWERLED-L7",
            "MD-POWERLED-LX"
        ]);

        sql.Should()
            .Contain("DELETE FROM dbo.ProductSearchTerms")
            .And.Contain("INSERT INTO dbo.ProductSearchTerms")
            .And.Contain("producto activo sin ProductSearchTerms")
            .And.Contain("MERGE dbo.ProductAliases")
            .And.Contain("alias global AutoResolve ambiguo")
            .And.Contain("(N'MD-ETCHANT-GEL-37', N'desmineralizante', 0, 1)");

        sql.Should()
            .Contain("(N'MD-OSSEO-100', N'motor implante implantologia osseo cien xcub torque')")
            .And.Contain("(N'MD-OSSEO-200', N'motor implante implantologia osseo doscientos bldc torque')")
            .And.NotContain("(N'MD-OSSEO-100', N'motor implante', 1, 0)")
            .And.NotContain("(N'MD-OSSEO-200', N'motor implante', 1, 0)",
                "a family expression shared by several SKUs belongs in catalog search terms, not repeated per-product aliases");

        sql.Should()
            .Contain("DECLARE @AliasNormalization TABLE")
            .And.Contain("COALESCE(n.NormalizedAlias, d.Alias)")
            .And.Contain("(N'escaler as6000', N'escaler as 6000')")
            .And.NotContain("(N'titanium 45p', N'titanium 45')")
            .And.NotContain("(N'titanium 45r', N'titanium 45')",
                "normalization overrides must not survive after their alias definitions are removed");
    }

    [Fact]
    public void ConsoleRegression_MirrorsEveryCjSuiteWithMedidentalSeed()
    {
        var evaluationDirectory = Path.Combine(
            FindSolutionRoot(),
            "src",
            "Console",
            "MimosBabySpa.Console",
            "ExtractorEvaluations");
        var cjSuites = Directory.GetFiles(evaluationDirectory, "cj-*.json")
            .OrderBy(Path.GetFileName)
            .ToArray();
        var medidentalSuites = Directory.GetFiles(evaluationDirectory, "medidental-*.json")
            .OrderBy(Path.GetFileName)
            .ToArray();

        medidentalSuites.Should().HaveCount(cjSuites.Length);
        cjSuites.Should().NotBeEmpty("the CJ extractor regression corpus must be present");

        for (var index = 0; index < cjSuites.Length; index++)
        {
            using var cj = JsonDocument.Parse(File.ReadAllText(cjSuites[index]));
            using var medidental = JsonDocument.Parse(File.ReadAllText(medidentalSuites[index]));
            var cjRoot = cj.RootElement;
            var medidentalRoot = medidental.RootElement;

            Path.GetFileName(medidentalSuites[index]).Should()
                .Be(Path.GetFileName(cjSuites[index]).Replace("cj-", "medidental-", StringComparison.Ordinal));
            medidentalRoot.GetProperty("repetitions").GetInt32().Should()
                .Be(cjRoot.GetProperty("repetitions").GetInt32());

            medidentalRoot.GetProperty("cases").GetArrayLength().Should()
                .BeGreaterThanOrEqualTo(cjRoot.GetProperty("cases").GetArrayLength(),
                    "Medidental may add business-specific regressions to a mirrored suite");

            foreach (var testCase in medidentalRoot.GetProperty("cases").EnumerateArray())
            {
                testCase.GetProperty("seed").GetString().Should().EndWith("SeedMedidental.sql");
                testCase.GetProperty("id").GetString().Should().StartWith("medidental_");
            }
        }

        File.Exists(Path.Combine(evaluationDirectory, "Run-MedidentalRegression.ps1"))
            .Should().BeTrue();
    }

    [Fact]
    public void Seed_ProvisionsEssentialSubscriptionWithoutOverridingBillingHistory()
    {
        var (_, sql) = LoadSeed();

        sql.Should()
            .Contain("FROM dbo.SubscriptionPlans")
            .And.Contain("Code = N'essential'")
            .And.Contain("INSERT INTO dbo.BusinessSubscriptions")
            .And.Contain("CurrentPeriodStart")
            .And.Contain("PlanCodeSnapshot")
            .And.Contain("AutoRenew");

        Regex.IsMatch(
                sql,
                @"IF NOT EXISTS\s*\(\s*SELECT 1\s*FROM dbo\.BusinessSubscriptions\s*WHERE BusinessId = @BusinessId\s*\)",
                RegexOptions.IgnoreCase)
            .Should().BeTrue(
                "rerunning the business seed must not overwrite upgrades, cancellations, or billing history");
        sql.Should().Contain(
            "plan essential activo no encontrado; no se puede completar el aprovisionamiento",
            "a business without a usable plan must fail provisioning visibly");
    }

    private static (AgentConfig Config, string Sql) LoadSeed()
    {
        var path = Path.Combine(
            FindSolutionRoot(),
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedMedidental.sql");
        var sql = File.ReadAllText(path);
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the Medidental seed must declare @SettingsJson");

        var json = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        var config = JsonSerializer.Deserialize<AgentConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
        });

        config.Should().NotBeNull();
        return (config!, sql);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MimosBabySpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate MimosBabySpa.sln.");
    }
}
