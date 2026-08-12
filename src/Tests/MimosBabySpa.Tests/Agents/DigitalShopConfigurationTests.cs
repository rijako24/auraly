using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DigitalShopConfigurationTests
{
    [Fact]
    public void Seed_DeserializesAndCompilesWithRegisteredOperations()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "database",
            "Auraly.Database",
            "Scripts",
            "Seeds",
            "SeedDigitalShop.sql"));
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();
        var json = match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        var config = JsonSerializer.Deserialize<AgentConfig>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            });
        config.Should().NotBeNull();

        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
        [
            new MethodStub("commerce.search_product_offers", "offers.found", "offers.not_found"),
            new MethodStub("commerce.search_products", "categories.found", "categories.not_found", "products.found", "products.not_found", "catalog.no_more", "catalog.not_ready", "products.search_failed"),
            new MethodStub("conversation.complete_request", "request.completed", "request.confirmation_required")
        ]));
        var compilation = compiler.Compile(config!);

        compilation.IsValid.Should().BeTrue(string.Join(
            "; ",
            compilation.Diagnostics.Select(value => $"{value.Path}:{value.Code}:{value.Message}")));
        config!.Flows.Should().ContainSingle(value => value.Type == FlowTypes.Primary);
        config.FactSchema.Should().Contain(value =>
            value.Key == "product_condition" && value.Options.Count == 2);
        var productCondition = config.FactSchema.Single(value => value.Key == "product_condition");
        productCondition.DependsOn.Should().ContainSingle().Which.Should().Be("device_model");
        productCondition.ExtractionGuidance.Should().Contain("iPhone 14 Pro Max usado");
        productCondition.ExtractionGuidance.Should().Contain("no infieras ni conserves la condicion anterior");
        config.Templates.Should().ContainKey("new_product_offers");
        config.Templates.Should().ContainKey("used_product_offers");
        config.Templates["new_product_offers"].Should().Contain("{{product_name}}");
        config.Templates["new_product_offers"].Should().Contain("{{description}}");
        config.Templates["used_product_offers"].Should().Contain("{{product_name}}");
        config.Templates["used_product_offers"].Should().Contain("{{description}}");
        var offerPresented = config.FactSchema.Single(value => value.Key == "offer_presented");
        offerPresented.DependsOn.Should().BeEmpty();
        var salesFlow = config.Flows.Single(value => value.Id == "iphone_sales");
        var discover = salesFlow.Stages.Single(value => value.Id == "discover");
        discover.ReentryOnFactChanged.Should().ContainSingle().Which.Should().Be("device_model");
        var quote = salesFlow.Stages.Single(value => value.Id == "quote");
        quote.ReentryOnFactChanged.Should().BeEquivalentTo("device_model", "product_condition");
        var visit = salesFlow.Stages.Single(value => value.Id == "visit");
        visit.Actions.Should().ContainSingle(value => value.Id == "complete_store_sale_lead");
        sql.Should().Contain("N'iPhone 17e'");
        sql.Should().Contain("N'iPhone Air'");
        sql.Should().Contain("MinimumBatteryHealthPercent, SourceUrl");
        sql.Should().Contain("TechnicalDescription NVARCHAR(1000)");
        sql.Should().Contain("two_iphone_models_comparison_requested");
        sql.Should().Contain("Modelo contra modelo y nunca nuevo contra usado");
        sql.Should().NotContain("• Chip:");
        sql.Should().Contain("ProMotion de hasta 120 Hz");
        sql.Should().Contain("Resuelve solo uno de estos casos por turno");
        sql.Should().Contain("A. Nuevo, en una linea propia");
        sql.Should().Contain("B. Usado, en una linea propia");
        sql.Should().Contain("Termina inmediatamente despues de la explicacion de usado");
        sql.Should().Contain("Conserva visibles los selectores");
        sql.Should().Contain("No elogies el modelo como excelente eleccion");
        sql.Should().Contain("condition_comparison_new");
        sql.Should().NotContain("refresh_new_offer_after_fact_change");
        sql.Should().Contain("yo te recomendaria");
        sql.Should().Contain("Acá te dejo toda la información del equipo");
        sql.Should().Contain("Y estas son sus características principales");
        sql.Should().Contain("Quedo atenta 😊 Cuando quieras verlo en persona");
        sql.Should().NotContain("Opciones usadas disponibles");
        sql.Should().Contain("Si cambia de modelo sin decir la condicion, no emitas esta senal");
        sql.Should().Contain("no afirmes que el modelo esta agotado o no disponible");
        sql.Should().NotContain("A. Nuevo, que estrena");
        sql.Should().NotContain("B. Usado, que permite ahorrar");
        sql.Should().NotContain("responde en este formato exacto y sin texto adicional: ¿Lo quieres nuevo o usado?");
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

    private sealed class MethodStub : IAgentOperation
    {
        public MethodStub(string id, params string[] outcomes) =>
            Descriptor = new OperationDescriptor(id, """{"type":"object"}""", outcomes, [], [], []);

        public OperationDescriptor Descriptor { get; }

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok(Descriptor.OutcomeCodes[0], new { }));
    }
}
