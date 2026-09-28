using Xunit;
using Moq;
using FluentAssertions;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Tests.Reliability;

public class SafetyTests : ReliabilityTestBase
{
    [Fact]
    public async Task Should_Prevent_Reservation_Without_Availability()
    {
        // Scenario: User tries to say "confirm" when availability hasn't been checked/confirmed.
        _state.Service = "Masaje";
        // Need to set valid DateOnly/TimeOnly if needed by logic, but missing availability is key
        _state.DesiredDate = DateOnly.FromDateTime(DateTime.Today);
        _state.AvailabilityConfirmed = false;

        // Flow Engine SHOULD evaluate CanCreateReservation = false in this case
        SetupFlowEvaluation(TransactionStage.CollectingInformation, canCreate: false);

        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserConfirmedBooking = true }
        });

        // Mock Tool
        var reservationToolMock = new Mock<IToolHandler>();
        _toolFactoryMock.Setup(f => f.GetTool(ToolType.CreateReservation)).Returns(reservationToolMock.Object);

        var result = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "Sí, confirma");

        // Tool should NOT be called
        reservationToolMock.Verify(t => t.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Include_Guardrails_When_Unverified()
    {
        // Scenario: User asks about availability, but the check was not executed (maybe due to missing info or failure).
        // LLM should be instructed NOT to hallucinate.

        _state.Service = "Masaje";
        // Missing Date
        _state.DesiredDate = null;

        SetupFlowEvaluation(TransactionStage.CollectingInformation, canCheck: false);

        SetupExtraction(new ExtractionOutput
        {
            WasSuccessful = true,
            Intentions = new ExtractionIntentions { UserRequestedAvailability = true },
            ExtractedFields = new List<ExtractedField>()
        });

        // We need to inspect the input to the LLM to verify the guardrail message.
        // We'll capture the LLMRequest passed to SendMessageAsync.
        LLMRequest capturedRequest = null;
        _llmAdapterMock.Setup(m => m.SendMessageAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new LLMResponse { Success = true, Content = "Respuesta segura" });

        var result = await _orchestrator.ProcessMessageAsync(_conversationId, _businessId, _customerPhone, "¿Tienen lugar?");

        // If SendMessageAsync is NOT called (fallback logic triggered), this will be null.
        // Orchestrator GenerateConversationalResponseAsync builds request and calls SendMessageAsync.
        // It catches exceptions.

        if (capturedRequest == null)
        {
             // If LLM wasn't called, maybe it fell back?
             // But we mocked it to return success.
             // Maybe Extraction Failed? No, we set WasSuccessful=true.

             // Check if Orchestrator logged error?
             // Let's assume it should call LLM.
        }

        capturedRequest.Should().NotBeNull();

        // Check if messages contain the Guardrail string
        // The orchestrator logic adds guardrails as a System message.

        bool hasGuardrail = capturedRequest.Messages.Any(m =>
            m.Content != null && (m.Content.Contains("PROHIBIDO mostrar o inventar horarios") || m.Content.Contains("DISPONIBILIDAD NO VERIFICADA")));

        hasGuardrail.Should().BeTrue("Guardrails should be present when availability is unverified but requested");
    }
}
