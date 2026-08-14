using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Worker.Functions;

/// <summary>
/// GET /api/release?conv={conversationId}&t={token}
/// El agente pulsa el link de la notificación de escalado para devolver la conversación al bot.
/// Seguridad: token HMAC firmado con Release:TokenSecret.
/// </summary>
public class ReleaseConversationFunction
{
    private readonly IReleaseLinkService _releaseLinkService;
    private readonly IConversationReleaseService _releaseService;
    private readonly ILogger<ReleaseConversationFunction> _logger;

    public ReleaseConversationFunction(
        IReleaseLinkService releaseLinkService,
        IConversationReleaseService releaseService,
        ILogger<ReleaseConversationFunction> logger)
    {
        _releaseLinkService = releaseLinkService;
        _releaseService = releaseService;
        _logger = logger;
    }

    [Function("ReleaseConversation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "release")] HttpRequestData req)
    {
        var query = QueryHelpers.ParseQuery(req.Url.Query);
        if (!query.TryGetValue("conv", out var convValue) || !query.TryGetValue("t", out var tokenValue))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync(ErrorHtml("Parámetros inválidos. Use conv y t."));
            bad.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return bad;
        }

        if (!Guid.TryParse(convValue, out var conversationId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync(ErrorHtml("ID de conversación inválido."));
            bad.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return bad;
        }

        if (!_releaseLinkService.ValidateToken(conversationId, tokenValue!))
        {
            _logger.LogWarning("Release: token inválido para Conv={ConvId}", conversationId);
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteStringAsync(ErrorHtml("Enlace no válido o expirado."));
            forbidden.Headers.Add("Content-Type", "text/html; charset=utf-8");
            return forbidden;
        }

        var result = await _releaseService.ReleaseToBotAsync(conversationId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        var html = result switch
        {
            ReleaseResult.Released => SuccessHtml(),
            ReleaseResult.AlreadyWithBot => SuccessHtml(),
            ReleaseResult.NotFound => ErrorHtml("Conversación no encontrada."),
            _ => throw new NotImplementedException()
        };
        await response.WriteStringAsync(html);
        return response;
    }

    private static string SuccessHtml() =>
        """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Listo</title></head><body style="font-family:sans-serif;margin:2em;text-align:center">
        <h2>✅ Listo</h2>
        <p>El bot retomará la conversación cuando el cliente escriba de nuevo.</p>
        </body></html>
        """;

    private static string ErrorHtml(string msg) =>
        $"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Error</title></head><body style="font-family:sans-serif;margin:2em;text-align:center">
        <h2>❌ {msg}</h2>
        </body></html>
        """;
}
