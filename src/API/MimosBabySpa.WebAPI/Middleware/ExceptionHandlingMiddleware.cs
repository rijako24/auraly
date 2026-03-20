using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;

namespace MimosBabySpa.WebAPI.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
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

            await HandleExceptionAsync(context, ex, correlationIdProvider.CorrelationId, _environment);
        }
    }

    private static Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        string correlationId,
        IHostEnvironment environment)
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
            problemDetails.Extensions["errors"] = validationEx.Errors;

        if (environment.IsDevelopment() && statusCode == StatusCodes.Status500InternalServerError)
        {
            problemDetails.Detail = BuildDevelopmentErrorDetail(exception);
            if (TryFindSqlException(exception, out var sqlEx))
                problemDetails.Extensions["sqlErrorNumber"] = sqlEx.Number;
        }

        return context.Response.WriteAsJsonAsync(problemDetails);
    }

    /// <summary>
    /// Expone cadena de mensajes (incl. internas) solo en Development para diagnosticar 500 genéricos (p. ej. SqlException).
    /// </summary>
    private static string BuildDevelopmentErrorDetail(Exception exception)
    {
        var parts = new List<string> { exception.GetType().Name + ": " + exception.Message };
        for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
            parts.Add(inner.GetType().Name + ": " + inner.Message);
        return string.Join(" → ", parts);
    }

    private static bool TryFindSqlException(Exception exception, out SqlException sqlException)
    {
        for (var ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is SqlException se)
            {
                sqlException = se;
                return true;
            }
        }

        sqlException = null!;
        return false;
    }
}
