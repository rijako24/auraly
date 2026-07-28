using Auraly.Api;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Auraly");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Auraly must point to the SQL Server database owned by Auraly.Database.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
builder.Services.AddSingleton(new SqlServerConnectionFactory(connectionString));
builder.Services.AddSingleton<IFiscalTechnicalKeyProvider, ConfigurationFiscalTechnicalKeyProvider>();
builder.Services.AddScoped<IFiscalSnapshotVerifier, FiscalSnapshotVerifier>();
builder.Services.AddScoped<IPosDeviceAuthenticator, SqlPosDeviceAuthenticator>();
builder.Services.AddScoped<IPosSaleServerStore, SqlPosSaleServerStore>();
builder.Services.AddScoped<SqlDocumentProcessingSessionAccessor>();
builder.Services.AddScoped<IDocumentProcessingReceiptStore, SqlDocumentProcessingReceiptStore>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlPosSaleDocumentHandler>();
builder.Services.AddScoped<DocumentProcessingEngine>();
builder.Services.AddScoped<ReceivePosSaleService>();

builder.Services
    .AddAuthentication(PosAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, PosDeviceAuthenticationHandler>(
        PosAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "pos.sales.upload",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(
                PosAuthenticationDefaults.PermissionClaim,
                CommercePermissionCodes.SalesCreate);
        });
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapPost(
        "/api/pos/v1/sales",
        async (
            HttpContext httpContext,
            PosSaleUploadRequest request,
            ReceivePosSaleService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
                var response = await service.ReceiveAsync(
                    httpContext.User.ToPosDeviceIdentity(),
                    idempotencyKey,
                    request,
                    cancellationToken);
                return response.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict
                    ? Results.Conflict(response)
                    : Results.Ok(response);
            }
            catch (PosSaleForbiddenException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (PosSaleInvalidException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (PosSaleIdempotencyConflictException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "IdempotencyConflict");
            }
            catch (PosSaleProcessingBusyException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "DocumentProcessingBusy");
            }
        })
    .RequireAuthorization("pos.sales.upload");

app.Run();

public partial class Program;

