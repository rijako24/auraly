using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proveedor de contexto de negocio con caché en memoria.
/// Reduce la carga en BD cachéando configuraciones que no cambian frecuentemente.
/// </summary>
public class CachedBusinessContextProvider
{
    private readonly IMemoryCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CachedBusinessContextProvider> _logger;
    private readonly ILogger<LoadedBusinessContext> _contextLogger;

    // Configuración de caché
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);
    private const string CacheKeyPrefix = "business_context_";

    public CachedBusinessContextProvider(
        IMemoryCache cache,
        IUnitOfWork unitOfWork,
        ILogger<CachedBusinessContextProvider> logger,
        ILogger<LoadedBusinessContext> contextLogger)
    {
        _cache = cache;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _contextLogger = contextLogger;
    }

    /// <summary>
    /// Obtiene el contexto de negocio desde caché o lo carga desde BD.
    /// </summary>
    public async Task<LoadedBusinessContext> GetOrLoadAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(businessId);

        // Intentar obtener desde caché
        if (_cache.TryGetValue<LoadedBusinessContext>(cacheKey, out var cached))
        {
            _logger.LogDebug(
                "✅ BusinessContext servido desde caché: BusinessId={BusinessId}",
                businessId);
            return cached!;
        }

        // Si no está en caché, cargar desde BD
        _logger.LogDebug(
            "⚠️ BusinessContext no en caché, cargando desde BD: BusinessId={BusinessId}",
            businessId);

        var context = await LoadedBusinessContext.LoadAsync(
            businessId,
            _unitOfWork,
            _contextLogger,
            cancellationToken);

        // Guardar en caché
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheExpiration,
            Priority = CacheItemPriority.High,
            Size = 1 // Para límites de tamaño si se configura
        };

        _cache.Set(cacheKey, context, cacheOptions);

        _logger.LogInformation(
            "💾 BusinessContext cargado y guardado en caché: BusinessId={BusinessId}, " +
            "Expira en {ExpirationMinutes} minutos",
            businessId, CacheExpiration.TotalMinutes);

        return context;
    }

    /// <summary>
    /// Invalida el caché para un negocio específico.
    /// Usar cuando se actualice la configuración del negocio.
    /// </summary>
    public void Invalidate(Guid businessId)
    {
        var cacheKey = GetCacheKey(businessId);
        _cache.Remove(cacheKey);

        _logger.LogInformation(
            "🗑️ Caché invalidado para BusinessId={BusinessId}",
            businessId);
    }

    /// <summary>
    /// Invalida todo el caché de contextos de negocio.
    /// Usar con precaución.
    /// </summary>
    public void InvalidateAll()
    {
        // Note: IMemoryCache no tiene método Clear(), 
        // así que esta implementación es limitada.
        // Para producción, considerar usar IDistributedCache con Redis
        _logger.LogWarning("InvalidateAll llamado - requiere implementación específica");
    }

    /// <summary>
    /// Precarga el contexto de un negocio en caché.
    /// Útil para warming up al iniciar la aplicación.
    /// </summary>
    public async Task PreloadAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "🔥 Precargando contexto para BusinessId={BusinessId}",
            businessId);

        await GetOrLoadAsync(businessId, cancellationToken);
    }

    private static string GetCacheKey(Guid businessId)
    {
        return $"{CacheKeyPrefix}{businessId}";
    }
}
