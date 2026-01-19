namespace MimosBabySpa.Application.DTOs;

public class IntentAndContextResult
{
    public string Intent { get; set; } = string.Empty;
    public List<string> Context { get; set; } = new List<string>(); // Lista de strings con el contexto extraído
}
