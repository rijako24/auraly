using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
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
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Tests.Reliability;

public abstract class ReliabilityTestBase
{
    // Mocks
    protected Mock<IConversationStateManager> _stateManagerMock;
    protected Mock<IFlowEngine> _flowEngineMock;
    protected Mock<IBusinessRuleEngine> _businessRuleEngineMock;
    protected Mock<IUnitOfWork> _unitOfWorkMock;
    protected Mock<IMemoryCache> _memoryCacheMock;
    protected Mock<ILogger<CachedBusinessContextProvider>> _cachedContextLoggerMock;
    protected Mock<ILogger<LoadedBusinessContext>> _loadedContextLoggerMock;
    protected Mock<IPromptProvider> _promptProviderMock;
    protected Mock<ILLMAdapter> _llmAdapterMock;
    protected Mock<IToolFactory> _toolFactoryMock;
    protected Mock<ISmartExtractionService> _extractionServiceMock;
    protected Mock<IMessageService> _messageServiceMock;
    protected Mock<IConversationStateUpdater> _stateUpdaterMock;
    protected Mock<IPaymentLinkService> _paymentLinkServiceMock;
    protected Mock<IPaymentTransactionRepository> _paymentTransactionRepositoryMock;
    protected Mock<ILogger<HybridTransactionalOrchestrator>> _loggerMock;
    protected Mock<ILogger<GenericToolDispatcher>> _dispatcherLoggerMock;

    // Orchestrator under test
    protected HybridTransactionalOrchestrator _orchestrator;
    protected CachedBusinessContextProvider _cachedContextProvider;
    protected GenericToolDispatcher _toolDispatcher;

    // Test Data
    protected Guid _conversationId;
    protected Guid _businessId;
    protected string _customerPhone;
    protected MimosBabySpa.Domain.Models.ConversationState _state = null!;

    public ReliabilityTestBase()
    {
        InitializeMocks();
        SetupDefaultBehavior();

        // Setup concrete helper classes with mocks
        _cachedContextProvider = new CachedBusinessContextProvider(
            _memoryCacheMock.Object,
            _unitOfWorkMock.Object,
            _cachedContextLoggerMock.Object,
            _loadedContextLoggerMock.Object
        );

        _toolDispatcher = new GenericToolDispatcher(
            _toolFactoryMock.Object,
            _dispatcherLoggerMock.Object
        );

        _orchestrator = new HybridTransactionalOrchestrator(
            _stateManagerMock.Object,
            _flowEngineMock.Object,
            _businessRuleEngineMock.Object,
            _cachedContextProvider,
            _promptProviderMock.Object,
            _llmAdapterMock.Object,
            _toolDispatcher,
            _extractionServiceMock.Object,
            _messageServiceMock.Object,
            _stateUpdaterMock.Object,
            _paymentLinkServiceMock.Object,
            _paymentTransactionRepositoryMock.Object,
            _loggerMock.Object
        );
    }

    private void InitializeMocks()
    {
        _stateManagerMock = new Mock<IConversationStateManager>();
        _flowEngineMock = new Mock<IFlowEngine>();
        _businessRuleEngineMock = new Mock<IBusinessRuleEngine>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _memoryCacheMock = new Mock<IMemoryCache>();
        _cachedContextLoggerMock = new Mock<ILogger<CachedBusinessContextProvider>>();
        _loadedContextLoggerMock = new Mock<ILogger<LoadedBusinessContext>>();
        _promptProviderMock = new Mock<IPromptProvider>();
        _llmAdapterMock = new Mock<ILLMAdapter>();
        _toolFactoryMock = new Mock<IToolFactory>();
        _extractionServiceMock = new Mock<ISmartExtractionService>();
        _messageServiceMock = new Mock<IMessageService>();
        _stateUpdaterMock = new Mock<IConversationStateUpdater>();
        _paymentLinkServiceMock = new Mock<IPaymentLinkService>();
        _paymentTransactionRepositoryMock = new Mock<IPaymentTransactionRepository>();
        _loggerMock = new Mock<ILogger<HybridTransactionalOrchestrator>>();
        _dispatcherLoggerMock = new Mock<ILogger<GenericToolDispatcher>>();
    }

