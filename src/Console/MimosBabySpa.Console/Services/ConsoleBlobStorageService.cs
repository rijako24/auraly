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

    public Task<string> UploadImageAsync(Stream imageStream, string fileName)
    {
        _logger.LogDebug("[MOCK] UploadImageAsync llamado para: {FileName}", fileName);
        return Task.FromResult<string>(string.Empty);
    }

    public Task<string> GetImageUrlAsync(string fileName)
    {
        _logger.LogDebug("[MOCK] GetImageUrlAsync llamado para: {FileName}", fileName);
        // Retornar null para que el código envíe solo texto en lugar de imagen
        return Task.FromResult<string>(string.Empty);
    }

    public Task<bool> ImageExistsAsync(string fileName)
    {
        _logger.LogDebug("[MOCK] ImageExistsAsync llamado para: {FileName}", fileName);
        return Task.FromResult(false);
    }
}
