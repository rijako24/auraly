using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Política de reserva configurable para tests de integración.
/// Por defecto sin anticipo para validar el flujo de reserva; los unit tests cubren el guard de pago.
/// </summary>
internal sealed class FakeBookingPolicyProvider : IBookingPolicyProvider
{
    public static bool DepositRequired { get; set; }

    public Task<BookingPolicyParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BookingPolicyParams
        {
            DepositRequired = DepositRequired,
            DepositPercentage = DepositRequired ? 50 : 0,
            Currency = "COP"
        });
}
