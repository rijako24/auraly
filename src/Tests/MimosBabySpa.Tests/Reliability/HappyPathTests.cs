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
}
