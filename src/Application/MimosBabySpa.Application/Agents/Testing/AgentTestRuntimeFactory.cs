using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.Agents.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Runtime;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Operations.Support;

using MimosBabySpa.Application.Billing;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Time;

namespace MimosBabySpa.Application.Agents.Testing;

public sealed class AgentTestRuntimeFactory : IAgentTestRuntimeFactory
{
    private readonly IAgentConfigProvider _configProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IBusinessClock _businessClock;
    private readonly IOperatingHoursTurnPolicy _operatingHoursTurnPolicy;
    private readonly IConversationFactsService _factsService;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly IReservationLifecycleService _reservationLifecycle;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IConversationService _conversationService;
    private readonly IConversationLifecycleService _lifecycleService;
    private readonly IFactHydrator _factHydrator;
    private readonly ILogger<AgentConversationService> _conversationLogger;

    public AgentTestRuntimeFactory(
        IAgentConfigProvider configProvider,
        IServiceProvider serviceProvider,
        IConversationStateManager stateManager,
        IMessageService messageService,
        IBusinessClock businessClock,
        IOperatingHoursTurnPolicy operatingHoursTurnPolicy,
        IConversationFactsService factsService,
        ICustomerMemoryService customerMemory,
        IReservationLifecycleService reservationLifecycle,
        IPaymentLifecycleService paymentLifecycle,
        IConversationService conversationService,
        IConversationLifecycleService lifecycleService,
        IFactHydrator factHydrator,
        ILogger<AgentConversationService> conversationLogger)
    {
        _configProvider = configProvider;
        _serviceProvider = serviceProvider;
        _stateManager = stateManager;
        _messageService = messageService;
        _businessClock = businessClock;
        _operatingHoursTurnPolicy = operatingHoursTurnPolicy;
        _factsService = factsService;
        _customerMemory = customerMemory;
        _reservationLifecycle = reservationLifecycle;
        _paymentLifecycle = paymentLifecycle;
        _conversationService = conversationService;
        _lifecycleService = lifecycleService;
        _factHydrator = factHydrator;
        _conversationLogger = conversationLogger;
    }

    public IAgentConversationService Create(
        AgentTestExecutionLog log,
        IDictionary<string, string>? initialFacts = null)
    {
        var memoryFacts = initialFacts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var testFactsService = new AgentTestConversationFactsService(_factsService, memoryFacts);
        var requestContext = new RequestContextService(
            testFactsService,
            _serviceProvider.GetRequiredService<ILogger<RequestContextService>>());
        var productionOperations = _serviceProvider.GetRequiredService<AgentOperationRegistry>();
        var operationRegistry = new AgentOperationRegistry(productionOperations.All
            .Select(operation => (IAgentOperation)new AgentTestOperationDecorator(operation, log)));
        var stageExecutor = new DeterministicStageExecutor(
            operationRegistry,
            _serviceProvider.GetRequiredService<StageConditionEvaluator>(),
            _serviceProvider.GetRequiredService<OperationArgumentBinder>());
        var coordinator = new DeterministicTurnCoordinator(
            _serviceProvider.GetRequiredService<ITurnPlanner>(),
            _serviceProvider.GetRequiredService<IDeterministicFlowSelector>(),
            _serviceProvider.GetRequiredService<FactMutationBatchProcessor>(),
            testFactsService,
            _serviceProvider.GetRequiredService<IConversationVerificationService>(),
            stageExecutor,
            _serviceProvider.GetRequiredService<DeterministicStageTransitionResolver>());
        var usageBilling = new AgentTestUsageBillingService(log);

        return new AgentConversationService(
            _configProvider,
            operationRegistry,
            _stateManager,
            _messageService,
            _businessClock,
            _operatingHoursTurnPolicy,
            testFactsService,
            _customerMemory,
            requestContext,
            _reservationLifecycle,
            _paymentLifecycle,
            _conversationService,
            _lifecycleService,
            _factHydrator,
            usageBilling,
            _conversationLogger,
            coordinator,
            _serviceProvider.GetRequiredService<IDeterministicResponseRenderer>(),
            new AgentTestTurnEffectProcessor());
    }

}
