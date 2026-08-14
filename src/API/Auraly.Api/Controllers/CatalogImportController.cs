using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/businesses/{businessId:guid}/catalog")]
public class CatalogImportController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly ICatalogImportAdminService _service;

    public CatalogImportController(ICatalogImportAdminService service) => _service = service;

    [HttpPost("extract")]
    [PermissionAuthorize("catalog.import")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<CatalogImportDraftDto>> Extract(
        Guid businessId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "Archivo vacío." });

        if (file.Length > MaxUploadBytes)
            return BadRequest(new { error = "El archivo supera el límite de 10 MB." });

        await using var stream = file.OpenReadStream();
        var draft = await _service.ExtractFromDocumentAsync(
            User.GetTenantId(),
            businessId,
            stream,
            file.FileName,
            ct);

        return Ok(draft);
    }

    [HttpPost("import")]
    [PermissionAuthorize("catalog.import")]
    public async Task<ActionResult<CatalogImportResultDto>> Import(
        Guid businessId,
        [FromBody] ConfirmCatalogImportRequest request,
        CancellationToken ct) =>
        Ok(await _service.ConfirmImportAsync(User.GetTenantId(), businessId, request, ct));
}
