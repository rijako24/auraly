using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.LLM;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Infrastructure.Catalog;

public sealed class InboundDocumentTextExtractor : IInboundDocumentTextExtractor
{
    private const int MaximumImageBytes = 10 * 1024 * 1024;
    private readonly ICatalogDocumentTextExtractor _documentExtractor;
    private readonly IChatClient _chatClient;

    public InboundDocumentTextExtractor(
        ICatalogDocumentTextExtractor documentExtractor,
        IChatClient chatClient)
    {
        _documentExtractor = documentExtractor;
        _chatClient = chatClient;
    }

    public bool Supports(string? fileName, string? mimeType)
    {
        if (mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return !string.IsNullOrWhiteSpace(fileName)
               && _documentExtractor.SupportsFileName(fileName);
    }

    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName,
        string? mimeType,
        CancellationToken cancellationToken = default)
    {
        if (mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length == 0 || buffer.Length > MaximumImageBytes)
                throw new InvalidDataException("La imagen debe pesar entre 1 byte y 10 MB.");

            var completion = await _chatClient.CompleteAsync(
                [
                    ChatMessage.System(
                        "Transcribe fielmente todo el texto visible. Conserva una linea por producto, "
                        + "incluyendo modelo, condicion, capacidad y precio. No corrijas, completes ni inventes datos."),
                    ChatMessage.UserWithImage(
                        "Extrae la lista de precios de esta imagen.",
                        buffer.ToArray(),
                        mimeType)
                ],
                new ChatCompletionOptions
                {
                    MaxTokens = 3000
                },
                cancellationToken);

            if (!completion.Success || string.IsNullOrWhiteSpace(completion.Content))
                throw new InvalidDataException("No fue posible leer texto de la imagen.");

            return completion.Content.Trim();
        }

        if (string.IsNullOrWhiteSpace(fileName)
            || !_documentExtractor.SupportsFileName(fileName))
        {
            throw new NotSupportedException("El documento recibido no tiene un formato compatible.");
        }

        return (await _documentExtractor.ExtractTextAsync(stream, fileName, cancellationToken)).Trim();
    }
}
