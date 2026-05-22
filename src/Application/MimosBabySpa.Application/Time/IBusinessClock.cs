namespace MimosBabySpa.Application.Time;

/// <summary>
/// Reloj del negocio: resuelve "ahora" y "hoy" en la zona horaria configurada.
/// </summary>
public interface IBusinessClock
{
    Task<BusinessClockSnapshot> GetSnapshotAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);
}
