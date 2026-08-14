using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class AvailabilityServiceTests
{
    [Fact]
    public async Task CheckAvailabilityAsync_WhenPreviousReservationEndsOffGrid_IncludesWindowStartAndLatestFittingStart()
    {
        var businessId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateTime(2026, 7, 3);
        var requestedService = new Service
        {
            BusinessId = businessId,
            ServiceId = serviceId,
            ServiceName = "Corte adulto",
            DurationMinutes = 45,
            IsActive = true
        };
        var employee = new Employee { BusinessId = businessId, EmployeeId = employeeId, Name = "Luis", IsActive = true };
        var reservations = new List<Reservation>
        {
            new()
            {
                BusinessId = businessId,
                ServiceId = serviceId,
                EmployeeId = employeeId,
                ReservationDateTime = date.AddHours(14),
                DurationMinutes = 45,
                Status = ReservationStatus.Confirmed
            }
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        var services = new Mock<IServiceRepository>();
        services.Setup(r => r.GetByBusinessIdAndNameAsync(businessId, "Corte adulto"))
            .ReturnsAsync(requestedService);
        unitOfWork.Setup(u => u.Services).Returns(services.Object);

        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByBusinessIdAndDateRangeAsync(
                businessId,
                date,
                date.AddDays(1).AddMinutes(-1)))
            .ReturnsAsync(reservations);
        unitOfWork.Setup(u => u.Reservations).Returns(reservationRepo.Object);

        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(r => r.GetByBusinessIdAndServiceIdAsync(businessId, serviceId))
            .ReturnsAsync([employee]);
        unitOfWork.Setup(u => u.Employees).Returns(employeeRepo.Object);

        var workingHours = new Mock<IWorkingHoursService>();
        workingHours.Setup(s => s.GetEffectiveWorkingHoursAsync(
                businessId,
                employeeId,
                DateOnly.FromDateTime(date),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TimeBlock { Open = "14:00", Close = "17:00" }]);

        var assignment = new Mock<IEmployeeAssignmentService>();
        assignment.Setup(s => s.FindBestAvailableEmployeeAsync(
                businessId,
                serviceId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                null))
            .ReturnsAsync(employee);

        var clock = new Mock<IBusinessClock>();
        clock.Setup(c => c.GetSnapshotAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessClockSnapshot(
                businessId,
                new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 7, 2),
                TimeZoneInfo.Utc));

        var sut = new AvailabilityService(
            unitOfWork.Object,
            assignment.Object,
            workingHours.Object,
            clock.Object,
            NullLogger<AvailabilityService>.Instance);

        var result = await sut.CheckAvailabilityAsync(
            businessId,
            "Corte adulto",
            date,
            time: null,
            new AvailabilityParams
            {
                SlotIntervalMinutes = 30,
                BufferBetweenAppointmentsMinutes = 0,
                MinimumLeadTimeMinutes = 30,
                RequireEmployee = true
            });

        result.IsAvailable.Should().BeTrue();
        result.AvailableWindows.Should().Equal(new AvailabilityWindow("14:45", "17:00"));
        result.AvailableOptions.Should().Equal(
            new AvailabilityOption("14:45", "15:30"),
            new AvailabilityOption("15:00", "15:45"),
            new AvailabilityOption("15:30", "16:15"),
            new AvailabilityOption("16:00", "16:45"),
            new AvailabilityOption("16:15", "17:00"));
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenTimeDoesNotFitDuration_ReturnsAlternatives()
    {
        var businessId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateTime(2026, 7, 3);
        var requestedService = new Service
        {
            BusinessId = businessId,
            ServiceId = serviceId,
            ServiceName = "Corte adulto",
            DurationMinutes = 45,
            IsActive = true
        };
        var employee = new Employee { BusinessId = businessId, EmployeeId = employeeId, Name = "Luis", IsActive = true };

        var unitOfWork = new Mock<IUnitOfWork>();
        var services = new Mock<IServiceRepository>();
        services.Setup(r => r.GetByBusinessIdAndNameAsync(businessId, "Corte adulto"))
            .ReturnsAsync(requestedService);
        unitOfWork.Setup(u => u.Services).Returns(services.Object);

        var reservationRepo = new Mock<IReservationRepository>();
        reservationRepo.Setup(r => r.GetByBusinessIdAndDateRangeAsync(
                businessId,
                date,
                date.AddDays(1).AddMinutes(-1)))
            .ReturnsAsync([]);
        unitOfWork.Setup(u => u.Reservations).Returns(reservationRepo.Object);

        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(r => r.GetByBusinessIdAndServiceIdAsync(businessId, serviceId))
            .ReturnsAsync([employee]);
        unitOfWork.Setup(u => u.Employees).Returns(employeeRepo.Object);

        var workingHours = new Mock<IWorkingHoursService>();
        workingHours.Setup(s => s.GetEffectiveWorkingHoursAsync(
                businessId,
                employeeId,
                DateOnly.FromDateTime(date),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TimeBlock { Open = "16:00", Close = "17:00" }]);

        var assignment = new Mock<IEmployeeAssignmentService>();
        assignment.Setup(s => s.FindBestAvailableEmployeeAsync(
                businessId,
                serviceId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                null))
            .ReturnsAsync(employee);

        var clock = new Mock<IBusinessClock>();
        clock.Setup(c => c.GetSnapshotAsync(businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessClockSnapshot(
                businessId,
                new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 7, 2),
                TimeZoneInfo.Utc));

        var sut = new AvailabilityService(
            unitOfWork.Object,
            assignment.Object,
            workingHours.Object,
            clock.Object,
            NullLogger<AvailabilityService>.Instance);

        var result = await sut.CheckAvailabilityAsync(
            businessId,
            "Corte adulto",
            date,
            TimeSpan.FromHours(16.5),
            new AvailabilityParams
            {
                SlotIntervalMinutes = 30,
                BufferBetweenAppointmentsMinutes = 0,
                RequireEmployee = true
            });

        result.IsAvailable.Should().BeFalse();
        result.RequestedOption.Should().Be(new AvailabilityOption("16:30", "17:15"));
        result.AvailableOptions.Should().Contain(new AvailabilityOption("16:15", "17:00"));
        result.Option.Should().BeNull();
    }
}