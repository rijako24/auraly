namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para formatear información adicional de reservas (Notes) de JSON a formato legible
/// </summary>
public interface INotesFormatterService
{
    /// <summary>
    /// Formatea las notas (JSON string) a un formato legible para mostrar al usuario
    /// </summary>
    /// <param name="notes">JSON string con información adicional</param>
    /// <returns>String formateado y legible, o string vacío si notes es null o vacío</returns>
    string FormatNotes(string? notes);
}
