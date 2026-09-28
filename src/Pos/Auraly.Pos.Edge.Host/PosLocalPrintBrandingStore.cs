namespace Auraly.Pos.Edge.Host;

// The Edge host owns the installed application's print image. A tenant logo is
// loaded from disk at most once per process and never read during each invoice.
public sealed class PosLocalPrintBrandingStore(
    string dataDirectory, Guid? preparedTenantId = null)
{
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private readonly Dictionary<Guid, string?> images = new();
    private readonly object gate = new();
    private readonly string directory = Path.Combine(dataDirectory, "print-branding");

    public bool AllowsTenant(Guid tenantId) =>
        preparedTenantId is null || preparedTenantId.Value == tenantId;

    public void Prepare(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("El tenant del logo no es válido.");
        lock (gate)
        {
            if (images.ContainsKey(tenantId)) return;
            var path = Path.Combine(directory, $"{tenantId:N}.logo");
            images[tenantId] = File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    public void Save(Guid tenantId, string? dataUri)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("El tenant del logo no es válido.");
        byte[]? bytes = null;
        string? mediaType = null;
        if (!string.IsNullOrWhiteSpace(dataUri))
        {
            var separator = dataUri.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
            if (separator < 0 || !dataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("El logo debe ser una imagen local en base64.");
            mediaType = dataUri[5..separator].ToLowerInvariant();
            if (mediaType is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif"))
                throw new ArgumentException("El formato del logo no es compatible.");
            if (dataUri.Length - separator - 8 > (MaxImageBytes * 4 / 3) + 8)
                throw new ArgumentException("El logo supera el tamaño permitido.");
            try { bytes = Convert.FromBase64String(dataUri[(separator + 8)..]); }
            catch (FormatException exception)
            { throw new ArgumentException("El logo no contiene una imagen válida.", exception); }
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
                throw new ArgumentException("El logo supera el tamaño permitido.");
        }

        lock (gate)
        {
            Prepare(tenantId);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{tenantId:N}.logo");
            if (bytes is null)
            {
                if (File.Exists(path)) File.Delete(path);
                images[tenantId] = null;
                return;
            }
            var stored = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
            if (string.Equals(images[tenantId], stored, StringComparison.Ordinal))
                return;
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, stored);
            File.Move(temporary, path, overwrite: true);
            images[tenantId] = stored;
        }
    }

    public string? Get(Guid tenantId)
    {
        if (tenantId == Guid.Empty) return null;
        lock (gate)
        {
            return images.GetValueOrDefault(tenantId);
        }
    }
}

public sealed record PosLocalPrintBrandingRequest(Guid TenantId, string? LogoDataUri);
public sealed record PosLocalPrintBrandingPrepareRequest(Guid TenantId);
