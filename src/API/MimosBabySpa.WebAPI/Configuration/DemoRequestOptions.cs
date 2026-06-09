namespace MimosBabySpa.WebAPI.Configuration;

public sealed class DemoRequestOptions
{
    public const string SectionName = "DemoRequests";

    public string RecipientEmail { get; set; } = string.Empty;
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "AURALY";
}
