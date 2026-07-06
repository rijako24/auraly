namespace MimosBabySpa.WebAPI.Configuration;

public sealed class DemoRequestOptions
{
    public const string SectionName = "DemoRequests";

    public Guid BusinessId { get; set; }
    public string? BusinessName { get; set; }
    public string? TemplateSequenceName { get; set; }
}
