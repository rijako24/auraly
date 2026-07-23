using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Commerce;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class MigratedSeedConfigurationTests
{
    [Theory]
    [InlineData("SeedLuisPetitBarber.sql", "SettingsJson")]
    [InlineData("SeedAgenticConfiguration.sql", "SettingsJson")]
    [InlineData("SeedCJDistribuciones.sql", "SettingsJson")]
    [InlineData("SeedAndinaSantander.sql", "SettingsJson")]
    [InlineData("SeedAuraly.sql", "SettingsJson")]
    [InlineData("SeedInmobiliariaDemo.sql", "SettingsJson")]
    [InlineData("SeedMedidental.sql", "SettingsJson")]
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
        if (seedFile is "SeedCJDistribuciones.sql" or "SeedMedidental.sql")
        {
            config!.Flows.SelectMany(flow => flow.Stages).Should().NotContain(stage => stage.Id == "cart_review");
            config.FactSchema.Should().NotContain(fact => fact.Key == "cart_review_confirmed");
            config.Templates.Should().NotContainKey("cart_review");
        }        config!.Policies.Should().StartWith("## EXPERIENCIA CONVERSACIONAL");
        AssertSemanticAndPresentationTextIsSeparated(config);

        if (config.Commerce.Enabled)
        {
            var finalizationFact = config.FactSchema.Should().ContainSingle(fact =>
                fact.Role == "order.finalized").Subject;
            config.Flows.SelectMany(flow => flow.Stages).Should().Contain(stage =>
                stage.Collect.Contains(finalizationFact.Key, StringComparer.OrdinalIgnoreCase),
                "the semantic finalization fact must be writable during a configured commerce stage");
        }

        var compiler = new AgentConfigurationCompiler(new AgentOperationRegistry(
            [new AvailabilityStub(), new CheckoutStub(), new CreationStub(), new OrderChangesStub(), new CatalogServicesStub(), new ResolveServiceStub(), new AddOnsStub(), new FulfillmentStub(), new MethodStub("reservation.list", "reservation.listed"), new MethodStub("reservation.manage", "reservation.managed"), new MethodStub("commerce.search_recipes", "recipes.found"), new MethodStub("commerce.search_products", "products.found", "products.not_found"), new MethodStub("commerce.get_order_draft", "order.draft_loaded", "order.draft_empty", "order_draft_missing"), new MethodStub("commerce.prepare_checkout", "order.checkout_ready", "order.checkout_payment_required", "order.checkout_pending_manual_payment", "missing_prerequisites", "order_draft_missing", "product_inactive", "checkout_mode_missing", "invalid_order_total", "payment_link_failed", "manual_payment_failed"), new MethodStub("commerce.create_order", "order.created"), new MethodStub("escalation.request_human", "escalation.requested", "escalation.notification_failed"), new MethodStub("conversation.reset_request", "conversation.request_reset"), new MethodStub("conversation.get_known_facts", "known_facts.found", "known_facts.not_found", "known_facts.forbidden"), new MethodStub("internal.get_reservations", "internal.reservations_loaded"), new MethodStub("internal.block_availability", "internal.availability_blocked"), new MethodStub("internal.request_reschedule", "internal.reschedule_requested"), new MethodStub("internal.get_business_metrics", "internal.metrics_loaded"), new MethodStub("internal.get_customer_history", "internal.customer_history_loaded"), new MethodStub("internal.search_order", "internal.order_loaded"), new MethodStub("internal.accept_order", "internal.order_accepted"), new MethodStub("internal.reject_order", "internal.order_rejected")]));

        var compilation = compiler.Compile(config!);

        compilation.IsValid.Should().BeTrue(
            string.Join("; ", compilation.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Path}:{diagnostic.Code}:{diagnostic.Message}")));
    }

    [Theory]
    [InlineData("SeedAndinaSantander.sql")]
    [InlineData("SeedCJDistribuciones.sql")]
    [InlineData("SeedMedidental.sql")]
    public void CommerceCatalogSignals_DeclareExplicitBrowseAndSearchModes(string seedFile)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(
            root,
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            seedFile);
        var config = JsonSerializer.Deserialize<AgentConfig>(
            ExtractSettingsJson(File.ReadAllText(path), "SettingsJson"),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            })!;

        var catalog = config.GlobalActions.Should().ContainSingle(action =>
            action.Signal.Type == "catalog_query").Subject;
        var schema = catalog.Signal.ValueSchema;
        schema.GetProperty("properties").GetProperty("mode")
            .GetProperty("enum").EnumerateArray().Select(value => value.GetString())
            .Should().BeEquivalentTo("search", "browse");
        schema.GetProperty("properties").GetProperty("queries")
            .GetProperty("minItems").GetInt32().Should().Be(0);
        schema.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).Should().Contain("mode");
        catalog.ConversationGuidance.Should().Contain("mode=browse")
            .And.Contain("mode=search")
            .And.Contain("coexistir");
        var action = catalog.Actions.Should().ContainSingle(candidate =>
            candidate.Operation == "commerce.search_products").Subject;
        action.Arguments.Should().ContainKey("mode");
        action.Arguments["mode"].ValueKind.Should().Be(JsonValueKind.String);
        action.Arguments["mode"].GetString().Should().Be("{{signal.catalog_query.value.mode}}");
    }

    [Theory]
    [InlineData("SeedCJDistribuciones.sql")]
    [InlineData("SeedMedidental.sql")]
    [InlineData("SeedSolorzanoAgentConfiguration.sql")]
    public void CommerceOrderAgents_NotifyCustomerAndInternalContact(string seedFile)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(
            root,
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            seedFile);
        var config = JsonSerializer.Deserialize<AgentConfig>(
            ExtractSettingsJson(File.ReadAllText(path), "SettingsJson"),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            })!;

        config.MessageSequences.Should().ContainKey("order_created_customer");
        config.MessageSequences.Should().ContainKey("order_created");

        var notification = config.Notifications["order_created"];
        notification.Enabled.Should().BeTrue();
        notification.Deliveries.Should().HaveCount(2);

        var customer = notification.Deliveries.Single(delivery => delivery.Id == "customer");
        customer.Enabled.Should().BeTrue();
        customer.Recipients.Should().Equal("source:conversation");
        customer.SendMessageSequence.Should().Be("order_created_customer");

        var internalDelivery = notification.Deliveries.Single(delivery => delivery.Id == "internal");
        internalDelivery.Enabled.Should().BeTrue();
        internalDelivery.Recipients.Should().NotBeEmpty();
        internalDelivery.SendMessageSequence.Should().Be("order_created");

        config.Webhooks.Wompi.Should().NotContainKey("order_paid");
    }

    [Fact]
    public void AuralyDemoFlow_UsesDeterministicAvailabilityAndReservationGuards()
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(
            root,
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedAuraly.sql");
        var seedSql = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AgentConfig>(
            ExtractSettingsJson(seedSql, "SettingsJson"),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            })!;

        config.Temperature.Should().Be(0.2f);
        config.ExtractorHistoryWindowSize.Should().Be(2);
        config.ConversationOpening.Enabled.Should().BeTrue();
        config.ConversationOpening.Guidance.Replace("\r\n", "\n").Should()
            .Contain("\U0001F44B Hola, soy Aly de AURALY.\n\n\u00A1Un gusto saludarte!\n\n")
            .And.Contain("como podemos ayudarte")
            .And.Contain("acompanarte a agendar una demo en vivo");
        config.Persona.Should().Contain("el bot de AURALY").And.Contain("podemos ayudarte");
        config.Policies.Should().Contain("precios, costos, planes o tarifas")
            .And.Contain("en la demo se les dara toda la informacion comercial")
            .And.Contain("No inventes ni anticipes montos");
        config.ConversationOpening.AllowQuestions.Should().BeFalse();
        seedSql.Should().NotContain("SystemPromptMarkdown");
        seedSql.Should().Contain(
            "CurrentPeriodStart     = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)");
        seedSql.Should().Contain(
            "CurrentPeriodEnd       = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1))");

        var pricingInformation = config.GlobalActions.Single(action => action.Id == "pricing_information");
        pricingInformation.Signal.Type.Should().Be("pricing_question");
        pricingInformation.Actions.Should().BeEmpty();
        pricingInformation.Response.Template.Should().Be("pricing_demo_information");
        config.Templates["pricing_demo_information"].Should().Contain("En la demo").And.Contain("precios y planes");
        var confirmationReplay = config.GlobalActions.Single(action => action.Id == "booking_confirmation_replay");
        confirmationReplay.Signal.Type.Should().Be("booking_confirmation_replay");
        confirmationReplay.Actions.Should().BeEmpty();
        confirmationReplay.Response.Template.Should().Be("booking_already_confirmed");
        config.Templates["booking_already_confirmed"].Should().Contain("ya quedo agendada");
        var humanHandoff = config.GlobalActions.Single(action => action.Id == "human_handoff");
        humanHandoff.Actions.Single(action => action.Operation == "escalation.request_human")
            .OnOutcome["escalation.requested"].Response!.Template.Should().Be("human_handoff_ack");
        config.Templates["human_handoff_ack"].Should().Contain("equipo AURALY");

        var facts = config.FactSchema.ToDictionary(fact => fact.Key, StringComparer.OrdinalIgnoreCase);
        facts["service"].DefaultValue.Should().Be("Demo AURALY");
        facts["main_channel"].DefaultValue.Should().Be("WhatsApp");
        facts["availability_checked"].Type.Should().Be("boolean");
        facts["company_name"].ShowInCollectedInfo.Should().BeTrue();
        facts["business_type"].ShowInCollectedInfo.Should().BeTrue();
        facts["pain_point"].ShowInCollectedInfo.Should().BeTrue();
        facts["pain_point"].Label.Should().Be("proceso que quiere automatizar o mejorar");
        facts["pain_point"].ExtractionGuidance.Should()
            .Contain("descripcion breve pero completa")
            .And.Contain("No la reduzcas a una categoria cerrada");
        config.Templates["demo_confirmation"].Should().Contain("{{business_type}}").And.Contain("{{pain_point}}");
        config.Templates["value_explanation"].Should()
            .Contain("\u2728")
            .And.Contain("\U0001F4AC")
            .And.Contain("\U0001F4C5")
            .And.Contain("\U0001F514")
            .And.NotContain("{{business_type}}")
            .And.NotContain("{{pain_point}}")
            .And.NotContain("Entendi que");
        config.Templates["discovery_question"].Should()
            .Contain("{{#if business_type}}")
            .And.Contain("{{#if company_name}}")
            .And.Contain("\u00BFComo se llama tu empresa?")
            .And.Contain("\u00BFQue tipo de negocio tienes?")
            .And.Contain("\U0001F4AC")
            .And.NotContain("\U0001F44B Hola, soy Aly de AURALY.")
            .And.NotContain("Por ejemplo");
        var initialDiscovery = new PromptTemplateRenderer().Render(
            config.Templates["discovery_question"],
            new Dictionary<string, object?>()).Replace("\r\n", "\n");
        initialDiscovery.Should()
            .Contain("\u00BFComo se llama tu empresa?")
            .And.Contain("\u00BFQue tipo de negocio tienes?")
            .And.Contain("\u00BFQue proceso te gustaria automatizar o mejorar en WhatsApp?");
        initialDiscovery.Split('\n').Count(line => line.StartsWith("\u2022 ")).Should().Be(3);
        facts["business_profile_url"].Required.Should().BeFalse();
        facts["business_profile_url"].Label.Should().Be("Facebook e Instagram");
        facts["business_profile_url"].ExtractionGuidance.Should().Contain("uno o ambos perfiles");
        facts["social_profiles_answered"].Type.Should().Be("boolean");
        facts["social_profiles_answered"].Scope.Should().Be("request");
        config.Templates["customer_data_question"].Should()
            .Contain("\U0001F4CB").And.NotContain("Facebook").And.NotContain("Instagram");
        config.Templates["social_profiles_question"].Should()
            .Contain("\U0001F517")
            .And.Contain("Facebook")
            .And.Contain("Instagram")
            .And.Contain("Es opcional");
        config.Templates["demo_confirmation"].Should()
            .Contain("Facebook/Instagram: {{business_profile_url}}");
        var customerDataQuestion = new PromptTemplateRenderer().Render(
            config.Templates["customer_data_question"],
            new Dictionary<string, object?>());
        var normalizedCustomerDataQuestion = customerDataQuestion.Replace("\r\n", "\n");
        normalizedCustomerDataQuestion.Should().Contain("\u2022 Tu nombre\n\u2022 Tu correo");
        facts["business_profile_url"].ShowInCollectedInfo.Should().BeTrue();
        facts["customer_confirmed"].DependsOn.Should().Contain(
            ["service", "desired_date", "desired_time", "customer_name", "company_name", "customer_email"]);

        var stages = config.Flows.Single(flow => flow.Type == "primary")
            .Stages.ToDictionary(stage => stage.Id, StringComparer.OrdinalIgnoreCase);
        stages["discovery"].Response.Template.Should().Be("discovery_question");
        stages["discovery"].AdvanceWhenFacts.Should().Contain("company_name");
        stages["discovery"].ConversationGuidance.Should()
            .Contain("un solo mensaje estructurado")
            .And.Contain("nombre propio de la empresa")
            .And.Contain("que quiere automatizar o mejorar")
            .And.Contain("No ofrezcas ejemplos ni una lista cerrada");
        var valueExplanation = stages["value_explanation"];
        valueExplanation.ConversationGuidance.Should()
            .Contain("Antes de pedir o usar una fecha")
            .And.Contain("Explica el flujo que vivirian el cliente y el equipo");
        var catalog = valueExplanation.Actions.Single(action => action.Operation == "catalog.get_services");
        catalog.Trigger.Should().Be(StageActionTriggers.OnEnter);
        catalog.OnOutcome["catalog.services_returned"].Response!.Template.Should().Be("value_explanation");
        var scheduling = stages["scheduling"];
        scheduling.Collect.Should().Contain(["desired_date", "desired_time"]);
        scheduling.Collect.Should().NotContain("availability_checked");
        scheduling.AdvanceWhenFacts.Should().Equal("availability_checked");
        scheduling.Signals.Should().BeEmpty("a time choice must not be interpreted as a service selection");
        var availability = scheduling.Actions.Single(action => action.Operation == "reservation.check_availability");
        availability.Condition.Should().NotBeNull();
        availability.Condition!.Not.Should().NotBeNull();
        availability.Condition.Not!.Any.Should().Contain(condition =>
            condition.FactChanged == "business_type");
        availability.Condition.Not.Any.Should().Contain(condition =>
            condition.FactChanged == "pain_point");
        availability.OnOutcome["availability.exact_time_available"].Effects.Should().ContainSingle(effect =>
            effect.Type == StageEffectTypes.SetFact
            && effect.Fact == "availability_checked"
            && effect.Value.ValueKind == JsonValueKind.True);
        availability.OnOutcome["availability.options_available"].Effects.Should().BeEmpty();
        stages["customer_data"].Collect.Should().NotContain("business_profile_url");
        stages["customer_data"].ConversationGuidance.Should()
            .Contain("No pidas redes sociales en esta etapa");
        stages["customer_data"].Response.Template.Should().Be("customer_data_question");
        var socialProfiles = stages["social_profiles"];
        socialProfiles.AdvanceWhenFacts.Should().Equal("social_profiles_answered");
        socialProfiles.Collect.Should().Contain(["business_profile_url", "social_profiles_answered"]);
        socialProfiles.ConversationGuidance.Should()
            .Contain("despues de validar fecha y hora y antes del resumen")
            .And.Contain("Nunca bloquees el agendamiento");
        socialProfiles.Response.Template.Should().Be("social_profiles_question");
        socialProfiles.Transitions.Should().Contain(transition => transition.To == "confirmation");
        availability.OnOutcome["availability.requested_time_unavailable"].Effects.Should().ContainSingle(effect =>
            effect.Type == StageEffectTypes.ClearFacts
            && effect.Facts.Contains("desired_time"));
        config.Templates["availability_slots"].Should().Contain("{{#if intro_message}}{{intro_message}}{{/if}}\n\n*");
        availability.OnOutcome["availability.none"].Effects.Should().ContainSingle();
        availability.OnOutcome["availability.none"].Response!.Template.Should().Be("availability_none");
        availability.OnOutcome["input.past_date"].Effects.Should().ContainSingle();
        availability.OnOutcome["input.past_date"].Response!.Template.Should().Be("past_date_invalid");
        availability.OnOutcome["input.invalid_date"].Response!.Template.Should().Be("date_invalid");
        availability.OnOutcome["input.invalid_time"].Response!.Template.Should().Be("time_invalid");

        var create = stages["reservation_creation"].Actions.Single(action => action.Operation == "reservation.create");
        create.Execution.Idempotency.Should().Be(StageActionIdempotency.OncePerRequest);
        create.Arguments.Should().ContainKey("customer_email");
        create.Condition!.All.Should().Contain(condition => condition.FactEquals != null
            && condition.FactEquals.Key == "customer_confirmed"
            && condition.FactEquals.Value.ValueKind == JsonValueKind.True);
        create.Condition.All.Should().Contain(condition => condition.VerificationActive == "availability_checked");
        create.OnOutcome["reservation.created"].Effects.Should().Contain(effect =>
            effect.Type == StageEffectTypes.CompleteRequest);
        create.OnOutcome["reservation.created"].Response!.Template.Should().Be("demo_created");

        var reservationNotification = config.Notifications["reservation_created"];
        reservationNotification.Enabled.Should().BeTrue();
        reservationNotification.Deliveries.Should().ContainSingle();
        var reservationInternalDelivery = reservationNotification.Deliveries.Single();
        reservationInternalDelivery.Id.Should().Be("internal");
        reservationInternalDelivery.Recipients.Should().Equal("573012926660");
        reservationInternalDelivery.SendMessageSequence.Should().Be("internal_demo_scheduled");
        var internalNotification = config.MessageSequences["internal_demo_scheduled"];
        internalNotification.Messages.Should().ContainSingle();
        internalNotification.Messages.Single().Body.Should()
            .Contain("{CustomerName}")
            .And.Contain("{company_name}")
            .And.Contain("{business_type}")
            .And.Contain("{pain_point}")
            .And.NotContain("{business_profile_url}");
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
        var seedSql = File.ReadAllText(path);
        var settingsJson = ExtractSettingsJson(seedSql, "SettingsJson");
        var config = JsonSerializer.Deserialize<AgentConfig>(
            settingsJson,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            })!;

        config.Commerce.Conversation.ContextualConfirmationPhrases.Should().Contain("si esa");
        config.Commerce.Conversation.QuantityWords["dos"].Should().Be(2m);
        config.Commerce.PendingCart.DiscardOnFinalizeIssueCodes.Should()
            .BeEquivalentTo("product_unavailable", "product_not_found");
        config.Commerce.PendingCart.DiscardAllOnExplicitFinalization.Should().BeTrue();
        config.Commerce.Conversation.CartReviewRules.Should().Contain(rule =>
            rule.Phrase == "como queda el carrito" && rule.Match == CommercePhraseMatchModes.Contains);
        config.Commerce.Conversation.ProductReplacementRules.Should().Contain(rule =>
            rule.Phrase == "no es el producto" && rule.Match == CommercePhraseMatchModes.Contains);        config.Commerce.Conversation.ProductReplacementRules.Should().Contain(rule =>
            rule.Phrase == "no lo quiero" && rule.Match == CommercePhraseMatchModes.Contains);        config.Commerce.PendingCart.FinalizeConfirmationPhrases.Should().Contain("si");
        seedSql.Should().Contain("{{#if can_finalize_with_pending}}")
            .And.Contain("¿Eso sería todo o deseas agregar algo más?");
        config.Commerce.Matching.ExactNameDominanceMinimumMatches.Should().Be(2);
        config.ConversationOpening.Enabled.Should().BeTrue();
        config.ConversationOpening.Guidance.Should()
            .Contain("una sola bienvenida")
            .And.Contain("bienvenida a CJ Distribuciones")
            .And.Contain("uno o dos emojis")
            .And.Contain("si no lo conoces, no inventes ninguno")
            .And.Contain("una linea en blanco")
            .And.Contain("No digas 'aqui estoy para lo que necesites'")
            .And.Contain("No menciones el tipo de cliente, ciudad, direccion, telefono")
            .And.Contain("compras anteriores");
        config.ConversationOpening.AllowQuestions.Should().BeFalse();
        config.FailureResponses.LlmUnavailable.Should().Contain("inconveniente temporal");
        config.BasePrompt.Should().Contain("cercana, empatica, natural y servicial");
        config.BasePrompt.Should().Contain("parrafos cortos y espacios en blanco");
        var restart = config.GlobalActions.Should().ContainSingle(action =>
            action.Signal.Type == "restart_request").Subject;
        restart.Actions.Should().ContainSingle(action =>
            action.Operation == "conversation.reset_request");
        restart.ConversationGuidance.Should().Contain("No lo detectes por un saludo solo");

        seedSql.Should().Contain("\"id\":\"cart_review_request\"")
            .And.Contain("\"operation\":\"commerce.get_order_draft\"")
            .And.Contain("$.templates.cart_changes_applied")
            .And.Contain("$.templates.cart_on_request")
            .And.Contain("replacement_reference")
            .And.Contain("{{#if removed}}");
        var catalogLookup = config.GlobalActions.Should().ContainSingle(action =>
            action.Signal.Type == "catalog_query").Subject;
        catalogLookup.Actions.Should().ContainSingle(action =>
            action.Operation == "commerce.search_products"
            && action.Signal == "catalog_query");
        config.Flows.SelectMany(flow => flow.Stages)
            .SelectMany(stage => stage.Signals)
            .Should().NotContain(signal => signal.Type == "catalog_query",
                "catalog lookup has one global owner and must not execute again inside a stage");
        config.Flows.SelectMany(flow => flow.Stages)
            .SelectMany(stage => stage.Actions)
            .Should().NotContain(action => action.Signal == "catalog_query",
                "global and stage actions must never compete for the same catalog turn");

        var cartMutation = config.GlobalActions.Should().ContainSingle(action =>
            action.Signal.Type == "order_changes").Subject;
        var globalCartAction = cartMutation.Actions.Should().ContainSingle(action =>
            action.Operation == "commerce.apply_order_changes"
            && action.Signal == "order_changes").Subject;
        globalCartAction.Execution.Idempotency.Should().Be(StageActionIdempotency.None);
        globalCartAction.OnOutcome["cart.applied"].Effects.Should().Contain(effect =>
            effect.Type == StageEffectTypes.ClearFacts
            && effect.Facts.Contains("order_finalized")
            && effect.Facts.Contains("order_checkout_presented"));
        config.Flows.SelectMany(flow => flow.Stages)
            .Where(stage => stage.Signals.Any(signal => signal.Type == "order_changes"))
            .Select(stage => stage.Id)
            .Should().BeEquivalentTo(["product_selection"],
                "product selection owns cart changes until the customer advances directly to delivery");
        seedSql.Should()
            .Contain("(N'$.globalActions[1].actions[0].execution')")
            .And.Contain("(N'$.flows[0].stages[2].actions[2].execution')")
            .And.Contain(
                "JSON_QUERY(N'{\"idempotency\":\"input_version\",\"timeoutSeconds\":240,\"maxAttempts\":1}')",
                "all CJ cart mutation owners must receive the long-running external batch policy");

        var createOrder = config.Flows
            .SelectMany(flow => flow.Stages)
            .SelectMany(stage => stage.Actions)
            .Should().ContainSingle(action =>
                action.Operation == "commerce.create_order").Subject;
        createOrder.Execution.Idempotency.Should().Be(
            StageActionIdempotency.OncePerRequest,
            "an explicit confirmation replay must never create a duplicate order");

        foreach (var flow in config.Flows)
        {
            foreach (var stage in flow.Stages)
            {
                var scope = MimosBabySpa.Application.Agents.Planning.TurnPlanScopeBuilder.Build(
                    config,
                    stage,
                    new Dictionary<string, string>(),
                    flow.Id);
                scope.Signals.Should().ContainKey("catalog_query",
                    $"catalog queries must remain available while the active stage is '{stage.Id}'");
                scope.Signals.Should().ContainKey("order_changes",
                    $"cart mutations must remain available while the active stage is '{stage.Id}'");
            }
        }


        settingsJson.Should().NotContain("fallbackTemplate").And.NotContain("clarificationTemplate");
        config.Templates.Should().NotContainKeys(
            "customer_name_prompt",
            "customer_type_prompt",
            "product_selection_prompt",
            "order_draft_unavailable");
        config.Templates["order_checkout_no_payment"].Should().Contain("- Cliente: {{customer_name}}");
        var customerNameStage = config.Flows.SelectMany(flow => flow.Stages)
            .Single(stage => stage.Id == "customer_name");
        customerNameStage.ConversationGuidance.Should()
            .Contain("No repitas el saludo ni la bienvenida")
            .And.NotContain("openingDirective");
        var productSelectionStage = config.Flows.SelectMany(flow => flow.Stages)
            .Single(stage => stage.Id == "product_selection");
        productSelectionStage.ConversationGuidance.Should().Contain("sin repetir la bienvenida")
            .And.Contain("ni mencionar su perfil, ubicacion o categorias supuestas")
            .And.Contain("Elegir una referencia ofrecida por una consulta no la agrega al pedido")
            .And.Contain("nunca supongas una unidad");
        config.Templates["catalog_results"].Should()
            .Contain("Cual te interesa y cuantas unidades deseas agregar?")
            .And.NotContain("Cual te gustaria agregar?");
        config.Templates["order_checkout_no_payment"].Should().Contain("- Recibe: {{delivery_recipient_name}}");

        var facts = config.FactSchema.ToDictionary(fact => fact.Key, StringComparer.OrdinalIgnoreCase);
        facts["customer_name"].ExtractionGuidance.Should().Contain("No lo actualices").And.Contain("recibe");
        facts["delivery_recipient_name"].Role.Should().Be("shipping.recipient_name");
        facts["delivery_recipient_name"].Scope.Should().Be("request");
        facts["delivery_reference"].Role.Should().Be("shipping.reference");
        facts["delivery_reference"].Required.Should().BeFalse();
        facts.Should().NotContainKey("delivery_location_status");

        var orderData = config.Flows.SelectMany(flow => flow.Stages)
            .Single(stage => stage.Id == "order_data");
        orderData.Collect.Should().Contain(["delivery_reference", "delivery_recipient_name"]);
        orderData.Collect.Should().NotContain(["customer_name", "delivery_location_status"]);
        orderData.AdvanceWhenFacts.Should().NotContain("delivery_location_status");
        orderData.ConversationGuidance.Should()
            .Contain("un solo mensaje breve y estructurado")
            .And.Contain("direccion completa")
            .And.Contain("referencia complementaria")
            .And.Contain("nombre de quien recibe")
            .And.Contain("celular de entrega")
            .And.Contain("no envies una pregunta separada por cada dato")
            .And.Contain("opcional")
            .And.Contain("nunca debe detener el flujo");
        config.Checkout.Modes["order"].RequiredFactRoles.Should().BeEmpty();
        facts["payment_method"].Options.Select(option => option.Value)
            .Should().BeEquivalentTo("efectivo", "transferencia", "datafono");
        var paymentStage = config.Flows.SelectMany(flow => flow.Stages)
            .Single(stage => stage.Id == "payment_method");
        paymentStage.ConversationGuidance.Should()
            .Contain("efectivo, transferencia o datafono")
            .And.Contain("payment_method=datafono");
        var cardTerminal = config.Checkout.Modes["order"].PaymentMethods["datafono"];
        cardTerminal.Template.Should().Be("order_checkout_card_terminal");
        cardTerminal.ManualConfirmationRequired.Should().BeFalse();
        cardTerminal.Aliases.Should().Contain(["datafono", "datáfono", "tarjeta"]);
        foreach (var templateId in new[]
                 {
                     "cart_snapshot",
                     "order_checkout_no_payment",
                     "order_checkout_card_terminal",
                     "order_checkout_manual_transfer"
                 })
        {
            config.Templates[templateId].Should().Contain(
                "{{requested_name}} ({{name}})",
                $"template '{templateId}' should show the requested label beside its resolved catalog product");
        }
        var renderedCheckout = new PromptTemplateRenderer().Render(
            config.Templates["order_checkout_no_payment"],
            new Dictionary<string, object?>
            {
                ["line_items"] = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["requested_name"] = "papas",
                        ["name"] = "PAPA FARM FRITES 3/8 X 2.5 KG",
                        ["quantity"] = "2",
                        ["line_total"] = "20,000.00"
                    }
                },
                ["shipping_cost"] = "0.00",
                ["total"] = "20,000.00",
                ["currency"] = "COP",
                ["city"] = "Valledupar",
                ["delivery_address"] = "Calle 5",
                ["customer_phone"] = "3012926660"
            });
        renderedCheckout.Should().Contain(
            "papas (PAPA FARM FRITES 3/8 X 2.5 KG) x2");
        config.Templates["order_checkout_card_terminal"].Should()
            .Contain("Metodo de pago: datafono al recibir")
            .And.Contain("Llevaremos el datafono")
            .And.Contain("Confirmas tu pedido con esta informacion?");
        var confirmationStage = config.Flows.SelectMany(flow => flow.Stages)
            .Single(stage => stage.Id == "order_confirmation");
        var deliveryPaymentConfirmation = confirmationStage.Actions.Single(action =>
            action.Id == "create_confirmed_delivery_payment_order");
        deliveryPaymentConfirmation.Operation.Should().Be("commerce.create_order");
        deliveryPaymentConfirmation.Arguments["customer_confirmed"].GetString()
            .Should().Be("{{fact.customer_confirmed}}");
        deliveryPaymentConfirmation.Condition!.All.Should().Contain(condition =>
            condition.Any.Any(option => option.FactEquals != null
                && option.FactEquals.Key == "payment_method"
                && option.FactEquals.Value.GetString() == "datafono"));
        deliveryPaymentConfirmation.Condition.All.Should().Contain(condition =>
            condition.FactEquals != null
            && condition.FactEquals.Key == "customer_confirmed"
            && condition.FactEquals.Value.ValueKind == JsonValueKind.True);

        var checkoutFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_finalized"] = "true",
            ["delivery_method"] = "domicilio",
            ["city"] = "Valledupar",
            ["delivery_address"] = "Calle 5",
            ["delivery_phone"] = "3012926660",
            ["customer_name"] = "Richard",
            ["payment_method"] = "efectivo"
        };
        MimosBabySpa.Application.Agents.Runtime.StageAdvanceFactReadiness
            .IsComplete(orderData, checkoutFacts, config.FactSchema)
            .Should().BeTrue("a supplied address is sufficient and an optional reference cannot block payment");

        var summary = config.Flows.SelectMany(flow => flow.Stages)
            .Single(stage => stage.Id == "summary");
        var prepareCheckout = summary.Actions.Single(action => action.Operation == "commerce.prepare_checkout");
        new MimosBabySpa.Application.Agents.Runtime.StageConditionEvaluator()
            .Evaluate(
                prepareCheckout.Condition,
                new MimosBabySpa.Application.Agents.Runtime.DeterministicStageExecutionContext
                {
                    Facts = checkoutFacts
                })
            .Should().BeTrue("after payment the official checkout summary must be eligible without a reference");

        config.Templates["catalog_results"].Should().Contain("\r\n\r\n*Productos disponibles*\r\n\r\n");
        config.Templates["cart_snapshot"].Should().Contain("\r\n\r\n*Pedido actual*\r\n\r\n");
    }

    [Fact]
    public void CjConversationFollowUp_IsOwnedOnlyByStages()
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(
            root,
            "database",
            "MimosBabySpa.Database",
            "Scripts",
            "Seeds",
            "SeedCJDistribuciones.sql");
        var seedSql = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AgentConfig>(
            ExtractSettingsJson(seedSql, "SettingsJson"),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter() }
            })!;

        config.ConversationFollowUp.Enabled.Should().BeTrue();
        config.ConversationFollowUp.DelayMinutes.Should().Be(120);
        config.ConversationFollowUp.RespectOperatingHours.Should().BeTrue();
        config.ConversationFollowUp.FallbackSequence.Should().BeNullOrWhiteSpace(
            "CJ retakes the pending context instead of sending a generic canned message");
        config.ConversationFollowUp.Guidance.Should()
            .Contain("pregunta, eleccion o confirmacion concreta")
            .And.Contain("una sola pregunta enfocada")
            .And.Contain("No repitas catalogos, carritos ni resumenes completos")
            .And.Contain("no modifiques el pedido");

        var order = config.Flows.Single(flow => flow.Id == "order");
        order.Stages.Should().NotContain(stage => stage.Id == "cart_review",
            "finalization advances directly from product selection to delivery");
        order.Stages
            .Where(stage => stage.AwaitCustomerReply)
            .Select(stage => stage.Id)
            .Should().BeEquivalentTo(
                "customer_name",
                "customer_type",
                "product_selection",
                "order_data",
                "payment_method",
                "summary",
                "order_confirmation");

        var summary = order.Stages.Single(stage => stage.Id == "summary");
        summary.Transitions.Select(transition => transition.To).Should()
            .BeEquivalentTo("manual_payment_pending", "order_confirmation");
        order.Stages.Single(stage => stage.Id == "manual_payment_pending")
            .AwaitCustomerReply.Should().BeFalse(
                "manual transfer approval waits on CJ's internal team, not on the customer");

        seedSql.Should()
            .NotContain(".response.awaitCustomerReply")
            .And.NotContain("{\"awaitCustomerReply\":true}");
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
