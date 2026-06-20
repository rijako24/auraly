using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
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
    private readonly IEscalationConfigProvider _escalationConfig;
    private readonly IBusinessClock _businessClock;
    private readonly ITemporalReferenceBuilder _temporalReferenceBuilder;
    private readonly IConversationFactsService _factsService;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly IReservationLifecycleService _reservationLifecycle;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IConversationService _conversationService;
    private readonly IConversationLifecycleService _lifecycleService;
    private readonly IPromptComposer _promptComposer;
    private readonly IAgentTurnResponseComposer _turnResponseComposer;
    private readonly IToolCapabilityGate _toolCapabilityGate;
    private readonly IFlowStageDetector _flowStageDetector;
    private readonly IFactHydrator _factHydrator;
    private readonly ILogger<AgentToolRegistry> _registryLogger;
    private readonly ILogger<AgentConversationService> _conversationLogger;

    public AgentTestRuntimeFactory(
        IAgentConfigProvider configProvider,
        IServiceProvider serviceProvider,
        IConversationStateManager stateManager,
        IMessageService messageService,
        IEscalationConfigProvider escalationConfig,
        IBusinessClock businessClock,
        ITemporalReferenceBuilder temporalReferenceBuilder,
        IConversationFactsService factsService,
        ICustomerMemoryService customerMemory,
        IReservationLifecycleService reservationLifecycle,
        IPaymentLifecycleService paymentLifecycle,
        IConversationService conversationService,
        IConversationLifecycleService lifecycleService,
        IPromptComposer promptComposer,
        IAgentTurnResponseComposer turnResponseComposer,
        IToolCapabilityGate toolCapabilityGate,
        IFlowStageDetector flowStageDetector,
        IFactHydrator factHydrator,
        ILogger<AgentToolRegistry> registryLogger,
        ILogger<AgentConversationService> conversationLogger)
    {
        _configProvider = configProvider;
        _serviceProvider = serviceProvider;
        _stateManager = stateManager;
        _messageService = messageService;
        _escalationConfig = escalationConfig;
        _businessClock = businessClock;
        _temporalReferenceBuilder = temporalReferenceBuilder;
        _factsService = factsService;
        _customerMemory = customerMemory;
        _reservationLifecycle = reservationLifecycle;
        _paymentLifecycle = paymentLifecycle;
        _conversationService = conversationService;
        _lifecycleService = lifecycleService;
        _promptComposer = promptComposer;
        _turnResponseComposer = turnResponseComposer;
        _toolCapabilityGate = toolCapabilityGate;
        _flowStageDetector = flowStageDetector;
        _factHydrator = factHydrator;
        _registryLogger = registryLogger;
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
        var testTools = BuildTestTools(log, memoryFacts);

        var registry = new AgentToolRegistry(testTools, _registryLogger);
        var usageBilling = new AgentTestUsageBillingService(log);
        var chatClient = ResolveChatClient(log);

        return new AgentConversationService(
            _configProvider,
            chatClient,
            registry,
            _stateManager,
            _messageService,
            _escalationConfig,
            _businessClock,
            _temporalReferenceBuilder,
            testFactsService,
            _customerMemory,
            requestContext,
            _reservationLifecycle,
            _paymentLifecycle,
            _conversationService,
            _lifecycleService,
            _promptComposer,
            _turnResponseComposer,
            _toolCapabilityGate,
            _flowStageDetector,
            _factHydrator,
            usageBilling,
            _serviceProvider.GetRequiredService<IMessageSequenceResolver>(),
            _serviceProvider.GetRequiredService<IReservationCreatedNotificationDispatcher>(),
            _conversationLogger);
    }

    private IChatClient ResolveChatClient(AgentTestExecutionLog log)
    {
        log.Add("llm_requested", "chat_client", new { provider = "AzureOpenAI" });
        return _serviceProvider.GetRequiredService<IChatClient>();
    }

    private List<IAgentTool> BuildTestTools(
        AgentTestExecutionLog log,
        IDictionary<string, string> memoryFacts) =>
    [
        WrapReadOnly(ActivatorUtilities.CreateInstance<GetServiceCatalogTool>(_serviceProvider), log),
        WrapReadOnly(ActivatorUtilities.CreateInstance<GetCompatibleAddOnsTool>(_serviceProvider), log),
        WrapReadOnly(ActivatorUtilities.CreateInstance<GetServiceFulfillmentTool>(_serviceProvider), log),
        WrapReadOnly(ActivatorUtilities.CreateInstance<CheckAvailabilityTool>(_serviceProvider), log),
        WrapReadOnly(ActivatorUtilities.CreateInstance<ResolvePricingTool>(_serviceProvider), log),
        Mock("set_fact", "Records a fact in the in-memory test context.", SetFactParametersSchemaBuilder.FallbackSchema, log, memoryFacts, [ToolCapabilities.FactWrite]),
        Mock("reset_flow_context", "Clears the in-memory test context.", BasicSchema, log, memoryFacts),
        Mock("prepare_checkout", "Simulates checkout preparation and payment link generation.", BasicSchema, log, memoryFacts, [ToolCapabilities.CheckoutPrepare]),
        Mock("prepare_order_checkout", "Simulates order checkout preparation and payment link generation.", BasicSchema, log, memoryFacts, [ToolCapabilities.CheckoutPrepare]),
        Mock("create_reservation", "Simulates reservation creation without persisting.", BasicSchema, log, memoryFacts, [ToolCapabilities.ReservationCreate]),
        Mock("assign_paid_slot", "Simulates assigning a paid slot without persisting.", BasicSchema, log, memoryFacts, [ToolCapabilities.PaidSlotAssign]),
        Mock("suspend_reservation", "Simulates reservation suspension without persisting.", BasicSchema, log, memoryFacts),
        Mock("verify_payment", "Simulates payment status verification without calling the provider.", BasicSchema, log, memoryFacts),
        Mock("escalate_to_human", "Simulates human escalation without notifications.", BasicSchema, log, memoryFacts, [ToolCapabilities.HumanEscalate]),
        Mock("send_message_sequence", "Simulates an outbound message sequence without sending.", BasicSchema, log, memoryFacts)
    ];

    private static IAgentTool WrapReadOnly(IAgentTool tool, AgentTestExecutionLog log) =>
        new AgentTestToolDecorator(tool, log);

    private static IAgentTool Mock(
        string name,
        string description,
        string schema,
        AgentTestExecutionLog log,
        IDictionary<string, string> memoryFacts,
        IReadOnlyList<string>? capabilities = null) =>
        new AgentTestMockTool(name, description, schema, log, memoryFacts, capabilities);

    private const string BasicSchema = """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": true
        }
        """;
}
