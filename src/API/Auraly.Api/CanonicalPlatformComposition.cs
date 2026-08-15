using Auraly.Api.Controllers;
using Auraly.Api.Middleware;

namespace Auraly.Api;

public static class CanonicalPlatformComposition
{
    public static void AddAuralyPlatformApi(
        this WebApplicationBuilder builder,
        bool configureAuthentication = true,
        bool configureExternalCustomerMessaging = true)
    {
        builder.AddPlatformApi(
            configureAuthentication,
            configureExternalCustomerMessaging);
    }

    public static void UseAuralyPlatformBeforeAuthentication(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Auraly API v1");
            c.RoutePrefix = "swagger";
        });
        app.UseCors("WebApp");
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
    }

    public static void UseAuralyExecutionContext(this WebApplication app) =>
        app.UseMiddleware<ExecutionContextMiddleware>();

    public static Task SeedAuralyPlatformPermissionsAsync(this WebApplication app) =>
        app.SeedPlatformPermissionsAsync();
}