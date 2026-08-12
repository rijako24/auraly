namespace MimosBabySpa.Application.Services;

/// <summary>
/// Converts an inbound WhatsApp document or image into text that the
/// deterministic agent can inspect. Binary media never reaches prompts
/// outside this bounded extraction step.
/// </summary>
public interface IInboundDocumentTextExtractor
{
    bool Supports(string? fileName, string? mimeType);

    Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName,
        string? mimeType,
        CancellationToken cancellationToken = default);
}
