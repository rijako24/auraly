namespace MimosBabySpa.Application.DTOs;

public class Value
{
    public string MessagingProduct { get; set; } = string.Empty;
    public List<Message> Messages { get; set; } = new();
    public List<Contact> Contacts { get; set; } = new();
}
