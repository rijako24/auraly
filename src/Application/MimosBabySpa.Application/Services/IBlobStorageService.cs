namespace MimosBabySpa.Application.Services;

public interface IBlobStorageService
{
    Task<string> UploadImageAsync(Guid businessId, Stream imageStream, string fileName);
    Task<string> GetImageUrlAsync(Guid businessId, string fileName);
    Task<bool> ImageExistsAsync(Guid businessId, string fileName);
}
