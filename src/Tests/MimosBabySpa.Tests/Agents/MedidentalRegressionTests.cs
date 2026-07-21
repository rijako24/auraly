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
    public void Seed_UsesLocalCommerceAndAllowsOpenCatalogQueries()
    {
        var (config, _) = LoadSeed();

        config.Commerce.Enabled.Should().BeTrue();
        config.Commerce.Provider.Should().Be(CommerceProvider.Local);

        var catalog = config.GlobalActions.Single(action => action.Id == "catalog_lookup");
        catalog.Signal.Type.Should().Be("catalog_query");
        catalog.ConversationGuidance.Should()
            .Contain("queries como una lista vacia")
            .And.Contain("No uses palabras genericas");

        var queriesSchema = catalog.Signal.ValueSchema
            .GetProperty("properties")
            .GetProperty("queries");
        queriesSchema.GetProperty("minItems").GetInt32().Should().Be(0);

        var search = catalog.Actions.Single(action => action.Operation == "commerce.search_products");
        search.OnOutcome["products.found"].Effects.Should().ContainSingle(effect =>
            effect.Type == "presentation.add"
            && effect.Template == "catalog_results");
    }

    [Fact]
    public void OpenCatalogTemplate_DescribesVarietyListsExamplesAndAsksForInterest()
    {
        var (config, _) = LoadSeed();
        var products = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "Motor de implantes 3G Osseo 200",
                ["unit_price"] = 0m,
                ["currency"] = "COP"
            },
            new Dictionary<string, object?>
            {
                ["name"] = "Lampara de fotocurado 3G PowerLED L9",
                ["unit_price"] = 0m,
                ["currency"] = "COP"
            },
            new Dictionary<string, object?>
            {
                ["name"] = "Pieza de mano 3G Titanium 45P",
                ["unit_price"] = 0m,
                ["currency"] = "COP"
            }
        };

        var rendered = new PromptTemplateRenderer().Render(
            config.Templates["catalog_results"],
            new Dictionary<string, object?>
            {
                ["search_text"] = string.Empty,
                ["products"] = products,
                ["recommendations"] = Array.Empty<object>()
            });

        rendered.Should()
            .Contain("gran variedad de productos odontologicos")
            .And.Contain("Motor de implantes 3G Osseo 200")
            .And.Contain("Lampara de fotocurado 3G PowerLED L9")
            .And.Contain("Pieza de mano 3G Titanium 45P")
            .And.Contain("Por cual producto estas interesado?");
        rendered.Should().NotContain("$").And.NotContain("0.00");
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
