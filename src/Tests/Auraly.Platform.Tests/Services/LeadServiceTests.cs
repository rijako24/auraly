using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public class LeadServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ILeadRepository> _mockLeadRepository;
    private readonly Mock<ILogger<LeadService>> _mockLogger;
    private readonly LeadService _service;

    public LeadServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockLeadRepository = new Mock<ILeadRepository>();
        _mockLogger = new Mock<ILogger<LeadService>>();

        _mockUnitOfWork.Setup(u => u.Leads).Returns(_mockLeadRepository.Object);

        _service = new LeadService(_mockUnitOfWork.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetOrCreateLeadAsync_WhenLeadExists_ShouldReturnExistingLead()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";
        var existingLead = new Lead
        {
            LeadId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            Status = "New",
            Timestamp = DateTime.UtcNow
        };

        _mockLeadRepository
            .Setup(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber))
            .ReturnsAsync(existingLead);

        // Act
        var result = await _service.GetOrCreateLeadAsync(businessId, userNumber);

        // Assert
        result.Should().NotBeNull();
        result.LeadId.Should().Be(existingLead.LeadId);
        result.UserNumber.Should().Be(userNumber);
        
        _mockLeadRepository.Verify(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber), Times.Once);
        _mockLeadRepository.Verify(x => x.CreateAsync(It.IsAny<Lead>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateLeadAsync_WhenLeadDoesNotExist_ShouldCreateNewLead()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";
        var customerName = "Juan Pérez";

        _mockLeadRepository
            .Setup(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber))
            .ReturnsAsync((Lead?)null);

        var newLead = new Lead
        {
            LeadId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            CustomerName = customerName,
            Status = "New",
            Timestamp = DateTime.UtcNow
        };

        _mockLeadRepository
            .Setup(x => x.CreateAsync(It.IsAny<Lead>()))
            .ReturnsAsync(newLead);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.GetOrCreateLeadAsync(businessId, userNumber, customerName);

        // Assert
        result.Should().NotBeNull();
        result.UserNumber.Should().Be(userNumber);
        result.CustomerName.Should().Be(customerName);
        result.Status.Should().Be("New");

        _mockLeadRepository.Verify(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber), Times.Once);
        _mockLeadRepository.Verify(x => x.CreateAsync(It.Is<Lead>(l =>
            l.BusinessId == businessId &&
            l.UserNumber == userNumber &&
            l.CustomerName == customerName &&
            l.Status == "New")), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateLeadAsync_WhenLeadExistsButNameIsEmpty_ShouldUpdateName()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var userNumber = "1234567890";
        var customerName = "Juan Pérez";
        var existingLead = new Lead
        {
            LeadId = Guid.NewGuid(),
            BusinessId = businessId,
            UserNumber = userNumber,
            CustomerName = null,
            Status = "New"
        };

        _mockLeadRepository
            .Setup(x => x.GetByBusinessIdAndUserNumberAsync(businessId, userNumber))
            .ReturnsAsync(existingLead);

        _mockLeadRepository
            .Setup(x => x.UpdateAsync(It.IsAny<Lead>()))
            .ReturnsAsync(existingLead);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.GetOrCreateLeadAsync(businessId, userNumber, customerName);

        // Assert
        result.Should().NotBeNull();
        result.CustomerName.Should().Be(customerName);
        
        _mockLeadRepository.Verify(x => x.UpdateAsync(It.Is<Lead>(l => l.CustomerName == customerName)), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateLeadAsync_WithValidData_ShouldUpdateLead()
    {
        // Arrange
        var leadId = Guid.NewGuid();
        var existingLead = new Lead
        {
            LeadId = leadId,
            UserNumber = "1234567890",
            Status = "New"
        };

        _mockLeadRepository
            .Setup(x => x.GetByIdAsync(leadId))
            .ReturnsAsync(existingLead);

        _mockLeadRepository
            .Setup(x => x.UpdateAsync(It.IsAny<Lead>()))
            .ReturnsAsync(existingLead);

        _mockUnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _service.UpdateLeadAsync(leadId, status: "Contacted", notes: "Test notes");

        // Assert
        _mockLeadRepository.Verify(x => x.GetByIdAsync(leadId), Times.Once);
        _mockLeadRepository.Verify(x => x.UpdateAsync(It.Is<Lead>(l =>
            l.Status == "Contacted" &&
            l.Notes == "Test notes")), Times.Once);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateLeadAsync_WhenLeadNotFound_ShouldNotUpdate()
    {
        // Arrange
        var leadId = Guid.NewGuid();

        _mockLeadRepository
            .Setup(x => x.GetByIdAsync(leadId))
            .ReturnsAsync((Lead?)null);

        // Act
        await _service.UpdateLeadAsync(leadId, status: "Contacted");

        // Assert
        _mockLeadRepository.Verify(x => x.GetByIdAsync(leadId), Times.Once);
        _mockLeadRepository.Verify(x => x.UpdateAsync(It.IsAny<Lead>()), Times.Never);
        _mockUnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
