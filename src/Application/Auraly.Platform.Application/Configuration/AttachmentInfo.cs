namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Proyección de adjunto para la capa de aplicación.
/// </summary>
public class AttachmentInfo
{
    public Guid AttachmentId { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "document";
    public string? Filename { get; set; }
}
