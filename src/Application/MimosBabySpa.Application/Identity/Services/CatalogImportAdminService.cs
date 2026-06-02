using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
namespace MimosBabySpa.Application.Identity.Services;

public sealed class CatalogImportAdminService : ICatalogImportAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICatalogDocumentTextExtractor _textExtractor;
    private readonly ICatalogDraftParser _draftParser;
    private readonly IAuditService _auditService;
    private readonly ILogger<CatalogImportAdminService> _logger;

    public CatalogImportAdminService(
        IUnitOfWork unitOfWork,
        ICatalogDocumentTextExtractor textExtractor,
        ICatalogDraftParser draftParser,
        IAuditService auditService,
        ILogger<CatalogImportAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _textExtractor = textExtractor;
        _draftParser = draftParser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<CatalogImportDraftDto> ExtractFromDocumentAsync(
        Guid tenantId,
        Guid businessId,
        Stream fileStream,
        string fileName,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        if (!_textExtractor.SupportsFileName(fileName))
            throw new DomainValidationException("file", "Formato no soportado. Use PDF, TXT, CSV o JSON.");

        var text = await _textExtractor.ExtractTextAsync(fileStream, fileName, ct);
        var services = await _draftParser.ParseAsync(text, ct);

        var preview = text.Length > 500 ? text[..500] + "…" : text;

        return new CatalogImportDraftDto
        {
            SourceFileName = fileName,
            ExtractedTextPreview = preview,
            Services = services
        };
    }

    public async Task<CatalogImportResultDto> ConfirmImportAsync(
        Guid tenantId,
        Guid businessId,
        ConfirmCatalogImportRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var warnings = new List<string>();
        var categoriesCreated = 0;
        var servicesCreated = 0;
        var servicesSkipped = 0;

        var categoryCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var existingCategories = await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId);
        foreach (var cat in existingCategories)
            categoryCache[cat.Name] = cat.ServiceCategoryId;

        var displayOrder = existingCategories.Any()
            ? existingCategories.Max(c => c.DisplayOrder) + 1
            : 0;

        foreach (var line in request.Services.Where(s => s.Selected))
        {
            if (string.IsNullOrWhiteSpace(line.ServiceName))
                continue;

            if (request.SkipExistingByName)
            {
                var existing = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
                    businessId, line.ServiceName);
                if (existing is not null)
                {
                    servicesSkipped++;
                    continue;
                }
            }

            if (!categoryCache.TryGetValue(line.CategoryName, out var categoryId))
            {
                var cat = new ServiceCategory
                {
                    BusinessId = businessId,
                    Name = line.CategoryName,
                    DisplayOrder = displayOrder++,
                    IsActive = true
                };
                await _unitOfWork.ServiceCategories.CreateAsync(cat);
                categoryCache[line.CategoryName] = cat.ServiceCategoryId;
                categoryId = cat.ServiceCategoryId;
                categoriesCreated++;
            }

            var service = new Service
            {
                ServiceId = Guid.NewGuid(),
                BusinessId = businessId,
                ServiceName = line.ServiceName.Trim(),
                Description = line.Description ?? string.Empty,
                DurationMinutes = line.DurationMinutes > 0 ? line.DurationMinutes : 60,
                Price = line.Price,
                CategoryId = categoryId,
                Tier = ParseTier(line.Tier),
                ServiceType = ParseServiceType(line.ServiceType),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Services.CreateAsync(service);
            servicesCreated++;
        }

        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "Import",
            "ServiceCatalog",
            businessId.ToString(),
            null,
            new { servicesCreated, categoriesCreated, servicesSkipped },
            ct);

        _logger.LogInformation(
            "Catalog import for business {BusinessId}: created={Created}, skipped={Skipped}",
            businessId, servicesCreated, servicesSkipped);

        if (servicesCreated == 0 && servicesSkipped == 0)
            warnings.Add("No se importó ningún servicio. Revise el documento o la selección.");

        return new CatalogImportResultDto
        {
            CategoriesCreated = categoriesCreated,
            ServicesCreated = servicesCreated,
            ServicesSkipped = servicesSkipped,
            Warnings = warnings
        };
    }

    private static ServiceTier ParseTier(string? tier) => tier?.ToLowerInvariant() switch
    {
        "premium" => ServiceTier.Premium,
        "deluxe" => ServiceTier.Deluxe,
        _ => ServiceTier.Base
    };

    private static ServiceType ParseServiceType(string? type) =>
        type?.Equals("AddOn", StringComparison.OrdinalIgnoreCase) == true
            ? ServiceType.AddOn
            : ServiceType.Standard;

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
