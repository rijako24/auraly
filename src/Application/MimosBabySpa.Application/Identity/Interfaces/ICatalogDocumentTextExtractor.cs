namespace MimosBabySpa.Application.Identity.Interfaces;

public interface ICatalogDocumentTextExtractor
{
    bool SupportsFileName(string fileName);
    Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken ct = default);
}
