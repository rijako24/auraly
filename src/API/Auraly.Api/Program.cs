using Auraly.Api;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Catalog;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Fiscal;
using Auraly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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
builder.Services.AddScoped<IFiscalDocumentStore, SqlFiscalDocumentStore>();
builder.Services.AddScoped<FiscalDocumentService>();
builder.Services.AddScoped<IFiscalGenerationWorkStore, SqlFiscalGenerationWorkStore>();
builder.Services.AddScoped<IFiscalSubmissionWorkStore, SqlFiscalSubmissionWorkStore>();
builder.Services.AddScoped<IDianHabilitationConfigurationProvider,
    SqlDianHabilitationConfigurationProvider>();
builder.Services.AddSingleton<IFiscalSoftwarePinProvider, EnvironmentFiscalSoftwarePinProvider>();
builder.Services.AddSingleton<IFiscalSigningCertificateProvider, WindowsFiscalSigningCertificateProvider>();
builder.Services.AddSingleton<IFiscalXmlSigner, DianXadesSigner>();
builder.Services.AddSingleton<IDianWcfClientFactory, DianWcfClientFactory>();
builder.Services.AddScoped<IDianHabilitationTransport, DianHabilitationTransport>();
builder.Services.AddSingleton<DianInvoiceUblBuilder>();
builder.Services.AddSingleton<DianSchemaValidator>();
builder.Services.AddSingleton<FiscalSubmissionPackageBuilder>();
builder.Services.AddScoped<FiscalGenerationWorker>();
builder.Services.AddScoped<FiscalSubmissionWorker>();
if (builder.Configuration.GetValue("Auraly:Fiscal:Worker:Enabled", true))
{
    builder.Services.AddHostedService<FiscalGenerationHostedService>();
    builder.Services.AddHostedService<FiscalSubmissionHostedService>();
}
builder.Services.AddScoped<IPosDeviceAuthenticator, SqlPosDeviceAuthenticator>();
builder.Services.AddScoped<IPosSaleServerStore, SqlPosSaleServerStore>();builder.Services.AddScoped<SqlDocumentProcessingSessionAccessor>();
builder.Services.AddScoped<IDocumentProcessingReceiptStore, SqlDocumentProcessingReceiptStore>();
builder.Services.AddScoped<IConfirmedDocumentHandler, SqlPosSaleDocumentHandler>();
builder.Services.AddScoped<DocumentProcessingEngine>();
builder.Services.AddScoped<ReceivePosSaleService>();
builder.Services.AddScoped<ICatalogStore, SqlCatalogStore>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<PosCatalogService>();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

var jwtIssuer = builder.Configuration["Authentication:Jwt:Issuer"];
var jwtAudience = builder.Configuration["Authentication:Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Authentication:Jwt:SigningKey"];
var validationKey = string.IsNullOrWhiteSpace(jwtSigningKey)
    ? RandomNumberGenerator.GetBytes(32)
    : Encoding.UTF8.GetBytes(jwtSigningKey);

builder.Services
    .AddAuthentication(PosAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, PosDeviceAuthenticationHandler>(
        PosAuthenticationDefaults.Scheme,
        _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
            ValidIssuer = jwtIssuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(validationKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("fiscal.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });    options.AddPolicy("catalog.user", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
    options.AddPolicy("pos.catalog.sync", policy =>
    {
        policy.AuthenticationSchemes.Add(PosAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(PosAuthenticationDefaults.PermissionClaim, CatalogPermissionCodes.Sync);
    });
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
app.UseResponseCompression();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapCatalogApi();
app.MapFiscalApi();
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

