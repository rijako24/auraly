namespace MimosBabySpa.Application.DTOs;

public class WhatsAppWebhookDto
{
    public string Object { get; set; } = string.Empty;
    public List<Entry> Entry { get; set; } = new();
}

public class Entry
{
    public string Id { get; set; } = string.Empty;
    public List<Change> Changes { get; set; } = new();
}

public class Change
{
    public string Field { get; set; } = string.Empty;
    public Value Value { get; set; } = new();
}

public class Value
{
    public string MessagingProduct { get; set; } = string.Empty;
    public List<Message> Messages { get; set; } = new();
    public List<Contact> Contacts { get; set; } = new();
}

public class Message
{
    public string From { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public TextMessage? Text { get; set; }
    public AudioMessage? Audio { get; set; }
    public VoiceMessage? Voice { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class TextMessage
{
    public string Body { get; set; } = string.Empty;
}

public class AudioMessage
{
    public string Id { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public class VoiceMessage
{
    public string Id { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public class Contact
{
    public Profile Profile { get; set; } = new();
    public string WaId { get; set; } = string.Empty;
}

public class Profile
{
    public string Name { get; set; } = string.Empty;
}
