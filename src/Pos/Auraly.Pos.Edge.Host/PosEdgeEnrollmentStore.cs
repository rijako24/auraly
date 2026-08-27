using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Organization;
using Auraly.Fiscal.Core;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record LocalPosEnrollmentRequest(
    Guid EnrollmentSessionId,
    string RedemptionCode);

public sealed record LocalPosEnrollmentResult(
    string Status,
    Guid DeviceId,
    string DeviceSeriesCode,
    bool RestartRequired);

public sealed class PosEnrollmentServerException(
    string message,
    int statusCode,
    string title) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class PosEdgeEnrollmentStore(
    string packagePath,
    string keyDirectory)
{
    public PosEnrollmentPackage? Load()
    {
        if (!File.Exists(packagePath)) return null;
        var protectedPayload = File.ReadAllText(packagePath);
        var json = PosEdgeProtectedSecret.UnprotectEnrollmentPackage(
            keyDirectory, protectedPayload);
        return JsonSerializer.Deserialize<PosEnrollmentPackage>(json)
            ?? throw new InvalidDataException(
                "The protected POS enrollment package is empty.");
    }

    public void Save(PosEnrollmentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var directory = Path.GetDirectoryName(Path.GetFullPath(packagePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(package);
        var protectedPayload = PosEdgeProtectedSecret.ProtectEnrollmentPackage(
            keyDirectory, json);
        var temporaryPath = packagePath + ".new";
        File.WriteAllText(temporaryPath, protectedPayload);
        File.Move(temporaryPath, packagePath, overwrite: true);
    }

    public static IReadOnlyDictionary<string, string?> ToConfiguration(
        PosEnrollmentPackage package,
        string keyDirectory,
        string databasePath)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PosEdge:DeviceId"] = package.DeviceId.ToString("D"),
            ["PosEdge:DeviceSecret"] = package.DeviceSecret,
            ["PosEdge:TenantId"] = package.TenantId.ToString("D"),
            ["PosEdge:BusinessId"] = package.BusinessId.ToString("D"),
            ["PosEdge:WarehouseId"] = package.WarehouseId.ToString("D"),
            ["PosEdge:BusinessName"] = package.BusinessName,
            ["PosEdge:CompanyName"] = package.CompanyName ?? package.BusinessName,
            ["PosEdge:CompanyLogoSource"] = package.CompanyLogoSource,
            ["PosEdge:WarehouseCode"] = package.WarehouseCode,
            ["PosEdge:WarehouseName"] = package.WarehouseName,
            ["PosEdge:WarehouseAllowsNegativeStock"] =
                package.WarehouseAllowsNegativeStock.ToString(),
            ["PosEdge:UserId"] = package.InitialUserId.ToString("D"),
            ["PosEdge:UserDisplayName"] = package.InitialUserDisplayName,
            ["PosEdge:SecretKeyDirectory"] = keyDirectory,
            ["PosEdge:Documents:SalesInvoice:SeriesId"] =
                package.DocumentSeries.SeriesId.ToString("D"),
            ["PosEdge:Documents:SalesInvoice:Prefix"] =
                package.DocumentSeries.Prefix,
            ["PosEdge:Documents:SalesInvoice:SeriesCode"] =
                package.DocumentSeries.SeriesCode,
            ["PosEdge:Documents:SalesInvoice:Padding"] =
                package.DocumentSeries.Padding.ToString(),
            ["PosEdge:Documents:SalesInvoice:RangeStart"] =
                package.DocumentSeries.RangeStart.ToString(),
            ["PosEdge:Documents:SalesInvoice:RangeEnd"] =
                package.DocumentSeries.RangeEnd.ToString(),
            ["PosEdge:Documents:SalesReceipt:SeriesId"] =
                package.ReceiptDocumentSeries.SeriesId.ToString("D"),
            ["PosEdge:Documents:SalesReceipt:Prefix"] =
                package.ReceiptDocumentSeries.Prefix,
            ["PosEdge:Documents:SalesReceipt:SeriesCode"] =
                package.ReceiptDocumentSeries.SeriesCode,
            ["PosEdge:Documents:SalesReceipt:Padding"] =
                package.ReceiptDocumentSeries.Padding.ToString(),
            ["PosEdge:Documents:SalesReceipt:RangeStart"] =
                package.ReceiptDocumentSeries.RangeStart.ToString(),
            ["PosEdge:Documents:SalesReceipt:RangeEnd"] =
                package.ReceiptDocumentSeries.RangeEnd.ToString(),
            ["PosEdge:PaperWidthMillimeters"] = "80",
            ["PosEdge:PrinterMode"] = "BrowserPreview",
            ["PosEdge:ReceiptOutputDirectory"] =
                Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                    "receipts")
        };
        if (package.FiscalSeries is { } fiscal)
        {
            values["PosEdge:SupplierTaxId"] = fiscal.SupplierTaxId;
            values["PosEdge:Fiscal:ProtectedTechnicalKey"] =
                PosEdgeProtectedSecret.ProtectTechnicalKey(
                    keyDirectory, fiscal.TechnicalKey);
            values["PosEdge:Fiscal:TechnicalKeyVersion"] =
                fiscal.TechnicalKeyVersion;
            values["PosEdge:Fiscal:Environment"] =
                ((FiscalEnvironment)fiscal.Environment).ToString();
            values["PosEdge:Fiscal:QrValidationUrl"] = fiscal.QrValidationUrl;
            values["PosEdge:Fiscal:SeriesId"] = fiscal.SeriesId.ToString("D");
            values["PosEdge:Fiscal:FiscalAuthorizationId"] =
                fiscal.FiscalAuthorizationId.ToString("D");
            values["PosEdge:Fiscal:Prefix"] = fiscal.Prefix;
            values["PosEdge:Fiscal:AuthorizationNumber"] =
                fiscal.AuthorizationNumber;
            values["PosEdge:Fiscal:RangeStart"] = fiscal.RangeStart.ToString();
            values["PosEdge:Fiscal:RangeEnd"] = fiscal.RangeEnd.ToString();
            values["PosEdge:Fiscal:ValidUntil"] =
                fiscal.ValidUntil.ToString("yyyy-MM-dd");
            values["PosEdge:Fiscal:ValidFrom"] =
                (fiscal.ValidFrom ?? DateOnly.MinValue).ToString("yyyy-MM-dd");
        }

        foreach (var key in package.OfflineLeaseTrustedPublicKeys ??
                 new Dictionary<string, string>(StringComparer.Ordinal))
            values[$"PosEdge:OfflineLeaseTrust:TrustedPublicKeys:{key.Key}"] = key.Value;

        for (var index = 0; index < package.Permissions.Count; index++)
            values[$"PosEdge:Permissions:{index}"] = package.Permissions[index];
        return values;
    }
}

