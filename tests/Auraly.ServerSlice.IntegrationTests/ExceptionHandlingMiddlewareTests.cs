using System.Text.Json;
using Auraly.Api.Middleware;
using Auraly.Platform.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Unexpected_exceptions_never_expose_stack_trace_or_internal_detail()
    {
        const string sensitiveDetail = "internal-database-detail";
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException(sensitiveDetail),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/auth/me";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, new TestCorrelationIdProvider("test-correlation"));

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var root = payload.RootElement;

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("Error interno del servidor.", root.GetProperty("title").GetString());
        Assert.Equal("test-correlation", root.GetProperty("correlationId").GetString());
        Assert.False(root.TryGetProperty("detail", out _));
        Assert.DoesNotContain(sensitiveDetail, root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), root.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class TestCorrelationIdProvider(string correlationId) : ICorrelationIdProvider
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string value) => CorrelationId = value;
    }
}
