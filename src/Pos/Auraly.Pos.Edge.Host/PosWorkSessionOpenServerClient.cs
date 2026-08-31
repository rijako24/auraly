using System.Net.Http.Json;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosWorkSessionOpenServerClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosOperationalScope scope)
{
    public async Task<WorkSessionView> OpenOrResumeAsync(
        PosLocalUserSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/pos/v1/work-sessions/current");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        request.Content = JsonContent.Create(new DeviceOpenWorkSessionRequest(
            session.UserId,
            scope.BusinessId,
            scope.WarehouseId));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkSessionView>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidDataException(
                   "Auraly Server devolvió una sesión de trabajo vacía.");
    }
}
