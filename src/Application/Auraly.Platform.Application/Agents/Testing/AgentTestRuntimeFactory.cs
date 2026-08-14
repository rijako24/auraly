using Auraly.Platform.Application.Agents.Planning;
using Auraly.Platform.Application.Agents.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Auraly.Platform.Application.Agents.Composition;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.Agents.Gating;
using Auraly.Platform.Application.Agents.Runtime;
using Auraly.Platform.Application.Agents.Templates;
using Auraly.Platform.Application.Agents.Operations.Support;

using Auraly.Platform.Application.Billing;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Application.StateManagement;
using Auraly.Platform.Application.LLM;
using Auraly.Platform.Application.Time;

namespace Auraly.Platform.Application.Agents.Testing;

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
            new AdminAgentConfigProvider(_configProvider),
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

    private sealed class AdminAgentConfigProvider(IAgentConfigProvider inner) : IAgentConfigProvider
    {
        public Task<AgentConfig> GetConfigAsync(Guid agentId, CancellationToken ct = default) =>
            inner.GetConfigForAdminAsync(agentId, ct);

        public Task<AgentConfig> GetConfigForAdminAsync(Guid agentId, CancellationToken ct = default) =>
            inner.GetConfigForAdminAsync(agentId, ct);

        public void Invalidate(Guid agentId) => inner.Invalidate(agentId);
    }

}