    private void SetupDefaultBehavior()
    {
        _conversationId = Guid.NewGuid();
        _businessId = Guid.NewGuid();
        _customerPhone = "1234567890";

        _state = new MimosBabySpa.Domain.Models.ConversationState
        {
            StateId = Guid.NewGuid(),
            BusinessId = _businessId,
            Phone = _customerPhone,
            CurrentStage = TransactionStage.CollectingInformation,
            UpdatedAt = DateTime.UtcNow
        };

        // Default State Manager behavior
        _stateManagerMock.Setup(m => m.GetOrCreateStateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_state);

        // Default Message Service behavior
        _messageServiceMock.Setup(m => m.GetConversationHistoryAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new List<Domain.Entities.Message>());

        // Default LLM behavior
        _llmAdapterMock.Setup(m => m.SendMessageAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMResponse { Success = true, Content = "Default AI Response" });

        // Default Cache behavior
        var businessInfo = new Domain.Entities.Business { BusinessId = _businessId, Name = "Test Business" };
        _unitOfWorkMock.Setup(u => u.Businesses.GetByIdAsync(_businessId))
            .ReturnsAsync(businessInfo);

        // Mock Services
        _unitOfWorkMock.Setup(u => u.Services.GetByBusinessIdAsync(_businessId))
            .ReturnsAsync(new List<Domain.Entities.Service>());

        // Mock AddOnRules
        _unitOfWorkMock.Setup(u => u.ServiceAddOnRules.GetByBusinessIdAsync(_businessId))
            .ReturnsAsync(new List<Domain.Entities.ServiceAddOnRule>());

        // Mock BusinessConfigurations (OperatingHours, etc)
        _unitOfWorkMock.Setup(u => u.BusinessConfigurations.GetByBusinessIdAndKeyAsync(_businessId, It.IsAny<BusinessConfigurationKey>()))
            .ReturnsAsync((Domain.Entities.BusinessConfiguration)null);

        // Mock MemoryCache behavior properly to prevent NullReferenceException
        _memoryCacheMock.Setup(m => m.CreateEntry(It.IsAny<object>())).Returns(new Mock<ICacheEntry>().Object);
        object expectedValue = null;
        _memoryCacheMock.Setup(m => m.TryGetValue(It.IsAny<object>(), out expectedValue)).Returns(false);

        // State Updater default behavior (just updates the state object directly)
        _stateUpdaterMock.Setup(m => m.ApplyField(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<MimosBabySpa.Domain.Models.ConversationState, string, string>((s, f, v) =>
            {
                if (f == "Service") s.Service = v;
                if (f == "CustomerName") s.CustomerName = v;
                if (f == "DesiredDate")
                {
                     if (DateOnly.TryParse(v, out var date)) s.DesiredDate = date;
                }
                if (f == "DesiredTime")
                {
                     if (TimeOnly.TryParse(v, out var time)) s.DesiredTime = time;
                }
                if (f.StartsWith("Attribute:"))
                {
                     // Simulate applying attribute
                }
            })
            .Returns(new ApplyFieldResult(true, "Applied"));

        _stateUpdaterMock.Setup(m => m.ApplyConfirmationFlag(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Callback<MimosBabySpa.Domain.Models.ConversationState, string, bool, string>((s, f, v, e) =>
            {
                if (f == "ReservationConfirmed") s.ReservationConfirmed = v;
                if (f == "AvailabilityConfirmed") s.AvailabilityConfirmed = v;
                if (f == "ReservationCreated") s.ReservationCreated = v;
                if (f == "ConfirmationSummaryPresented") s.ConfirmationSummaryPresented = v;
                if (f == "AddOnsOffered") s.AddOnsOffered = v;
            })
            .Returns(new ApplyFieldResult(true, "Applied"));

        _stateUpdaterMock.Setup(m => m.ResetTransactionalFlags(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>()))
            .Callback<MimosBabySpa.Domain.Models.ConversationState>(s =>
            {
                s.ReservationConfirmed = false;
                s.AvailabilityConfirmed = false;
            });
    }

    protected void SetupExtraction(ExtractionOutput output)
    {
        _extractionServiceMock.Setup(m => m.ExtractWithValidationAsync(
            It.IsAny<string>(),
            It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(),
            It.IsAny<LoadedBusinessContext>(),
            It.IsAny<List<Domain.Entities.Message>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);
    }

    protected void SetupFlowEvaluation(TransactionStage stage, bool canCheck = false, bool canCreate = false)
    {
        _flowEngineMock.Setup(m => m.Evaluate(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), It.IsAny<RequiredFieldsConfiguration>()))
            .Returns(new FlowEvaluationResult
            {
                CurrentStage = stage,
                CanCheckAvailability = canCheck,
                CanCreateReservation = canCreate,
                MissingFields = new List<string>(),
                CompletenessPercentage = 50
            });
    }
}
