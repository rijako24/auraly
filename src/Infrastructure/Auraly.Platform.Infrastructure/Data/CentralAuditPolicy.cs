using System.Text.Json;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Auraly.Platform.Infrastructure.Data;

internal sealed class CentralAuditPolicy
{
    private const int MaximumTextValueLength = 512;

    private static readonly HashSet<Type> AuditedEntityTypes =
    [
        typeof(Tenant),
        typeof(Business),
        typeof(AppUser),
        typeof(AppRole),
        typeof(UserRole),
        typeof(RolePermission),
        typeof(BusinessWhatsAppNumber),
        typeof(Service),
        typeof(Reservation),
        typeof(Promotion),
        typeof(Product),
        typeof(ProductCategory),
        typeof(ProductOffer),
        typeof(ProductImage),
        typeof(ProductAlias),
        typeof(Lead),
        typeof(Employee),
        typeof(Agent),
        typeof(BusinessInboundContact),
        typeof(IntegrationConnection)
    ];

    private static readonly string[] SensitivePropertyTokens =
    [
        "password", "hash", "salt", "token", "secret", "credential", "certificate",
        "privatekey", "accesskey", "pin", "cvv", "cardnumber", "settingsjson", "secretsjson",
        "rawpayload", "body", "content"
    ];

    private readonly ITenantContext? _tenantContext;
    private readonly ICorrelationIdProvider? _correlationIdProvider;

    public CentralAuditPolicy(
        ITenantContext? tenantContext,
        ICorrelationIdProvider? correlationIdProvider)
    {
        _tenantContext = tenantContext;
        _correlationIdProvider = correlationIdProvider;
    }

    public void AppendAuditEntries(ApplicationDbContext context)
    {
        var auditLogs = context.ChangeTracker.Entries()
            .Where(IsAuditableChange)
            .Select(CreateAuditLog)
            .ToArray();

        if (auditLogs.Length > 0)
        {
            context.AuditLogs.AddRange(auditLogs);
        }
    }

    private static bool IsAuditableChange(EntityEntry entry)
    {
        if (!AuditedEntityTypes.Contains(entry.Metadata.ClrType))
        {
            return false;
        }

        return entry.State switch
        {
            EntityState.Added or EntityState.Deleted => true,
            EntityState.Modified => entry.Properties.Any(HasActualChange),
            _ => false
        };
    }

    private AuditLog CreateAuditLog(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        var entityId = primaryKey is null
            ? null
            : string.Join("|", primaryKey.Properties.Select(property =>
                FormatValue(entry.Property(property.Name).CurrentValue)));

        var properties = entry.Properties
            .Where(property => ShouldCapture(entry, property))
            .OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
            .ToArray();

        var oldValues = entry.State is EntityState.Modified or EntityState.Deleted
            ? ToJson(properties, useOriginalValue: true)
            : null;
        var newValues = entry.State is EntityState.Added or EntityState.Modified
            ? ToJson(properties, useOriginalValue: false)
            : null;

        return new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            UserId = _tenantContext?.UserId,
            TenantId = _tenantContext?.TenantId,
            BusinessId = _tenantContext?.BusinessId,
            Action = entry.State.ToString(),
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            CorrelationId = _correlationIdProvider?.CorrelationId,
            Timestamp = DateTime.UtcNow
        };
    }

    private static bool ShouldCapture(EntityEntry entry, PropertyEntry property)
    {
        var name = property.Metadata.Name;
        if (SensitivePropertyTokens.Any(token =>
                name.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return entry.State != EntityState.Modified || property.IsModified && HasActualChange(property);
    }

    private static string? ToJson(IEnumerable<PropertyEntry> properties, bool useOriginalValue)
    {
        var values = properties.ToDictionary(
            property => property.Metadata.Name,
            property => NormalizeValue(useOriginalValue ? property.OriginalValue : property.CurrentValue),
            StringComparer.Ordinal);

        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is not string text)
        {
            return value;
        }

        return text.Length <= MaximumTextValueLength
            ? text
            : $"{text[..MaximumTextValueLength]}…";
    }

    private static bool HasActualChange(PropertyEntry property) =>
        !ValuesEqual(property.OriginalValue, property.CurrentValue);

    private static bool ValuesEqual(object? before, object? after)
    {
        if (ReferenceEquals(before, after)) return true;
        if (before is byte[] beforeBytes && after is byte[] afterBytes)
            return beforeBytes.AsSpan().SequenceEqual(afterBytes);
        return Equals(before, after);
    }

    private static string FormatValue(object? value) => value?.ToString() ?? "null";
}