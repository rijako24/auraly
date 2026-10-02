using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlDianDocumentQuota
{
    public static async Task<bool> TryReserveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid sourceDocumentId,
        string documentKind,
        DateTimeOffset now,
        CancellationToken cancellationToken) => await TryReserveManyAsync(connection, transaction,
            businessId, [sourceDocumentId], documentKind, now, cancellationToken);

    public static async Task<bool> TryReserveManyAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyList<Guid> sourceDocumentIds, string documentKind, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (sourceDocumentIds.Count is < 1 or > 100 || sourceDocumentIds.Any(id => id == Guid.Empty) ||
            sourceDocumentIds.Distinct().Count() != sourceDocumentIds.Count)
            throw new ArgumentException("Quota reservation requires a bounded set of distinct documents.", nameof(sourceDocumentIds));
        await using var command = new SqlCommand("dbo.TenantDianDocumentQuotaReserve", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", sourceDocumentIds[0]);
        if (sourceDocumentIds.Count > 1)
            command.Parameters.Add("@DocumentIdsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(sourceDocumentIds);
        command.Parameters.AddWithValue("@DocumentKind", documentKind);
        command.Parameters.AddWithValue("@Now", now);
        var reserved = command.Parameters.Add("@Reserved", SqlDbType.Bit);
        reserved.Direction = ParameterDirection.Output;
        var failureReason = command.Parameters.Add("@FailureReason", SqlDbType.NVarChar, 32);
        failureReason.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (reserved.Value is true) return true;
        var reason = failureReason.Value as string ?? throw new InvalidOperationException(
            "La reserva DIAN no informó por qué fue rechazada.");
        if (reason == "QuotaExhausted") return false;
        throw new PosSaleInvalidException(reason switch
        {
            "TenantInactive" => "La empresa o la sede no está activa para emitir documentos.",
            "SubscriptionInactive" => "La suscripción no está activa. Revisa su estado antes de facturar.",
            "PeriodUnavailable" => "El período de documentos DIAN no está disponible. Inténtalo de nuevo o contacta a soporte.",
            _ => throw new InvalidOperationException($"Motivo de rechazo DIAN desconocido: {reason}.")
        });
    }
}
