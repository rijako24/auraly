using System.Text;
using System.Text.Json;
using UglyToad.PdfPig; // PdfPig package

using MimosBabySpa.Application.Identity.Interfaces;

namespace MimosBabySpa.Infrastructure.Catalog;

public sealed class CatalogDocumentTextExtractor : ICatalogDocumentTextExtractor
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".csv", ".json", ".md"
    };

    public bool SupportsFileName(string fileName) =>
        Supported.Contains(Path.GetExtension(fileName));

    public async Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ExtractPdf(stream),
            ".json" => await ReadUtf8Async(stream, ct),
            ".csv" or ".txt" or ".md" => await ReadUtf8Async(stream, ct),
            _ => throw new NotSupportedException($"Formato no soportado: {ext}")
        };
    }

    private static string ExtractPdf(Stream stream)
    {
        using var doc = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static async Task<string> ReadUtf8Async(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }
}
