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
    [InlineData("SeedLuisPetitBarber.sql")]
    [InlineData("SeedAgenticConfiguration.sql")]
    [InlineData("SeedCJDistribuciones.sql")]
    public void MigratedSeed_CompilesBeforeActivation(string seedFile)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(root, "database", "MimosBabySpa.Database", "Scripts", "Seeds", seedFile);
        var settingsJson = ExtractSettingsJson(File.ReadAllText(path));
        var config = JsonSerializer.Deserialize<AgentConfig>(settingsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        });
        config.Should().NotBeNull();

        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
            [new AvailabilityStub(), new CheckoutStub(), new CreationStub(), new OrderChangesStub(), new CatalogServicesStub(), new ResolveServiceStub(), new AddOnsStub(), new FulfillmentStub(), new MethodStub("reservation.list", "reservation.listed"), new MethodStub("reservation.manage", "reservation.managed"), new MethodStub("commerce.search_recipes", "recipes.found"), new MethodStub("commerce.search_products", "products.found"), new MethodStub("commerce.get_order_draft", "order.draft_loaded"), new MethodStub("commerce.prepare_checkout", "order.checkout_prepared", "order.checkout_ready", "order.checkout_payment_required", "order.checkout_pending_manual_payment"), new MethodStub("commerce.create_order", "order.created")]));

        var compilation = compiler.Compile(config!);

        compilation.IsValid.Should().BeTrue(
            string.Join("; ", compilation.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Path}:{diagnostic.Code}:{diagnostic.Message}")));
    }

    private static string ExtractSettingsJson(string sql)
    {
        var match = Regex.Match(
            sql,
            "DECLARE\\s+@SettingsJson\\s+NVARCHAR\\(MAX\\)\\s*=\\s*N'(.*?)';",
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
                "cart.conflicting_commands",
                "cart.multiple_orders",
                "cart.product_not_found",
                "cart.product_ambiguous",
                "cart.item_not_found_or_ambiguous",
                "cart.invalid_input"
            ],
            [],
            [],
            []);

        public Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
