using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Bootstrap;

/// <summary>
/// Configura un ServiceProvider aislado para cada escenario de test.
/// No hay BD real, no hay LLM real, no hay calendar real; solo fakes en memoria.
/// Las IAgentTool estan envueltas con ToolCallInterceptor para registrar todas las llamadas.
/// </summary>
public static class TestServiceBuilder
{
    public static void Register(
        IServiceCollection services,
        Guid businessId,
        Guid agentId,
        CalendarMode calendarMode,
        ReservationMode reservationMode,
        ToolCallLog toolCallLog,
        FakeChatClient fakeChatClient)
    {
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        var unitOfWork = new InMemoryUnitOfWork(businessId);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton(unitOfWork);
        services.AddSingleton(unitOfWork.Conversations);
        services.AddSingleton(unitOfWork.Messages);
        services.AddSingleton(unitOfWork.ConversationStates);
        services.AddSingleton(unitOfWork.ConversationContexts);
        services.AddSingleton(unitOfWork.CustomerMemory);
        services.AddSingleton(unitOfWork.Reservations);
        services.AddSingleton(unitOfWork.PaymentTransactions);

        services.AddSingleton<IAvailabilityService>(new FakeAvailabilityService(calendarMode));
        services.AddSingleton<IReservationService>(new FakeReservationService(reservationMode));
        services.AddSingleton<IEmployeeAssignmentService>(new FakeEmployeeAssignmentService(businessId));
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<IConversationLifecycleService, ConversationLifecycleService>();
        services.AddSingleton<ILeadService, LeadService>();
        services.AddSingleton<IMessageService>(sp =>
            new FakeMessageService(sp.GetRequiredService<IUnitOfWork>().Messages));
        services.AddSingleton<IBusinessRuleEngine, FakeBusinessRuleEngine>();
        services.AddSingleton<IBusinessClock>(new FakeBusinessClock());
        services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();
        services.AddSingleton<ISchedulingPolicyProvider, FakeSchedulingPolicyProvider>();
        services.AddSingleton<IWorkingHoursService, WorkingHoursService>();
        services.AddSingleton<IUsageBillingService, NoOpUsageBillingService>();

        services.AddSingleton<IConversationStateRepository>(unitOfWork.ConversationStates);
        services.AddSingleton<IPaymentTransactionRepository>(unitOfWork.PaymentTransactions);
        services.AddSingleton<IPaymentLinkService, FakePaymentLinkService>();
        services.AddSingleton<IConversationStateManager, ConversationStateManager>();
        services.AddSingleton<ICustomerMemoryService, CustomerMemoryService>();
        services.AddSingleton<IConversationFactsService, ConversationFactsService>();
        services.AddSingleton<IRequestContextService, RequestContextService>();
        services.AddSingleton<IReservationLifecycleService, ReservationLifecycleService>();
        services.AddSingleton<ICustomerReservationResolver, CustomerReservationResolver>();
        services.AddSingleton<IPaymentLifecycleService, PaymentLifecycleService>();
        services.AddSingleton<IReservationIntentBuilder, ReservationIntentBuilder>();
        services.AddSingleton<IConversationVerificationService, ConversationVerificationService>();
        services.AddSingleton<IGuardEvaluator, GuardEvaluator>();
        services.AddSingleton<IToolCapabilityGate, ToolCapabilityGate>();
        services.AddSingleton<ITurnEventExtractor, NoOpTurnEventExtractor>();
        services.AddSingleton<IFlowRuntimeStateResolver, FlowRuntimeStateResolver>();
        services.AddSingleton<IFlowPolicyEngine, FlowPolicyEngine>();
        services.AddSingleton<IFlowRuntimeOrchestrator, FlowRuntimeOrchestrator>();
        services.AddSingleton<IFlowStageDetector, FlowStageDetector>();
        services.AddSingleton<IFactHydrator, FactHydrator>();
        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelPhoneResolver>();
        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelEmailResolver>();
        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.EngagementResolver>();
        services.AddSingleton<IPromptComposer, AgentPromptComposer>();
        services.AddSingleton<IOperatingHoursTurnPolicy, OperatingHoursTurnPolicy>();
        services.AddSingleton<IAgentTurnToolResolver, AgentTurnToolResolver>();

        services.AddSingleton<IWhatsAppService, NoOpWhatsAppService>();
        services.AddSingleton<IReleaseLinkService, FakeReleaseLinkService>();
        services.AddSingleton<IConversationReleaseService, ConversationReleaseService>();
        services.AddSingleton<IEscalationNotifier, EscalationNotifier>();
        services.AddSingleton<IEscalationConfigProvider, EscalationConfigProvider>();
        services.AddSingleton<IMessageSequenceResolver, NoOpMessageSequenceResolver>();
        services.AddSingleton<IEventNotificationDispatcher, NoOpEventNotificationDispatcher>();

        services.AddSingleton<ServiceNameResolver>();
        services.AddSingleton<ServiceSelectionResolver>();
        services.AddSingleton<IAddOnCatalogService, AddOnCatalogService>();

        services.AddSingleton<IChatClient>(fakeChatClient);
        services.AddSingleton<IAgentConfigProvider>(new FakeAgentConfigProvider(businessId));

        services.AddSingleton<AgentToolRegistry>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AgentToolRegistry>>();

            IAgentTool[] rawTools =
            [
                new CheckAvailabilityTool(
                    sp.GetRequiredService<IAvailabilityService>(),
                    sp.GetRequiredService<ISchedulingPolicyProvider>(),
                    sp.GetRequiredService<IEmployeeAssignmentService>(),
                    sp.GetRequiredService<IUnitOfWork>(),
                    sp.GetRequiredService<IConversationVerificationService>(),
                    sp.GetRequiredService<ServiceNameResolver>()),
                new CreateReservationTool(
                    sp.GetRequiredService<IReservationService>(),
                    sp.GetRequiredService<IReservationIntentBuilder>(),
                    sp.GetRequiredService<IBusinessRuleEngine>(),
                    sp.GetRequiredService<IAvailabilityService>(),
                    sp.GetRequiredService<ISchedulingPolicyProvider>(),
                    sp.GetRequiredService<ILogger<CreateReservationTool>>()),
                new EscalateToHumanTool(sp.GetRequiredService<IEscalationNotifier>()),
                new GetCustomerReservationsTool(sp.GetRequiredService<IReservationLifecycleService>()),
                new GetCompatibleAddOnsTool(sp.GetRequiredService<IAddOnCatalogService>()),
                new ResolveServiceSelectionTool(
                    sp.GetRequiredService<ServiceSelectionResolver>(),
                    sp.GetRequiredService<IConversationFactsService>(),
                    sp.GetRequiredService<IAddOnCatalogService>()),
                new GetServiceFulfillmentTool(
                    sp.GetRequiredService<IUnitOfWork>(),
                    sp.GetRequiredService<ServiceNameResolver>()),
                new PrepareReservationChangeTool(
                    sp.GetRequiredService<IReservationService>(),
                    sp.GetRequiredService<ICustomerReservationResolver>()),
                new ConfirmReservationChangeTool(
                    sp.GetRequiredService<IReservationService>(),
                    sp.GetRequiredService<ICustomerReservationResolver>()),
                new SetFactTool(
                    sp.GetRequiredService<IConversationFactsService>(),
                    sp.GetRequiredService<IAddOnCatalogService>(),
                    sp.GetRequiredService<IConversationVerificationService>(),
                    sp.GetRequiredService<ILeadService>()),
            ];

            var intercepted = rawTools
                .Select(t => (IAgentTool)new ToolCallInterceptor(t, toolCallLog));

            return new AgentToolRegistry(intercepted, logger);
        });

        services.AddSingleton<IAgentTemplateResolver, AgentTemplateResolver>();
        services.AddSingleton<ITemplateRenderer, PromptTemplateRenderer>();
        services.AddSingleton<IAgentTurnResponseComposer, AgentTurnResponseComposer>();

        services.AddSingleton<IAgentConversationService, AgentConversationService>();
    }

    private sealed class NoOpUsageBillingService : IUsageBillingService
    {
        public Task<UsageGateResult> CanProcessAsync(Guid businessId, CancellationToken ct = default) =>
            Task.FromResult(new UsageGateResult(
                true,
                "allowed",
                "Allowed in integration tests.",
                Snapshot: null));

        public Task<UsageChargeResult> ChargeAsync(UsageChargeRequest request, CancellationToken ct = default) =>
            Task.FromResult(new UsageChargeResult(
                Charged: false,
                CreditsCharged: 0,
                EstimatedCostCop: 0,
                Snapshot: null));

        public Task<BusinessUsageSnapshot?> GetCurrentUsageAsync(Guid businessId, CancellationToken ct = default) =>
            Task.FromResult<BusinessUsageSnapshot?>(null);

        public Task<IReadOnlyList<UsagePlanDto>> GetPlansAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UsagePlanDto>>([]);
    }

    private sealed class NoOpMessageSequenceResolver : IMessageSequenceResolver
    {
        public Task<IReadOnlyList<OutboundMessage>> ResolveAsync(
            Guid businessId,
            string sequenceName,
            MimosBabySpa.Application.Agents.Configuration.MessageSequenceCatalog catalog,
            MessageSequenceContext context,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OutboundMessage>>([]);
    }

    private sealed class NoOpEventNotificationDispatcher : IEventNotificationDispatcher
    {
        public Task SendEventAsync(
            Guid businessId,
            AgentConfig config,
            string eventName,
            IReadOnlyDictionary<string, string>? custom = null,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SendEventAsync(
            Guid businessId,
            AgentConfig config,
            string eventName,
            MessageSequenceContext context,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SendEventForActiveAgentAsync(
            Guid businessId,
            string eventName,
            MessageSequenceContext context,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
