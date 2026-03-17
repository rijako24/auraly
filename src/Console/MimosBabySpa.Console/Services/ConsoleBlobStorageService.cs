using MimosBabySpa.Application.Services;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Console.Services;

/// <summary>
/// Mock implementation de IBlobStorageService para la aplicación de consola.
/// Retorna valores vacíos ya que no es necesario para probar la funcionalidad de OpenAI.
/// </summary>
public class ConsoleBlobStorageService : IBlobStorageService
{
    private readonly ILogger<ConsoleBlobStorageService> _logger;

    public ConsoleBlobStorageService(ILogger<ConsoleBlobStorageService> logger)
    {
        _logger = logger;
    }

    public Task<string> UploadImageAsync(Guid businessId, Stream imageStream, string fileName)
    {
        _logger.LogDebug("[MOCK] UploadImageAsync llamado para business {BusinessId}: {FileName}", businessId, fileName);
        return Task.FromResult<string>(string.Empty);
    }

    public Task<string> GetImageUrlAsync(Guid businessId, string fileName)
    {
        _logger.LogDebug("[MOCK] GetImageUrlAsync llamado para business {BusinessId}: {FileName}", businessId, fileName);
        return Task.FromResult<string>(string.Empty);
    }

    public Task<bool> ImageExistsAsync(Guid businessId, string fileName)
    {
        _logger.LogDebug("[MOCK] ImageExistsAsync llamado para business {BusinessId}: {FileName}", businessId, fileName);
        return Task.FromResult(false);
    }
}
