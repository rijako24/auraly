using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
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
        // ── Logging ───────────────────────────────────────────────────────────
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        // ── Infrastructure Fakes ──────────────────────────────────────────────
        var unitOfWork = new InMemoryUnitOfWork(businessId);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton(unitOfWork.Conversations);
        services.AddSingleton(unitOfWork.Messages);
        services.AddSingleton(unitOfWork.ConversationStates);

        services.AddSingleton<IAvailabilityService>(new FakeAvailabilityService(calendarMode));
        services.AddSingleton<IReservationService>(new FakeReservationService(reservationMode));
        services.AddSingleton<IEmployeeAssignmentService>(new FakeEmployeeAssignmentService(businessId));
        services.AddSingleton<IConversationService>(new FakeConversationService(businessId));
        services.AddSingleton<IMessageService>(sp =>
            new FakeMessageService(sp.GetRequiredService<IUnitOfWork>().Messages));
        services.AddSingleton<IBusinessRuleEngine, FakeBusinessRuleEngine>();

        // ── State Management ──────────────────────────────────────────────────
        services.AddSingleton<IConversationStateRepository>(unitOfWork.ConversationStates);
        services.AddSingleton<IPaymentTransactionRepository, InMemoryPaymentTransactionRepository>();
        services.AddSingleton<IPaymentLinkService, FakePaymentLinkService>();
        services.AddSingleton<IConversationStateUpdater, ConversationStateUpdater>();
        services.AddSingleton<IConversationStateManager, ConversationStateManager>();

        // ── Business Context ──────────────────────────────────────────────────
        services.AddSingleton<CachedBusinessContextProvider>();

        // ── Escalation ────────────────────────────────────────────────────────
        services.AddSingleton<IWhatsAppService, NoOpWhatsAppService>();
        services.AddSingleton<FakeAdminActionLinkService>();
        services.AddSingleton<IAdminActionLinkService>(sp => sp.GetRequiredService<FakeAdminActionLinkService>());
        services.AddSingleton<IReleaseLinkService>(sp => sp.GetRequiredService<FakeAdminActionLinkService>());
        services.AddSingleton<IConversationReleaseService, ConversationReleaseService>();
        services.AddSingleton<IEscalationNotifier, EscalationNotifier>();
        services.AddSingleton<IEscalationConfigProvider, EscalationConfigProvider>();

        // ── Agentic Engine ────────────────────────────────────────────────────
        services.AddSingleton<IChatClient>(fakeChatClient);
        services.AddSingleton<IAgentConfigProvider>(new FakeAgentConfigProvider(businessId));

        // Tools reales envueltas con interceptors
        services.AddSingleton<AgentToolRegistry>(sp =>
        {
            var stateManager = sp.GetRequiredService<IConversationStateManager>();
            var availability = sp.GetRequiredService<IAvailabilityService>();
            var reservation = sp.GetRequiredService<IReservationService>();
            var rules = sp.GetRequiredService<IBusinessRuleEngine>();
            var escalation = sp.GetRequiredService<IEscalationNotifier>();
            var logger = sp.GetRequiredService<ILogger<AgentToolRegistry>>();

            IAgentTool[] rawTools =
            [
                new CheckAvailabilityTool(availability),
                new CreateReservationTool(reservation, rules, stateManager),
                new EscalateToHumanTool(stateManager, escalation),
            ];

            var intercepted = rawTools
                .Select(t => (IAgentTool)new ToolCallInterceptor(t, toolCallLog));

            return new AgentToolRegistry(intercepted, logger);
        });

        services.AddSingleton<IAgentConversationService, AgentConversationService>();
    }
}
