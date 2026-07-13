using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class MigratedSeedConfigurationTests
{
    [Theory]
    [InlineData("SeedLuisPetitBarber.sql", "SettingsJson")]
    [InlineData("SeedAgenticConfiguration.sql", "SettingsJson")]
    [InlineData("SeedCJDistribuciones.sql", "SettingsJson")]
    [InlineData("SeedAuraly.sql", "SettingsJson")]
    [InlineData("SeedRadaConcept.sql", "SettingsJson")]
    [InlineData("SeedSolorzanoAgentConfiguration.sql", "SettingsJson")]
    [InlineData("SeedSolorzanoDomicilioAgent.sql", "SolorzanoDeliverySettingsJson")]
    [InlineData("SeedSystemAgentTemplatesAndInboundContacts.sql", "DeliverySettingsJson")]
    [InlineData("SeedSystemAgentTemplatesAndInboundContacts.sql", "OperationsSettingsJson")]
    public void MigratedSeed_CompilesBeforeActivation(string seedFile, string variableName)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(root, "database", "MimosBabySpa.Database", "Scripts", "Seeds", seedFile);
        var settingsJson = ExtractSettingsJson(File.ReadAllText(path), variableName);
        var config = JsonSerializer.Deserialize<AgentConfig>(settingsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
        });
        config.Should().NotBeNull();
        config!.Policies.Should().StartWith("## EXPERIENCIA CONVERSACIONAL");
        AssertSemanticAndPresentationTextIsSeparated(config);

        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
            [new AvailabilityStub(), new CheckoutStub(), new CreationStub(), new OrderChangesStub(), new CatalogServicesStub(), new ResolveServiceStub(), new AddOnsStub(), new FulfillmentStub(), new MethodStub("reservation.list", "reservation.listed"), new MethodStub("reservation.manage", "reservation.managed"), new MethodStub("commerce.search_recipes", "recipes.found"), new MethodStub("commerce.search_products", "products.found", "products.not_found"), new MethodStub("commerce.get_order_draft", "order.draft_loaded", "order.draft_empty", "order_draft_missing"), new MethodStub("commerce.prepare_checkout", "order.checkout_prepared", "order.checkout_ready", "order.checkout_payment_required", "order.checkout_pending_manual_payment", "order_draft_missing", "missing_prerequisites"), new MethodStub("commerce.create_order", "order.created"), new MethodStub("escalation.request_human", "escalation.requested", "escalation.notification_failed"), new MethodStub("conversation.reset_request", "conversation.request_reset"), new MethodStub("internal.get_reservations", "internal.reservations_loaded"), new MethodStub("internal.block_availability", "internal.availability_blocked"), new MethodStub("internal.request_reschedule", "internal.reschedule_requested"), new MethodStub("internal.get_business_metrics", "internal.metrics_loaded"), new MethodStub("internal.get_customer_history", "internal.customer_history_loaded"), new MethodStub("internal.search_order", "internal.order_loaded"), new MethodStub("internal.accept_order", "internal.order_accepted"), new MethodStub("internal.reject_order", "internal.order_rejected")]));

        var compilation = compiler.Compile(config!);

        compilation.IsValid.Should().BeTrue(
            string.Join("; ", compilation.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Path}:{diagnostic.Code}:{diagnostic.Message}")));
    }

    [Fact]
    public void CjCustomerFacingTemplates_AreConversationalAndReadableOnWhatsApp()
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(
            root,
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedCJDistribuciones.sql");
        var settingsJson = ExtractSettingsJson(File.ReadAllText(path), "SettingsJson");
        var config = JsonSerializer.Deserialize<AgentConfig>(
            settingsJson,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!;

        config.ConversationOpening.Enabled.Should().BeTrue();
        config.ConversationOpening.Guidance.Should().Contain("bienvenida a CJ Distribuciones");
        config.ConversationOpening.AllowQuestions.Should().BeFalse();
        config.FailureResponses.LlmUnavailable.Should().Contain("inconveniente temporal");
        config.BasePrompt.Should().Contain("cercana, empatica, natural y servicial");
        config.BasePrompt.Should().Contain("parrafos cortos y espacios en blanco");

        var customerName = config.Templates["customer_name_prompt"];
        customerName.Should().Contain("\r\n\r\n");
        customerName.Should().Contain("Que gusto saludarte");
        customerName.Should().NotContain("Bienvenido a CJ Distribuciones. Con gusto");

        var customerType = config.Templates["customer_type_prompt"];
        customerType.Should().Contain("\r\n\r\n");
        customerType.Should().Contain("Puedes responder con la letra o con el nombre");
        customerType.Should().Contain("*A.* Hogar");

        config.Templates["product_selection_prompt"].Should().Contain("\r\n\r\n");
        config.Templates["catalog_results"].Should().Contain("\r\n\r\n*Productos disponibles*\r\n\r\n");
        config.Templates["cart_snapshot"].Should().Contain("\r\n\r\n*Pedido actual*\r\n\r\n");
        config.Templates["cart_review"].Should().Contain("\r\n\r\n*Resumen de tu pedido*\r\n\r\n");
    }
    private static void AssertSemanticAndPresentationTextIsSeparated(AgentConfig config)
    {
        const string internalVocabulary = @"\b(tool|tools|herramienta|herramientas|prepare_checkout|create_reservation)\b";
        Regex.IsMatch(config.Policies, internalVocabulary, RegexOptions.IgnoreCase)
            .Should().BeFalse("policies describe brand and presentation, not engine operations");

        var policyStatements = config.Policies
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '#', ' '))
            .Where(line => line.Length >= 40)
            .ToList();

        foreach (var stage in config.Flows.SelectMany(flow => flow.Stages))
        {
            Regex.IsMatch(stage.ConversationGuidance ?? string.Empty, internalVocabulary, RegexOptions.IgnoreCase)
                .Should().BeFalse($"stage '{stage.Id}' guidance describes customer communication, while actions configure operations");
            foreach (var statement in policyStatements)
            {
                (stage.ConversationGuidance ?? string.Empty).Contains(statement, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"stage '{stage.Id}' must not duplicate policy text");
            }
        }
    }
    private static string ExtractSettingsJson(string sql, string variableName)
    {
        var match = Regex.Match(
            sql,
            $"DECLARE\\s+@{Regex.Escape(variableName)}\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the seed must declare @SettingsJson");
        return match.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
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
        public MethodStub(string id, params string[] outcomes) => Descriptor = new OperationDescriptor(
            id,
            "{\"type\":\"object\",\"required\":[]}",
            outcomes, [], [], []);
        public OperationDescriptor Descriptor { get; }
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class CatalogServicesStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "catalog.get_services",
            "{\"type\":\"object\",\"required\":[\"view\"]}",
            ["catalog.services_returned"], [], [], []);
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ResolveServiceStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "catalog.resolve_service",
            "{\"type\":\"object\",\"required\":[\"text\"]}",
            ["catalog.service_resolved", "catalog.service_unchanged", "catalog.add_on_detected", "catalog.service_ambiguous", "catalog.service_not_found", "input.invalid"],
            [], [], []);
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class AddOnsStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "catalog.get_compatible_add_ons",
            "{\"type\":\"object\",\"required\":[\"service\"]}",
            ["catalog.add_ons_available", "catalog.no_add_ons", "input.invalid"],
            [], [], []);
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class FulfillmentStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "catalog.get_service_fulfillment",
            "{\"type\":\"object\",\"required\":[\"service\"]}",
            ["catalog.fulfillment_reservation", "catalog.fulfillment_enrollment", "catalog.fulfillment_missing_schedule", "catalog.service_not_found", "input.invalid"],
            [], [], []);
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class CheckoutStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "reservation.prepare_checkout",
            "{\"type\":\"object\",\"required\":[\"service\"]}",
            ["checkout.prepared"],
            ["checkout.prepare"],
            [],
            []);
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class CreationStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "reservation.create",
            "{\"type\":\"object\",\"required\":[\"service\",\"date\",\"time\",\"customer_name\",\"customer_phone\",\"customer_confirmed\"]}",
            ["reservation.created", "reservation.idempotent_replay"],
            ["reservation.create"],
            [],
            []);
        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class AvailabilityStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "reservation.check_availability",
            "{\"type\":\"object\",\"required\":[\"service\",\"date\"]}",
            [
                "availability.exact_time_available",
                "availability.options_available",
                "availability.requested_time_unavailable",
                "availability.none",
                "input.invalid",
                "input.invalid_date",
                "input.past_date",
                "input.invalid_time",
                "catalog.service_unresolved"
            ],
            [],
            ["availability_slots"],
            []);

        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class OrderChangesStub : IAgentOperation
    {
        public OperationDescriptor Descriptor { get; } = new(
            "commerce.apply_order_changes",
            "{\"type\":\"object\",\"required\":[\"commands\"]}",
            [
                "cart.applied",
                "cart.no_changes",
                "cart.pending_cancelled",
                "cart.conflicting_commands",
                "cart.multiple_destinations",
                "cart.product_not_found",
                "cart.product_ambiguous",
                "cart.item_not_found_or_ambiguous",
                "cart.insufficient_stock",
                "cart.invalid_input"
            ],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
