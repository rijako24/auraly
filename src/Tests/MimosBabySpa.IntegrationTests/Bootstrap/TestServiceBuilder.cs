using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.Prompts;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Interception;

namespace MimosBabySpa.IntegrationTests.Bootstrap;

/// <summary>
/// Configures an isolated ServiceProvider for each test scenario.
/// No real DB, no real LLM, no real calendar — only in-memory fakes.
/// Tool handlers are wrapped with ToolCallInterceptor to log all calls.
/// </summary>
public static class TestServiceBuilder
{
    public static void Register(
        IServiceCollection services,
        Guid businessId,
        CalendarMode calendarMode,
        ReservationMode reservationMode,
        ToolCallLog toolCallLog,
        List<TurnScript> llmScripts)
    {
        // ── Logging ───────────────────────────────────────────────────
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // ── Memory Cache (required by CachedBusinessContextProvider) ──
        services.AddMemoryCache();

        // ── Infrastructure Fakes ──────────────────────────────────────
        var unitOfWork = new InMemoryUnitOfWork(businessId);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton(unitOfWork.Conversations);
        services.AddSingleton(unitOfWork.Messages);
        services.AddSingleton(unitOfWork.ConversationStates);

        services.AddSingleton<IAvailabilityService>(
            new FakeAvailabilityService(calendarMode));

        services.AddSingleton<IReservationService>(
            new FakeReservationService(reservationMode));

        services.AddSingleton<IEmployeeAssignmentService>(
            new FakeEmployeeAssignmentService(businessId));

        services.AddSingleton<IConversationService>(
            new FakeConversationService(businessId));

        services.AddSingleton<IMessageService>(sp =>
            new FakeMessageService(sp.GetRequiredService<IUnitOfWork>().Messages));

        services.AddSingleton<ILLMAdapter>(new FakeLLMAdapter(llmScripts));

        services.AddSingleton<IPromptProvider, FakePromptProvider>();

        services.AddSingleton<IBusinessRuleEngine, FakeBusinessRuleEngine>();

        // ── State Management ──────────────────────────────────────────
        services.AddSingleton<IConversationStateRepository>(unitOfWork.ConversationStates);
        services.AddSingleton<IPaymentTransactionRepository, InMemoryPaymentTransactionRepository>();
        services.AddSingleton<IPaymentLinkService, FakePaymentLinkService>();
        services.AddSingleton<IConversationStateUpdater, ConversationStateUpdater>();
        services.AddSingleton<IConversationStateManager, ConversationStateManager>();

        // ── Business Context ──────────────────────────────────────────
        services.AddSingleton<CachedBusinessContextProvider>();

        // ── Flow Engine ───────────────────────────────────────────────
        services.AddSingleton<IFlowEngine, FlowEngine>();

        // ── Extraction ────────────────────────────────────────────────
        services.AddSingleton<ISmartExtractionService>(sp =>
            new FakeSmartExtractionService(sp.GetRequiredService<ILLMAdapter>()));

        // ── Tool Handlers (real implementations) ─────────────────────
        services.AddSingleton<CheckAvailabilityToolHandler>();
        services.AddSingleton<CreateReservationToolHandler>();

        // ── Tool Factory with Interceptors ────────────────────────────
        services.AddSingleton<IToolFactory>(sp =>
        {
            var checkHandler  = (IToolHandler)sp.GetRequiredService<CheckAvailabilityToolHandler>();
            var createHandler = (IToolHandler)sp.GetRequiredService<CreateReservationToolHandler>();

            // Wrap with interceptors
            var interceptedCheck  = new ToolCallInterceptor(checkHandler,  ToolType.CheckAvailability, toolCallLog);
            var interceptedCreate = new ToolCallInterceptor(createHandler, ToolType.CreateReservation, toolCallLog);

            return new InterceptedToolFactory(interceptedCheck, interceptedCreate);
        });

        services.AddSingleton<GenericToolDispatcher>();

        // ── Escalation y release ──────────────────────────────────────
        services.AddSingleton<IWhatsAppService, NoOpWhatsAppService>();
        services.AddSingleton<FakeAdminActionLinkService>();
        services.AddSingleton<IAdminActionLinkService>(sp => sp.GetRequiredService<FakeAdminActionLinkService>());
        services.AddSingleton<IReleaseLinkService>(sp => sp.GetRequiredService<FakeAdminActionLinkService>());
        services.AddSingleton<IConversationReleaseService, ConversationReleaseService>();
        services.AddSingleton<IEscalationNotifier, EscalationNotifier>();
        services.AddSingleton<IEscalationConfigProvider, EscalationConfigProvider>();

        // ── Orchestrator ──────────────────────────────────────────────
        services.AddSingleton<HybridTransactionalOrchestrator>();
    }
}

/// <summary>
/// IToolFactory implementation that always returns the already-intercepted handlers.
/// </summary>
internal class InterceptedToolFactory : IToolFactory
{
    private readonly IToolHandler _check;
    private readonly IToolHandler _create;

    public InterceptedToolFactory(IToolHandler check, IToolHandler create)
    {
        _check  = check;
        _create = create;
    }

    public IToolHandler GetTool(ToolType toolType) => toolType switch
    {
        ToolType.CheckAvailability => _check,
        ToolType.CreateReservation => _create,
        _ => throw new ArgumentException($"Unsupported tool: {toolType}")
    };
}
