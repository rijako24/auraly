using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface ICatalogDraftParser
{
    Task<IReadOnlyList<CatalogImportServiceLineDto>> ParseAsync(string documentText, CancellationToken ct = default);
}
