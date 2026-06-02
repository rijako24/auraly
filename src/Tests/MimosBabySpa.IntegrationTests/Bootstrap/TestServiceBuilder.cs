using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
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
/// No hay BD real, no hay LLM real, no hay calendar real — solo fakes en memoria.
/// Las IAgentTool están envueltas con ToolCallInterceptor para registrar todas las llamadas.
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
        services.AddSingleton<IConversationService>(new FakeConversationService(businessId));
        services.AddSingleton<IConversationLifecycleService, ConversationLifecycleService>();
        services.AddSingleton<ILeadService, LeadService>();
        services.AddSingleton<IMessageService>(sp =>
            new FakeMessageService(sp.GetRequiredService<IUnitOfWork>().Messages));
        services.AddSingleton<IBusinessRuleEngine, FakeBusinessRuleEngine>();
        services.AddSingleton<IBusinessClock>(new FakeBusinessClock());
        services.AddSingleton<ITemporalReferenceBuilder, TemporalReferenceBuilder>();
        services.AddSingleton<ISchedulingPolicyProvider, FakeSchedulingPolicyProvider>();
        services.AddSingleton<IBookingPolicyProvider, FakeBookingPolicyProvider>();

        services.AddSingleton<IConversationStateRepository>(unitOfWork.ConversationStates);
        services.AddSingleton<IPaymentTransactionRepository>(unitOfWork.PaymentTransactions);
        services.AddSingleton<IPaymentLinkService, FakePaymentLinkService>();
        services.AddSingleton<IConversationStateManager, ConversationStateManager>();
        services.AddSingleton<ICustomerMemoryService, CustomerMemoryService>();
        services.AddSingleton<IConversationClosedHook, ConversationSummaryHook>();
        services.AddSingleton<IConversationFactsService, ConversationFactsService>();
        services.AddSingleton<IReservationLifecycleService, ReservationLifecycleService>();
        services.AddSingleton<ICustomerReservationResolver, CustomerReservationResolver>();
        services.AddSingleton<IPaymentLifecycleService, PaymentLifecycleService>();
        services.AddSingleton<IReservationIntentBuilder, ReservationIntentBuilder>();
        services.AddSingleton<IConversationVerificationService, ConversationVerificationService>();
        services.AddSingleton<IGuardEvaluator, GuardEvaluator>();
        services.AddSingleton<IToolCapabilityGate, ToolCapabilityGate>();
        services.AddSingleton<IFlowStageDetector, FlowStageDetector>();
        services.AddSingleton<IFactHydrator, FactHydrator>();
        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelPhoneResolver>();
        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.ChannelEmailResolver>();
        services.AddSingleton<IFactSourceResolver, MimosBabySpa.Application.Agents.Facts.Resolvers.EngagementResolver>();
        services.AddSingleton<IPromptComposer, AgentPromptComposer>();

        services.AddSingleton<IWhatsAppService, NoOpWhatsAppService>();
        services.AddSingleton<FakeAdminActionLinkService>();
        services.AddSingleton<IAdminActionLinkService>(sp => sp.GetRequiredService<FakeAdminActionLinkService>());
        services.AddSingleton<IReleaseLinkService>(sp => sp.GetRequiredService<FakeAdminActionLinkService>());
        services.AddSingleton<IConversationReleaseService, ConversationReleaseService>();
        services.AddSingleton<IEscalationNotifier, EscalationNotifier>();
        services.AddSingleton<IEscalationConfigProvider, EscalationConfigProvider>();

        services.AddSingleton<ServiceNameResolver>();
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
                    sp.GetRequiredService<IConversationVerificationService>()),
                new CreateReservationTool(
                    sp.GetRequiredService<IReservationService>(),
                    sp.GetRequiredService<IReservationIntentBuilder>(),
                    sp.GetRequiredService<IBusinessRuleEngine>(),
                    sp.GetRequiredService<IBookingPolicyProvider>(),
                    sp.GetRequiredService<IPaymentLifecycleService>(),
                    sp.GetRequiredService<IAvailabilityService>(),
                    sp.GetRequiredService<ISchedulingPolicyProvider>(),
                    sp.GetRequiredService<IConversationLifecycleService>()),
                new EscalateToHumanTool(sp.GetRequiredService<IEscalationNotifier>()),
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
}
