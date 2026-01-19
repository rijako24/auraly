namespace MimosBabySpa.Application.Services;

public interface IBlobStorageService
{
    Task<string> UploadImageAsync(Stream imageStream, string fileName);
    Task<string> GetImageUrlAsync(string fileName);
    Task<bool> ImageExistsAsync(string fileName);
}
