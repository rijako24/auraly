using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;

namespace Auraly.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception [CorrelationId: {CorrelationId}]",
                correlationIdProvider.CorrelationId);

            await HandleExceptionAsync(context, ex, correlationIdProvider.CorrelationId, _environment.IsEnvironment("Testing"));
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId, bool exposeDetail)
    {
        var (statusCode, title) = exception switch
        {
            UnauthorizedAccessException e => (StatusCodes.Status401Unauthorized, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
            DomainValidationException e => (StatusCodes.Status400BadRequest, e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor.")
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Instance = context.Request.Path,
            Extensions = { ["correlationId"] = correlationId }
        };

        if (exception is DomainValidationException validationEx)
        {
            problemDetails.Extensions["errors"] = validationEx.Errors;
        }
        if (exposeDetail) problemDetails.Detail = exception.ToString();

        return context.Response.WriteAsJsonAsync(problemDetails);
    }
}
