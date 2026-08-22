using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _context;

    public TenantRepository(ApplicationDbContext context) => _context = context;

    public async Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Businesses)
            .Include(t => t.AppUsers)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is not null) await LoadDeviceCountAsync(tenant, ct);
        if (tenant is not null) await LoadLegalIdentityAsync(tenant, ct);
        return tenant;
    }

    public Task<Tenant?> GetByIdForCapacityUpdateAsync(Guid tenantId, CancellationToken ct = default) =>
        _context.Tenants
            .FromSqlInterpolated($"SELECT * FROM dbo.Tenants WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={tenantId}")
            .SingleOrDefaultAsync(ct);

    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = _context.Tenants
            .Include(t => t.Businesses)
            .Include(t => t.AppUsers)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(value) || t.Email.ToLower().Contains(value));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        foreach (var tenant in items) await LoadDeviceCountAsync(tenant, ct);
        return (items, totalCount);
    }

    private async Task LoadDeviceCountAsync(Tenant tenant, CancellationToken ct)
    {
        tenant.ActiveEnrolledDeviceCount = await _context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.EnrolledDevices WHERE TenantId={tenant.TenantId} AND IsActive=1")
            .SingleAsync(ct);
    }

    private async Task LoadLegalIdentityAsync(Tenant tenant, CancellationToken ct)
    {
        var profile = await _context.Database.SqlQuery<LegalIdentityRow>($"""
            SELECT LegalName,Nit,VerificationDigit
            FROM dbo.TenantLegalProfiles WHERE TenantId={tenant.TenantId}
            """).SingleOrDefaultAsync(ct);
        tenant.LegalName = profile?.LegalName;
        tenant.Nit = profile?.Nit;
        tenant.VerificationDigit = profile?.VerificationDigit;
    }

    public Task UpdateLegalIdentityAsync(Guid tenantId, string legalName, string nit, string verificationDigit, DateTimeOffset now, CancellationToken ct = default)
    {
        var normalizedNit = new string(nit.Where(char.IsDigit).ToArray());
        return _context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.TenantLegalProfiles
            SET LegalName={legalName},Nit={nit},NormalizedNit={normalizedNit},VerificationDigit={verificationDigit},UpdatedAt={now}
            WHERE TenantId={tenantId};
            """, ct);
    }

    private sealed record LegalIdentityRow(string LegalName, string Nit, string VerificationDigit);

    public Task RevokeActiveAuthenticationSessionsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default) =>
        _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.AuthenticationSessions
            SET Status=N'Revoked', RevokedAt={now}, RevocationReason=N'TenantDeactivated', UpdatedAt={now}
            WHERE TenantId={tenantId} AND Status=N'Active';", ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default) => await _context.Tenants.AddAsync(tenant, ct);
    public void Update(Tenant tenant) => _context.Tenants.Update(tenant);
}
