using Xunit;
using Moq;
using FluentAssertions;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Tests.Reliability;

public class HappyPathTests : ReliabilityTestBase
{
    [Fact]
    public async Task Should_Complete_Full_Booking_Flow()
    {
        // 1. Initial Contact - Greeting
        // Note: InitialContact doesn't exist in TransactionStage enum, using CollectingInformation as default start
        SetupFlowEvaluation(TransactionStage.CollectingInformation);
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { IsInformationQuery = true },
            ExtractedFields = new List<ExtractedField>()
        });

        var result1 = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Hola, quiero información");

        result1.ReservationCreated.Should().BeFalse();

        // 2. User provides Name - collecting info
        SetupFlowEvaluation(TransactionStage.CollectingInformation);
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            ExtractedFields = new List<ExtractedField>
            {
                new ExtractedField { FieldName = "CustomerName", Value = "Maria", Confidence = 0.9f }
            }
        });

        var result2 = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Me llamo Maria");

        _state.CustomerName.Should().Be("Maria");

        // 3. User Selects Service & Date - Exploring Services -> Checking Availability
        SetupFlowEvaluation(TransactionStage.ExploringServices, canCheck: true);
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserRequestedAvailability = true },
            ExtractedFields = new List<ExtractedField>
            {
                new ExtractedField { FieldName = "Service", Value = "Masaje Infantil", Confidence = 0.9f },
                new ExtractedField { FieldName = "DesiredDate", Value = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"), Confidence = 0.9f }
            }
        });

        // Mock Tool Execution for Availability
        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CheckAvailability))
            .Returns(new Mock<IToolHandler>().Object);
        var availabilityToolMock = new Mock<IToolHandler>();
        availabilityToolMock.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult { Success = true, Message = "Disponible", StateModified = false });

        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CheckAvailability)).Returns(availabilityToolMock.Object);

        var result3 = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Quiero Masaje Infantil para mañana");

        _state.Service.Should().Be("Masaje Infantil");
        availabilityToolMock.Verify(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);


        // 4. User Confirms - Confirming Booking -> Create Reservation
        SetupFlowEvaluation(TransactionStage.ConfirmingBooking, canCreate: true);
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserConfirmedBooking = true }
        });

        // Mock Tool Execution for Reservation
        var reservationToolMock = new Mock<IToolHandler>();
        reservationToolMock.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult { Success = true, Message = "Reserva Creada", StateModified = true });

        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CreateReservation)).Returns(reservationToolMock.Object);

        var result4 = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Sí, confirmo");

        reservationToolMock.Verify(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_Defer_AddOns_If_Availability_Requested_Immediately()
    {
        // Scenario: User selects Service AND asks for availability ("Quiero Masaje Infantil para mañana")
        // Goal: Ensure Availability Check runs FIRST, and Add-ons are offered in the NEXT turn.

        // Setup Business Context with Add-ons (Mocked in Base usually, but we need to ensure ShouldOfferAddOns returns true)
        // In ReliabilityTestBase, we mocked AddOnRules to return empty list. We need to override that.

        var compatibleService = new ServiceInfo { Name = "Masaje Infantil", Category = ServiceCategory.Plan };
        var addOnRules = new List<AddOnRuleInfo>
        {
            new AddOnRuleInfo { AddOnName = "Aromaterapia", CompatibleWithServiceName = "Masaje Infantil" }
        };

        // Mock UnitOfWork to return these rules when context is loaded.
        // HOWEVER, LoadedBusinessContext is cached. If we reuse the same _cachedContextProvider, it might return old data.
        // But ReliabilityTestBase creates new Orchestrator per test? Yes, constructor runs per test.
        // So we can re-setup the mocks before calling ProcessMessageAsync.

        _unitOfWorkMock.Setup(u => u.ServiceAddOnRules.GetByBusinessIdAsync(_businessId))
            .ReturnsAsync(new List<Domain.Entities.ServiceAddOnRule>
            {
                // We need to match what LoadAddOnRulesAsync expects. It maps Entities to AddOnRuleInfo.
                // We need to return Domain Entities here.
                new Domain.Entities.ServiceAddOnRule
                {
                    AddOnService = new Domain.Entities.Service { ServiceName = "Aromaterapia", Description = "Olor rico", Price = 100, Category = ServiceCategory.Plan },
                    CompatibleService = new Domain.Entities.Service { ServiceName = "Masaje Infantil" }
                }
            });

        // We also need Services to match for "Masaje Infantil"
        _unitOfWorkMock.Setup(u => u.Services.GetByBusinessIdAsync(_businessId))
            .ReturnsAsync(new List<Domain.Entities.Service>
            {
                new Domain.Entities.Service { ServiceName = "Masaje Infantil", Category = ServiceCategory.Plan, IsActive = true }
            });

        // 1. User selects Service + Date + Asks Availability
        SetupFlowEvaluation(TransactionStage.ExploringServices, canCheck: true);
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserRequestedAvailability = true },
            ExtractedFields = new List<ExtractedField>
            {
                new ExtractedField { FieldName = "Service", Value = "Masaje Infantil", Confidence = 0.9f },
                new ExtractedField { FieldName = "DesiredDate", Value = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"), Confidence = 0.9f }
            }
        });

        // Mock Availability Tool
        var availabilityToolMock = new Mock<IToolHandler>();
        availabilityToolMock.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult { Success = true, Message = "Disponible", StateModified = false });
        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CheckAvailability)).Returns(availabilityToolMock.Object);

        // Act - Turn 1
        var result1 = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Quiero Masaje Infantil para mañana, ¿tienen lugar?");

        // Assert - Turn 1
        // Availability should have been checked because user explicitly asked for it
        availabilityToolMock.Verify(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);

        // Add-ons should NOT have been offered yet (deferred) because we prioritized the explicit availability question
        // How to verify?
        // 1. Check state.AddOnsOffered is still false (or wasn't set to true in this turn)
        // 2. Check response doesn't contain add-on offer logic (harder to test on text).
        // 3. Check TurnActions.AddOnOfferingRequired (internal). We can't access it easily from result.

        // However, if AddOnsOffered was set to true, it would prevent offering in next turn.
        // So checking _state.AddOnsOffered is the key.
        // NOTE: The orchestrator sets AddOnsOffered=true ONLY when it actually offers them.

        // Wait, if "userAskedAvailability" logic kicks in, it skips the "if (!userAskedAvailability ... ShouldOfferAddOns)" block.
        // So AddOnsOffered should remain false.

        // We need to ensure our Mock State Updater updates the property if called.
        // Base mock handles: ApplyConfirmationFlag for "AddOnsOffered".

        _stateUpdaterMock.Verify(m => m.ApplyConfirmationFlag(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), "AddOnsOffered", true, It.IsAny<string>()), Times.Never);
        _state.AddOnsOffered.Should().BeFalse("Add-ons should be deferred when availability is explicitly requested with service selection.");


        // 2. User says "Ok" (Implicitly continuing flow)
        // Now Add-ons SHOULD be offered.

        // Update state to reflect what happened in Turn 1
        _state.Service = "Masaje Infantil";
        _state.DesiredDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        _state.AvailabilityConfirmed = true; // Assume tool/updater did this in real life

        SetupFlowEvaluation(TransactionStage.ExploringServices); // Still exploring/refining
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions(), // No specific intention, just continuing
            ExtractedFields = new List<ExtractedField>()
        });

        // Act - Turn 2
        var result2 = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Ok, perfecto");

        // Assert - Turn 2
        // Now AddOnsOffered should be set to true
        _stateUpdaterMock.Verify(m => m.ApplyConfirmationFlag(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), "AddOnsOffered", true, It.IsAny<string>()), Times.Once);

        // And we should see indications of offering add-ons (e.g. TurnActions.AddOnOfferingRequired was true internaly)
        // Since we can't see internals, the verification of the flag update is the strong signal.
        // Also, CheckAvailability should NOT be called again.
        availabilityToolMock.Verify(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once); // Still Once from before
    }

    [Fact]
    public async Task Should_Show_Reservation_Form_When_Data_Is_Complete()
    {
        // Scenario: Flow reaches ConfirmingBooking.
        // Goal: Verify ConfirmationSummaryPresented flag is set to true.

        // Prep: All data present.
        _state.Service = "Masaje Infantil";
        _state.DesiredDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        _state.CustomerName = "Maria";
        _state.AvailabilityConfirmed = true;
        _state.ReservationConfirmed = false;
        _state.ConfirmationSummaryPresented = false;

        // Flow: ConfirmingBooking
        SetupFlowEvaluation(TransactionStage.ConfirmingBooking);
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions(), // Just flow transition
            ExtractedFields = new List<ExtractedField>()
        });

        // Act
        // User just says something neutral or we just processed the last input that completed the data
        var result = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Ok, gracias");

        // Assert
        // Orchestrator logic: if (ShouldInjectConfirmationSummary(ctx)) -> _stateUpdater.ApplyConfirmationFlag(..., "ConfirmationSummaryPresented", true)
        _stateUpdaterMock.Verify(m => m.ApplyConfirmationFlag(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), "ConfirmationSummaryPresented", true, It.IsAny<string>()), Times.Once);

        // Verify the response is the deterministic summary (starts with specific intro)
        // intro = "¡Gracias, {name}! Ya tengo todos los datos necesarios. Aquí está el resumen de tu reserva:"
        result.Response.Should().Contain("Aquí está el resumen de tu reserva");
    }
}
