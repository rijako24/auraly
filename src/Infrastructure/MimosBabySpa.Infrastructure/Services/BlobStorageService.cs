using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Infrastructure.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        string containerName,
        ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _containerName = containerName;
        _logger = logger;
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string fileName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobClient = containerClient.GetBlobClient(fileName);
            await blobClient.UploadAsync(imageStream, overwrite: true);

            var url = blobClient.Uri.ToString();
            _logger.LogInformation("Imagen subida: {FileName}", fileName);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al subir imagen {FileName}", fileName);
            throw;
        }
    }

    public async Task<string> GetImageUrlAsync(string fileName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(fileName);
            
            if (await blobClient.ExistsAsync())
            {
                return blobClient.Uri.ToString();
            }

            _logger.LogWarning("Imagen no encontrada: {FileName}", fileName);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener URL de imagen {FileName}", fileName);
            return string.Empty;
        }
    }

    public async Task<bool> ImageExistsAsync(string fileName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(fileName);
            return await blobClient.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar existencia de imagen {FileName}", fileName);
            return false;
        }
    }
}
