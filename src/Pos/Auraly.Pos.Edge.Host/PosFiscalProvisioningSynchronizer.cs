using System.Net.Http.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Fiscal.Core;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

internal sealed class PosFiscalProvisioningSynchronizer(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosEdgeRuntimeContext runtime,
    PosEdgeSaleStore sales,
    PosFiscalRuntimeSettings settings,
    PosEdgeEnrollmentStore enrollmentStore)
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var cursor = await sales.GetFiscalCursorStateAsync(
            new DeviceId(credentials.DeviceId), cancellationToken);
        var cursorQuery = cursor is null
            ? string.Empty
            : $"&currentSeriesId={cursor.SeriesId:D}&nextConsecutive={cursor.NextConsecutive}";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/fiscal/provisioning-bundle?businessId={runtime.BusinessId.Value:D}{cursorQuery}");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return;
        response.EnsureSuccessStatusCode();
        var provisions = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<PosFiscalSeriesProvisioning>>(cancellationToken)
            ?? throw new InvalidDataException(
                "El servidor devolvió una asignación fiscal vacía.");
        if (provisions.Count != 1)
            throw new InvalidDataException(
                "El servidor debe devolver exactamente una resolución DIAN por equipo.");
        var active = provisions[0];
        var activeEdge = new PosEdgeSeriesProvision(
            active.SeriesId, new DeviceId(credentials.DeviceId),
            active.Prefix, active.AuthorizationNumber,
            active.RangeStart, active.RangeEnd, active.ValidUntil,
            active.FiscalAuthorizationId, active.ValidFrom,
            active.AuthorizationRangeStart, active.AuthorizationRangeEnd,
            active.ExpirationWarningDays,
            active.RemainingNumberWarningThreshold);
        await sales.ProvisionSeriesAsync(activeEdge, cancellationToken);
        settings.Replace(new PosFiscalHostSettings(
            active.SupplierTaxId,
            new FiscalTechnicalKey(active.TechnicalKey, active.TechnicalKeyVersion),
            (FiscalEnvironment)active.Environment,
            active.QrValidationUrl,
            activeEdge));

        if (enrollmentStore.Load() is { } package)
        {
            enrollmentStore.Save(package with
            {
                FiscalSeries = new PosEnrollmentFiscalSeries(
                    active.SeriesId, active.FiscalAuthorizationId,
                    active.Prefix, active.AuthorizationNumber,
                    active.RangeStart, active.RangeEnd, active.ValidUntil,
                    active.Environment, active.SupplierTaxId, active.TechnicalKey,
                    active.TechnicalKeyVersion, active.QrValidationUrl,
                    active.ValidFrom, active.AuthorizationRangeStart,
                    active.AuthorizationRangeEnd,
                    active.ExpirationWarningDays,
                    active.RemainingNumberWarningThreshold)
            });
        }
    }
}
