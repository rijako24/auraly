using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface ICatalogDraftParser
{
    Task<IReadOnlyList<CatalogImportServiceLineDto>> ParseAsync(string documentText, CancellationToken ct = default);
}
