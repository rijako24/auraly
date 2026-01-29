namespace MimosBabySpa.Application.DTOs;

public class Contact
{
    public Profile Profile { get; set; } = new();
    public string WaId { get; set; } = string.Empty;
}