public sealed class PosEnrollmentSessionCompleter(
    PosEdgeEnrollmentStore enrollments,
    PosLocalIdentityStore identities,
    PosOfflineLeaseStore leases)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<PosLocalUserSession> CompleteAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var package = enrollments.Load()
                ?? throw new PosLocalLoginException(
                    "EnrollmentUnavailable",
                    "Este equipo no tiene un enrolamiento disponible.");
            var access = package.InitialOfflineAccess
                ?? throw new PosLocalLoginException(
                    "EnrollmentSessionConsumed",
                    "La sesión inicial del enrolamiento ya fue utilizada.");
            await identities.ApplyLeaseUserAsync(access.User, cancellationToken);
            var lease = await leases.SaveAsync(access, cancellationToken);
            var session = await identities.LoginFromEnrollmentAsync(
                access.User.UserId,
                lease.Payload.ExpiresAt,
                cancellationToken);
            enrollments.Save(package with { InitialOfflineAccess = null });
            return session;
        }
        finally
        {
            gate.Release();
        }
    }
}

public sealed class PosEdgeEnrollmentClient(
    HttpClient httpClient,
    PosEdgeEnrollmentStore store,
    PosStartupModeStore startupMode,
    PosLocalDeviceIdentityRecovery identityRecovery)
{
    public async Task<LocalPosEnrollmentResult> RedeemAsync(
        LocalPosEnrollmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var installationId = $"{Environment.MachineName}:{Environment.UserName}";
        var existingDeviceId = store.Load()?.DeviceId ??
                               identityRecovery.ReadSingleDeviceId();
        using var response = await httpClient.PostAsJsonAsync(
            "api/pos/v1/enrollments/redeem",
            new RedeemPosEnrollmentRequest(
                request.EnrollmentSessionId,
                request.RedemptionCode,
                installationId,
                existingDeviceId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ReadServerExceptionAsync(response, cancellationToken);
        var package = await response.Content.ReadFromJsonAsync<PosEnrollmentPackage>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException(
                "The Auraly server returned an empty enrollment package.");
        package = await CacheCompanyLogoAsync(package, cancellationToken);
        store.Save(package);
        startupMode.Save(PosStartupModes.Enrolled);
        return new LocalPosEnrollmentResult(
            "Enrolled",
            package.DeviceId,
            package.DocumentSeries.SeriesCode,
            RestartRequired: true);
    }

    private static async Task<PosEnrollmentServerException> ReadServerExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        var title = "PosEnrollmentFailed";
        var detail = statusCode == 409
            ? "Este equipo no pudo activar el respaldo porque existe otra configuración activa."
            : "Auraly no pudo completar la activación del respaldo sin conexión.";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.TryGetProperty("title", out var problemTitle) &&
                    problemTitle.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(problemTitle.GetString()))
                    title = problemTitle.GetString()!;
                if (root.TryGetProperty("detail", out var problemDetail) &&
                    problemDetail.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(problemDetail.GetString()))
                    detail = problemDetail.GetString()!;
            }
            catch (JsonException)
            {
                // The upstream status still determines the safe localized fallback.
            }
        }
        return new PosEnrollmentServerException(detail, statusCode, title);
    }

    private static async Task<PosEnrollmentPackage> CacheCompanyLogoAsync(
        PosEnrollmentPackage package,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package.CompanyLogoSource)) return package;
        if (!Uri.TryCreate(package.CompanyLogoSource, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException(
                "El logo de la empresa no tiene una dirección HTTPS válida.");

        using var client = new HttpClient();
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType) ||
            !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "El archivo configurado como logo de la empresa no es una imagen válida.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return package with
        {
            CompanyLogoSource = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}"
        };
    }
}
