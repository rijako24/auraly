using Xunit;
using Moq;
using FluentAssertions;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;

namespace MimosBabySpa.Tests.Reliability;

public class EdgeCaseTests : ReliabilityTestBase
{
    [Fact]
    public async Task Should_Handle_Service_Switch()
    {
        // Start with a state where a service is already selected
        _state.Service = "Masaje Infantil";
        SetupFlowEvaluation(TransactionStage.ExploringServices);

        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            ExtractedFields = new List<ExtractedField>
            {
                new ExtractedField { FieldName = "Service", Value = "Hidroterapia", Confidence = 0.95f }
            }
        });

        // Ensure we simulate ApplyField updating the state
        _stateUpdaterMock.Setup(m => m.ApplyField(It.IsAny<MimosBabySpa.Domain.Models.ConversationState>(), "Service", "Hidroterapia"))
            .Callback<MimosBabySpa.Domain.Models.ConversationState, string, string>((s, f, v) => s.Service = v)
            .Returns(new ApplyFieldResult(true, "Applied"));


        var result = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Mejor quiero Hidroterapia");

        // Assert state updated
        _state.Service.Should().Be("Hidroterapia");
    }

    [Fact]
    public async Task Should_Handle_Unavailable_Slot()
    {
        // User asks for a date, but availability check fails
        _state.Service = "Masaje";
        // Need to ensure DesiredDate is set so ShouldRecheckAvailability or logic passes if needed
        _state.DesiredDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

        SetupFlowEvaluation(TransactionStage.ExploringServices, canCheck: true);

        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserRequestedAvailability = true },
            ExtractedFields = new List<ExtractedField>()
        });

        // Mock Unavailable
        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CheckAvailability))
            .Returns(new Mock<IToolHandler>().Object);
        var availabilityToolMock = new Mock<IToolHandler>();
        availabilityToolMock.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult
            {
                Success = true, // Tool execution itself is successful (it ran)
                Message = "No hay cupo",
                StateModified = false
            });

        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CheckAvailability)).Returns(availabilityToolMock.Object);

        var result = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "¿Hay cupo?");

        _state.AvailabilityConfirmed.Should().BeFalse();

        availabilityToolMock.Verify(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_Handle_Cancellation()
    {
        // Setup state with some data
        _state.Service = "Masaje";
        _state.DesiredDate = DateOnly.FromDateTime(DateTime.Today);
        _state.CurrentStage = TransactionStage.ConfirmingBooking;

        SetupFlowEvaluation(TransactionStage.ConfirmingBooking);

        // User says "Cancel"
        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserWantsToCancel = true }
        });

        // Ensure we are matching the correct signature for ResetTransactionalFlags
        // The mock in Base handles Any, so it should catch it.
        // But orchestrator logic calls it.

        var result = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Cancelar todo");

        _stateUpdaterMock.Verify(m => m.ResetTransactionalFlags(_state), Times.Once);
    }
}
