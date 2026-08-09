using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.LLM;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class DigitalShopOperationsConfigurationTests
{
    [Fact]
    public void OperationsAgent_AcceptsTextPdfAndImagePriceLists()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindSolutionRoot(), "database", "MimosBabySpa.Database", "Scripts", "Seeds",
            "SeedDigitalShop.sql"));
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@OperationsSettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();

        var config = JsonSerializer.Deserialize<AgentConfig>(
            match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            });
        config.Should().NotBeNull();

        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
            [new OperationStub()]));
        var compilation = compiler.Compile(config!);

        compilation.IsValid.Should().BeTrue(string.Join(
            "; ",
            compilation.Diagnostics.Select(value => $"{value.Path}:{value.Code}:{value.Message}")));
        config!.FactSchema.Should().ContainSingle(value => value.Key == "price_list_text");
        sql.Should().Contain("texto, PDF o imagen");
        sql.Should().Contain("N'Operaciones Digital Shop'");
    }

    [Fact]
    public void CustomerAgent_UsesStructuredGreetingAndAutomaticSalesActions()
    {
        var sql = File.ReadAllText(Path.Combine(
            FindSolutionRoot(), "database", "MimosBabySpa.Database", "Scripts", "Seeds",
            "SeedDigitalShop.sql"));
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue();

        var config = JsonSerializer.Deserialize<AgentConfig>(
            match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            });
        config.Should().NotBeNull();

        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
            [new SearchOffersOperationStub(), new SearchProductsOperationStub(), new CompleteRequestOperationStub()]));
        var compilation = compiler.Compile(config!);

        compilation.IsValid.Should().BeTrue(string.Join(
            "; ",
            compilation.Diagnostics.Select(value => $"{value.Path}:{value.Code}:{value.Message}")));
        config!.ConversationOpening.Enabled.Should().BeTrue();
        config.ConversationOpening.AllowQuestions.Should().BeFalse();
        config.ConversationOpening.Guidance.Should().Contain("Soy Catalina, un gusto saludarte.")
            .And.Contain("unico contenido de la apertura")
            .And.Contain("continuacion pertenece exclusivamente a la etapa");
        config.ExtractorHistoryWindowSize.Should().Be(8);
        config.Policies.Should().Contain("Los unicos telefonos vendidos por Digital Shop son iPhone")
            .And.Contain("resultados autoritativos del catalogo del turno")
            .And.Contain("garantia es directamente con la marca")
            .And.Contain("bateria es superior al 90%");
        config.Policies.Should().NotContain("Si modelo y condicion vienen juntos")
            .And.NotContain("Cra. 12 #16B-06")
            .And.NotContain("una sola comparacion automatica");
        config.FactSchema.Should().ContainSingle(value => value.Key == "storage_gb");
        config.FactSchema.Single(value => value.Key == "device_model").ExtractionGuidance
            .Should().Contain("nunca la reduzcas a 'Pro'");
        config.Templates.Keys.Should().Contain([
            "new_product_offers", "used_product_offers",
            "accessory_product_offers", "product_color_options", "iphone_model_catalog",
            "compared_current_new_offer", "compared_previous_new_offer",
            "compared_current_used_offer", "compared_previous_used_offer",
            "phone_accessory_recommendation", "technical_service_local", "store_location",
            "switched_new_offer", "switched_used_offer",
            "additional_new_offer", "additional_used_offer"]);
        config.Flows.Single().Stages.Single(value => value.Id == "discover")
            .ConversationGuidance.Should().Contain("Caso 1:")
            .And.Contain("A. Nuevo")
            .And.Contain("B. Usado")
            .And.Contain("Caso 4:");
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "store_location" && value.Actions.Count == 0 && value.Priority == 125);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "technical_service" && value.Actions.Count == 0);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "iphone_catalog_sales" && value.Actions.Count == 1);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "accessory_sales" && value.Actions.Count == 1);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "switch_used_to_new" && value.Actions.Count == 1);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "switch_new_to_used" && value.Actions.Count == 1);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "compare_new_and_used" && value.Actions.Count == 2);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "compare_different_iphone_models" && value.Actions.Count == 6);
        config.FactSchema.Should().ContainSingle(value =>
            value.Key == "automatic_model_comparison_used");
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "show_product_colors" && value.Actions.Count == 2);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "show_product_details" && value.Actions.Count == 2);
        config.Flows.Single().Stages.Single(value => value.Id == "visit")
            .Collect.Should().Contain(["device_model", "product_condition"]);
        config.Flows.Single().Stages.Single(value => value.Id == "quote")
            .Actions.Should().HaveCount(3);
        config.Flows.Single().Stages.Single(value => value.Id == "quote")
            .Actions.Should().ContainSingle(value => value.Id == "recommend_charger_after_phone");
        sql.Should().Contain("ACC-CUBO-20W");
        sql.Should().Contain("ACC-CABLE-TC-LIGHTNING");
        sql.Should().Contain("ProductRecommendationRules");
        sql.Should().Contain("{{description}}")
            .And.Contain("store_location_requested")
            .And.Contain("automatic_model_comparison_used")
            .And.NotContain("Mi recomendacion");
        config.GlobalActions.Single(value => value.Id == "compare_different_iphone_models").Response.Guidance
            .Should().Contain("no reconstruyas ni repitas ninguno")
            .And.Contain("Nunca cierres con si quieres");
        config.GlobalActions.Single(value => value.Id == "compare_different_iphone_models").Actions
            .Select(value => value.Id).Should().Contain(["show_followup_new_model", "show_followup_used_model"]);
        config.GlobalActions.Should().ContainSingle(value =>
            value.Id == "purchase_interest_store_visit" && value.Actions.Count == 0 && value.Priority == 124);
        config.GlobalActions.Should().NotContain(value => value.Id == "show_additional_iphone_model");
        config.Persona.Should().NotContain("esta opcion esta genial")
            .And.NotContain("Personalmente")
            .And.NotContain("me encanta esta alternativa");
        config.Templates["new_product_offers"].Should().NotContain("Cra. 12")
            .And.NotContain("Personalmente");
        config.Templates["used_product_offers"].Should().NotContain("Cra. 12")
            .And.NotContain("Personalmente");
        config.Templates["store_location"].Should().Contain("Cra. 12 #16B-06");
        sql.Should().Contain("products/catalog/iphone-15-pro-max.png")
            .And.Contain("products/catalog/iphone-17-pro-max.png")
            .And.Contain("AND target.IsPrimary = 1");
        var catalogImagePaths = Regex.Matches(sql, @"products/catalog/iphone-[a-z0-9-]+\.png")
            .Select(match => match.Value)
            .ToArray();
        catalogImagePaths.Should().HaveCount(29).And.OnlyHaveUniqueItems();
        sql.Should().NotContain("ðŸ").And.NotContain("Â·");
    }

    [Fact]
    public void ProductConditionOptions_ResolveDirectUsedReplyAfterStructuredPresentation()
    {
        var condition = new FactSchemaEntry
        {
            Key = "product_condition",
            Label = "condicion",
            Type = "string",
            Source = "user",
            Options =
            [
                new FactValueOption { Value = "new", Label = "Nuevo", Selector = "A" },
                new FactValueOption { Value = "used", Label = "Usado", Selector = "B" }
            ]
        };
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [condition.Key] = condition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));
        var ambiguousPlan = new TurnPlan
        {
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification",
                AmbiguousFields = ["product_condition"]
            }
        };

        var resolved = OptionSelectorResolver.Resolve(
            ambiguousPlan,
            scope,
            "B",
            [ChatMessage.Assistant(
                "¿Lo quieres nuevo o usado?\n" +
                "A. Nuevo: equipo nuevo con garantia directamente con la marca.\n" +
                "B. Usado: bateria superior al 90%; el valor exacto se verifica en tienda.")],
            out _);

        resolved.Response.Mode.Should().Be("continue");
        resolved.Response.AmbiguousFields.Should().BeEmpty();
        resolved.Facts.Should().ContainSingle(value =>
            value.Key == "product_condition"
            && value.Value.GetString() == "used");
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

    private sealed class OperationStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "internal.update_product_offer_prices",
            """{"type":"object"}""",
            ["prices.updated", "prices.no_changes", "prices.review_required"],
            ["catalog.prices"],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok("prices.updated", new { }));
    }

    private sealed class SearchOffersOperationStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "commerce.search_product_offers",
            """{"type":"object"}""",
            ["offers.found", "offers.not_found"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok("offers.found", new { }));
    }

    private sealed class SearchProductsOperationStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "commerce.search_products",
            """{"type":"object"}""",
            ["categories.found", "categories.not_found", "products.found", "products.not_found", "catalog.no_more", "catalog.not_ready", "products.search_failed"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok("products.found", new { }));
    }
    private sealed class CompleteRequestOperationStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "conversation.complete_request",
            """{"type":"object"}""",
            ["request.completed", "request.confirmation_required"],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(
            JsonElement input,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Ok("request.completed", new { }));
    }
}
