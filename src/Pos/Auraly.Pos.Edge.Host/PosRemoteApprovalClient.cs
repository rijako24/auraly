using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosRemoteApprovalClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosEdgeRuntimeContext runtime)
{
    public async Task<PosApprovalRequestView> CreateAsync(
        PosLocalUserSession user,
        Guid draftId,
        Guid? lineId,
        string permissionResource,
        string contextJson,
        CancellationToken cancellationToken)
    {
        using var request = DeviceRequest(
            HttpMethod.Post,
            "api/pos/v1/approvals/",
            JsonContent.Create(new CreatePosApprovalRequest(
                runtime.BusinessId.Value,
                credentials.DeviceId,
                user.WorkSessionId,
                draftId,
                lineId,
                permissionResource,
                contextJson)));
        request.Headers.Add("X-Auraly-User-Id", user.UserId.ToString("D"));
        request.Headers.Add("X-Auraly-Work-Session-Id", user.WorkSessionId.ToString("D"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await PosRemoteApprovalException.FromAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PosApprovalRequestView>(cancellationToken)
            ?? throw new PosRemoteApprovalException("InvalidApprovalResponse", "El servidor no creó la solicitud remota.");
    }

    public async Task<PosApprovalDeviceReservation> ReserveAsync(
        Guid approvalRequestId,
        PosLocalUserSession user,
        Guid draftId,
        Guid? lineId,
        string permissionResource,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var request = DeviceRequest(
            HttpMethod.Post,
            $"api/pos/v1/approvals/{approvalRequestId:D}/reserve",
            JsonContent.Create(new ReservePosApprovalForDeviceRequest(
                runtime.BusinessId.Value,
                user.UserId,
                user.WorkSessionId,
                draftId,
                lineId,
                permissionResource,
                operationId)));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await PosRemoteApprovalException.FromAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PosApprovalDeviceReservation>(cancellationToken)
            ?? throw new PosRemoteApprovalException("InvalidApprovalResponse", "El servidor no confirmó la aprobación remota.");
    }

    public async Task CompleteAsync(
        Guid approvalRequestId,
        PosLocalUserSession user,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var request = DeviceRequest(
            HttpMethod.Post,
            $"api/pos/v1/approvals/{approvalRequestId:D}/complete",
            JsonContent.Create(new CompletePosApprovalForDeviceRequest(
                runtime.BusinessId.Value,
                user.UserId,
                operationId)));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await PosRemoteApprovalException.FromAsync(response, cancellationToken);
    }

    private HttpRequestMessage DeviceRequest(HttpMethod method, string path, HttpContent content)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        return request;
    }
}

public sealed class PosRemoteApprovalException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static async Task<PosRemoteApprovalException> FromAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(cancellationToken);
        return new PosRemoteApprovalException(
            problem?.Title ?? "RemoteApprovalFailed",
            problem?.Detail ?? "No fue posible validar la aprobación remota.");
    }
}

public sealed record PosSensitiveActionAuthorization(
    PosLocalSensitiveAuthorization? Local,
    Guid? RemoteApprovalRequestId,
    Guid OperationId,
    PosLocalUserSession User);

public sealed class PosSensitiveActionAuthorizer(
    PosLocalIdentityStore local,
    PosRemoteApprovalClient remote)
{
    public async Task<PosSensitiveActionAuthorization> AuthorizeAsync(
        PosLocalUserSession user,
        string permissionResource,
        Guid draftId,
        Guid? lineId,
        string? approvalRequestHeader,
        string? operationHeader,
        string? supervisorSecret,
        CancellationToken cancellationToken)
    {
        if (user.Permissions.Contains(permissionResource) || string.IsNullOrWhiteSpace(approvalRequestHeader))
        {
            var authorization = await local.AuthorizeSensitiveAsync(
                user, permissionResource, draftId, lineId, supervisorSecret, cancellationToken);
            return new PosSensitiveActionAuthorization(authorization, null, Guid.Empty, user);
        }

        if (!Guid.TryParse(approvalRequestHeader, out var approvalRequestId) ||
            !Guid.TryParse(operationHeader, out var operationId) || operationId == Guid.Empty)
            throw new PosLocalApprovalException("InvalidApproval", "La aprobación remota no identifica la operación.");
        await remote.ReserveAsync(
            approvalRequestId, user, draftId, lineId, permissionResource, operationId, cancellationToken);
        return new PosSensitiveActionAuthorization(null, approvalRequestId, operationId, user);
    }

    public async Task CompleteAsync(
        PosSensitiveActionAuthorization authorization,
        CancellationToken cancellationToken)
    {
        if (authorization.Local is not null)
            await local.CompleteSensitiveAsync(authorization.Local, cancellationToken);
        else if (authorization.RemoteApprovalRequestId is Guid approvalRequestId)
            await remote.CompleteAsync(
                approvalRequestId, authorization.User, authorization.OperationId, cancellationToken);
    }
}
