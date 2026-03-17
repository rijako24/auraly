using MimosBabySpa.Application.Common.Interfaces;

namespace MimosBabySpa.WebAPI.Middleware;

public class CorrelationIdMiddleware
{
    private const string Header = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        var correlationId = context.Request.Headers.TryGetValue(Header, out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        correlationIdProvider.Set(correlationId);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
