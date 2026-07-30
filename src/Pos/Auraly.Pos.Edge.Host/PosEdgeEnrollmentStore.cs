using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Organization;
using Auraly.Fiscal.Core;

namespace Auraly.Pos.Edge.Host;

public sealed record LocalPosEnrollmentRequest(
    Guid EnrollmentSessionId,
    string RedemptionCode);

public sealed record LocalPosEnrollmentResult(
    string Status,
    Guid DeviceId,
    string RegisterCode,
    bool RestartRequired);

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
            ["PosEdge:LocationId"] = package.LocationId.ToString("D"),
            ["PosEdge:WarehouseId"] = package.WarehouseId.ToString("D"),
            ["PosEdge:RegisterId"] = package.RegisterId.ToString("D"),
            ["PosEdge:RegisterCode"] = package.RegisterCode,
            ["PosEdge:WarehouseAllowsNegativeStock"] =
                package.WarehouseAllowsNegativeStock.ToString(),
            ["PosEdge:UserId"] = package.InitialUserId.ToString("D"),
            ["PosEdge:UserDisplayName"] = package.InitialUserDisplayName,
            ["PosEdge:SupplierTaxId"] = package.FiscalSeries.SupplierTaxId,
            ["PosEdge:SecretKeyDirectory"] = keyDirectory,
            ["PosEdge:Fiscal:ProtectedTechnicalKey"] =
                PosEdgeProtectedSecret.ProtectTechnicalKey(
                    keyDirectory, package.FiscalSeries.TechnicalKey),
            ["PosEdge:Fiscal:TechnicalKeyVersion"] =
                package.FiscalSeries.TechnicalKeyVersion,
            ["PosEdge:Fiscal:Environment"] =
                ((FiscalEnvironment)package.FiscalSeries.Environment).ToString(),
            ["PosEdge:Fiscal:QrValidationUrl"] =
                package.FiscalSeries.QrValidationUrl,
            ["PosEdge:Fiscal:SeriesId"] =
                package.FiscalSeries.SeriesId.ToString("D"),
            ["PosEdge:Fiscal:FiscalAuthorizationId"] =
                package.FiscalSeries.FiscalAuthorizationId.ToString("D"),
            ["PosEdge:Fiscal:Prefix"] = package.FiscalSeries.Prefix,
            ["PosEdge:Fiscal:AuthorizationNumber"] =
                package.FiscalSeries.AuthorizationNumber,
            ["PosEdge:Fiscal:RangeStart"] =
                package.FiscalSeries.RangeStart.ToString(),
            ["PosEdge:Fiscal:RangeEnd"] =
                package.FiscalSeries.RangeEnd.ToString(),
            ["PosEdge:Fiscal:ValidUntil"] =
                package.FiscalSeries.ValidUntil.ToString("yyyy-MM-dd"),
            ["PosEdge:Documents:SalesInvoice:SeriesId"] =
                package.DocumentSeries.SeriesId.ToString("D"),
            ["PosEdge:Documents:SalesInvoice:Padding"] =
                package.DocumentSeries.Padding.ToString(),
            ["PosEdge:Documents:SalesInvoice:RangeStart"] =
                package.DocumentSeries.RangeStart.ToString(),
            ["PosEdge:Documents:SalesInvoice:RangeEnd"] =
                package.DocumentSeries.RangeEnd.ToString(),
            ["PosEdge:PaperWidthMillimeters"] = "80",
            ["PosEdge:PrinterMode"] = "BrowserPreview",
            ["PosEdge:ReceiptOutputDirectory"] =
                Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                    "receipts")
        };
        for (var index = 0; index < package.Permissions.Count; index++)
            values[$"PosEdge:Permissions:{index}"] = package.Permissions[index];
        return values;
    }
}

public sealed class PosEdgeEnrollmentClient(
    HttpClient httpClient,
    PosEdgeEnrollmentStore store)
{
    public async Task<LocalPosEnrollmentResult> RedeemAsync(
        LocalPosEnrollmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var installationId = $"{Environment.MachineName}:{Environment.UserName}";
        using var response = await httpClient.PostAsJsonAsync(
            "api/pos/v1/enrollments/redeem",
            new RedeemPosEnrollmentRequest(
                request.EnrollmentSessionId,
                request.RedemptionCode,
                installationId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var package = await response.Content.ReadFromJsonAsync<PosEnrollmentPackage>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException(
                "The Auraly server returned an empty enrollment package.");
        store.Save(package);
        return new LocalPosEnrollmentResult(
            "Enrolled",
            package.DeviceId,
            package.RegisterCode,
            RestartRequired: true);
    }
}
