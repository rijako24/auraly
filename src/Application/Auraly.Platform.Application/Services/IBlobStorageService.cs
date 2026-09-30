namespace Auraly.Platform.Application.Services;

public interface IBlobStorageService
{
    Task<string> UploadImageAsync(Guid businessId, Stream imageStream, string fileName);
    Task<string> UploadTenantImageAsync(Guid tenantId, Stream imageStream, string fileName);
    Task<string> GetImageUrlAsync(Guid businessId, string fileName);
    Task<bool> ImageExistsAsync(Guid businessId, string fileName);
    Task<byte[]> DownloadImageAsync(Guid businessId, string fileName, CancellationToken ct = default);
}
