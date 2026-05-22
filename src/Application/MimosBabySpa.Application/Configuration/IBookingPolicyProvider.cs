namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proveedor de política de reserva y pago por negocio.
/// Fuente única: BusinessConfiguration (Key=BookingPolicy).
/// </summary>
public interface IBookingPolicyProvider
{
    /// <summary>
    /// Obtiene la política de reserva del negocio.
    /// Retorna <see cref="BookingPolicyParams.Default"/> si no existe configuración o el JSON es inválido.
    /// </summary>
    Task<BookingPolicyParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default);
}
