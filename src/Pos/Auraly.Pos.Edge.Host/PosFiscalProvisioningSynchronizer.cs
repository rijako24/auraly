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
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/fiscal/provisioning?businessId={runtime.BusinessId.Value:D}");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return;
        response.EnsureSuccessStatusCode();
        var provision = await response.Content
            .ReadFromJsonAsync<PosFiscalSeriesProvisioning>(cancellationToken)
            ?? throw new InvalidDataException(
                "El servidor devolvió una asignación fiscal vacía.");
        var edgeProvision = new PosEdgeSeriesProvision(
            provision.SeriesId,
            new DeviceId(credentials.DeviceId),
            provision.Prefix,
            provision.AuthorizationNumber,
            provision.RangeStart,
            provision.RangeEnd,
            provision.ValidUntil,
            provision.FiscalAuthorizationId,
            provision.ValidFrom);
        await sales.ProvisionSeriesAsync(edgeProvision, cancellationToken);
        settings.Replace(new PosFiscalHostSettings(
            provision.SupplierTaxId,
            new FiscalTechnicalKey(provision.TechnicalKey, provision.TechnicalKeyVersion),
            (FiscalEnvironment)provision.Environment,
            provision.QrValidationUrl,
            edgeProvision));

        if (enrollmentStore.Load() is { } package)
        {
            enrollmentStore.Save(package with
            {
                FiscalSeries = new PosEnrollmentFiscalSeries(
                    provision.SeriesId,
                    provision.FiscalAuthorizationId,
                    provision.Prefix,
                    provision.AuthorizationNumber,
                    provision.RangeStart,
                    provision.RangeEnd,
                    provision.ValidUntil,
                    provision.Environment,
                    provision.SupplierTaxId,
                    provision.TechnicalKey,
                    provision.TechnicalKeyVersion,
                    provision.QrValidationUrl,
                    provision.ValidFrom)
            });
        }
    }
}
