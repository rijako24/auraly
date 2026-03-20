using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

// Shared category ID for Plan (usado por Services y AddOnRules)
internal static class TestCategoryIds
{
    public static readonly Guid Plan = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid Otros = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
}

// ─────────────────────────────────────────────────────────────────────────────
// Service Repository — pre-populates the three MimosBabySpa services
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryServiceRepository : IServiceRepository
{
    private readonly List<Service> _store;
    private static readonly ServiceCategory PlanCategory = new()
    {
        ServiceCategoryId = TestCategoryIds.Plan,
        Name = "Plan",
        DisplayOrder = 0
    };

    public InMemoryServiceRepository(Guid businessId)
    {
        _store = new List<Service>
        {
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Marineritos",      DurationMinutes = 60, Price = 80, IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Base,    CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Post Vacunas",    DurationMinutes = 60, Price = 90, IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Premium, CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Aventuras Marinas", DurationMinutes = 75, Price = 100, IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Deluxe, CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), BusinessId = businessId, ServiceName = "Plan Deluxe", DurationMinutes = 90, Price = 120, IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Deluxe, CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), BusinessId = businessId, ServiceName = "Masaje Extra 15m", DurationMinutes = 15, Price = 20, IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Base, ServiceType = ServiceType.AddOn, CreatedAt = DateTime.UtcNow }
        };
    }

    public Task<Service?> GetByIdAsync(Guid serviceId) =>
        Task.FromResult(_store.FirstOrDefault(s => s.ServiceId == serviceId));

    public Task<Service?> GetByBusinessIdAndNameAsync(Guid businessId, string serviceName) =>
        Task.FromResult(_store.FirstOrDefault(s =>
            s.BusinessId == businessId &&
            string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase)));

    public Task<IEnumerable<Service>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_store.Where(s => s.BusinessId == businessId));

    public Task<IEnumerable<Service>> GetActiveByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_store.Where(s => s.BusinessId == businessId && s.IsActive));

    public Task<(IReadOnlyList<Service> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var q = _store.Where(s => s.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.ServiceName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.OrderBy(x => x.ServiceName).ToList();
        var total = list.Count;
        var items = list
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<(IReadOnlyList<Service> Items, int TotalCount)>((items, total));
    }

    public Task<Service> CreateAsync(Service service) { _store.Add(service); return Task.FromResult(service); }
    public Task<Service> UpdateAsync(Service service) => Task.FromResult(service);
}

// ─────────────────────────────────────────────────────────────────────────────
// Business Repository — returns a pre-built Business
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryBusinessRepository : IBusinessRepository
{
    private readonly Business _business;

    public InMemoryBusinessRepository(Guid businessId)
    {
        _business = new Business
        {
            BusinessId          = businessId,
            TenantId            = Guid.NewGuid(),
            Name                = "Mimos Baby Spa",
            Description         = "Spa especializado en masajes para bebés",
            Phone               = "+1234567890",
            Email               = "info@mimosbabyspa.com",
            IsActive            = true,
            CreatedAt           = DateTime.UtcNow
        };
    }

    public Task<Business?> GetByIdAsync(Guid businessId) =>
        Task.FromResult(_business.BusinessId == businessId ? _business : (Business?)null);

    public Task<Business?> GetByIdWithConfigurationAsync(Guid businessId) =>
        GetByIdAsync(businessId);

    public Task<IReadOnlyList<Business>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Business>>(
            _business.TenantId == tenantId ? [_business] : []);

    public Task<(IReadOnlyList<Business> Items, int TotalCount)> GetPagedByTenantIdAsync(
        Guid tenantId, int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var list = _business.TenantId == tenantId
            ? new List<Business> { _business }
            : [];
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            list = list.Where(b => b.Name.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var total = list.Count;
        var items = list
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<(IReadOnlyList<Business> Items, int TotalCount)>((items, total));
    }

    public Task<Business> CreateAsync(Business business) => Task.FromResult(business);
    public Task<Business> UpdateAsync(Business business) => Task.FromResult(business);
}

// ─────────────────────────────────────────────────────────────────────────────
// BusinessConfiguration Repository — solo claves de infraestructura vigentes (p. ej. Integrations)
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryBusinessConfigurationRepository : IBusinessConfigurationRepository
{
    private readonly List<BusinessConfiguration> _configs;

    public InMemoryBusinessConfigurationRepository(Guid businessId)
    {
         _configs =
         [
             new()
             {
                 BusinessId = businessId,
                 Key = BusinessConfigurationKey.Integrations,
                 Value = """{"googleCalendar":{"enabled":false,"calendarId":"primary"}}"""
             }
         ];
    }

    public Task<IEnumerable<BusinessConfiguration>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult<IEnumerable<BusinessConfiguration>>(_configs);

    public Task<BusinessConfiguration?> GetByBusinessIdAndKeyAsync(Guid businessId, BusinessConfigurationKey key)
    {
        var config = _configs.FirstOrDefault(c => c.BusinessId == businessId && c.Key == key);
        return Task.FromResult(config);
    }

    public Task<IEnumerable<BusinessConfiguration>> GetActiveByBusinessIdAsync(Guid businessId) =>
        Task.FromResult<IEnumerable<BusinessConfiguration>>(_configs);

    public Task<BusinessConfiguration> CreateAsync(BusinessConfiguration configuration)
    {
        _configs.Add(configuration);
        return Task.FromResult(configuration);
    }

    public Task<BusinessConfiguration> UpdateAsync(BusinessConfiguration configuration)
    {
        // simplistic update
        var existing = _configs.FirstOrDefault(c => c.Key == configuration.Key);
        if (existing != null) _configs.Remove(existing);
        _configs.Add(configuration);
        return Task.FromResult(configuration);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SystemConfiguration Repository
// ─────────────────────────────────────────────────────────────────────────────

public class InMemorySystemConfigurationRepository : ISystemConfigurationRepository
{
    public Task<SystemConfiguration?> GetByKeyAsync(SystemConfigurationKey key) =>
        Task.FromResult<SystemConfiguration?>(null);

    public Task<IEnumerable<SystemConfiguration>> GetAllActiveAsync() =>
        Task.FromResult(Enumerable.Empty<SystemConfiguration>());

    public Task<SystemConfiguration> CreateAsync(SystemConfiguration configuration) =>
        Task.FromResult(configuration);

    public Task<SystemConfiguration> UpdateAsync(SystemConfiguration configuration) =>
        Task.FromResult(configuration);
}

// ─────────────────────────────────────────────────────────────────────────────
// Reservation Repository
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryReservationRepository : IReservationRepository
{
    private readonly List<Reservation> _store = [];

    public Task<Reservation?> GetByIdAsync(Guid reservationId) =>
        Task.FromResult(_store.FirstOrDefault(r => r.ReservationId == reservationId));

    public Task<IEnumerable<Reservation>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_store.Where(r => r.BusinessId == businessId));

    public Task<IEnumerable<Reservation>> GetByBusinessIdAndDateRangeAsync(
        Guid businessId, DateTime startDate, DateTime endDate) =>
        Task.FromResult(_store.Where(r =>
            r.BusinessId == businessId &&
            r.ReservationDateTime >= startDate &&
            r.ReservationDateTime <= endDate));

    public Task<Reservation> CreateAsync(Reservation reservation)
    {
        _store.Add(reservation);
        return Task.FromResult(reservation);
    }

    public Task<Reservation> UpdateAsync(Reservation reservation) => Task.FromResult(reservation);

    public Task<bool> ExistsOverlappingReservationAsync(
        Guid businessId, DateTime reservationDate, TimeSpan reservationTime,
        int durationMinutes, Guid? excludeReservationId = null) =>
        Task.FromResult(false);

    public Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var q = _store.Where(r => r.BusinessId == businessId);
        if (startDate.HasValue)
            q = q.Where(r => r.ReservationDateTime >= startDate.Value);
        if (endDate.HasValue)
            q = q.Where(r => r.ReservationDateTime <= endDate.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r =>
                r.Service != null &&
                r.Service.ServiceName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.OrderByDescending(r => r.ReservationDateTime).ToList();
        var total = list.Count;
        var items = list
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<(IReadOnlyList<Reservation> Items, int TotalCount)>((items, total));
    }

    public Task<IReadOnlyList<Reservation>> GetRecentByBusinessIdAsync(
        Guid businessId, int limit, CancellationToken ct = default)
    {
        var items = _store
            .Where(r => r.BusinessId == businessId)
            .OrderByDescending(r => r.ReservationDateTime)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Reservation>>(items);
    }

    public Task<IReadOnlyList<(Guid ServiceId, string ServiceName, int TotalReservations, decimal Revenue)>>
        GetTopServicesByBusinessIdAsync(
            Guid businessId, int limit, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = _store.Where(r => r.BusinessId == businessId);
        if (from.HasValue)
            q = q.Where(r => r.ReservationDateTime >= from.Value);
        if (to.HasValue)
            q = q.Where(r => r.ReservationDateTime <= to.Value);

        var rows = q
            .GroupBy(r => r.ServiceId)
            .Select(g => (
                ServiceId: g.Key,
                ServiceName: g.First().Service?.ServiceName ?? string.Empty,
                TotalReservations: g.Count(),
                Revenue: g.Sum(r => r.Service?.Price ?? 0m)))
            .OrderByDescending(x => x.TotalReservations)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<(Guid ServiceId, string ServiceName, int TotalReservations, decimal Revenue)>>(rows);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BusinessResource Repository — empty store
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryBusinessResourceRepository : IBusinessResourceRepository
{
    public Task<BusinessResource?> GetByIdAsync(Guid businessResourceId) =>
        Task.FromResult<BusinessResource?>(null);

    public Task<BusinessResource?> GetByBusinessIdAndNameAsync(Guid businessId, string resourceName) =>
        Task.FromResult<BusinessResource?>(null);

    public Task<IEnumerable<BusinessResource>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(Enumerable.Empty<BusinessResource>());

    public Task<BusinessResource> CreateAsync(BusinessResource resource) => Task.FromResult(resource);
    public Task<BusinessResource> UpdateAsync(BusinessResource resource) => Task.FromResult(resource);
}

// ─────────────────────────────────────────────────────────────────────────────
// Employee Repository — pre-populates one employee
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryEmployeeRepository : IEmployeeRepository
{
    private readonly List<Employee> _store;

    public InMemoryEmployeeRepository(Guid businessId)
    {
        _store = new List<Employee>
        {
            new()
            {
                EmployeeId = Guid.NewGuid(),
                BusinessId = businessId,
                Name       = "María Terapeuta",
                IsActive   = true,
                CreatedAt  = DateTime.UtcNow
            }
        };
    }

    public Task<Employee?> GetByIdAsync(Guid employeeId) =>
        Task.FromResult(_store.FirstOrDefault(e => e.EmployeeId == employeeId));

    public Task<IEnumerable<Employee>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_store.Where(e => e.BusinessId == businessId));

    public Task<IEnumerable<Employee>> GetActiveByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_store.Where(e => e.BusinessId == businessId && e.IsActive));

    public Task<IEnumerable<Employee>> GetByBusinessIdAndServiceIdAsync(Guid businessId, Guid serviceId) =>
        Task.FromResult(_store.Where(e => e.BusinessId == businessId && e.IsActive));

    public Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var q = _store.Where(e => e.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.OrderBy(e => e.Name).ToList();
        var total = list.Count;
        var items = list
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<(IReadOnlyList<Employee> Items, int TotalCount)>((items, total));
    }

    public Task<Employee> CreateAsync(Employee employee) { _store.Add(employee); return Task.FromResult(employee); }
    public Task<Employee> UpdateAsync(Employee employee) => Task.FromResult(employee);
}

// ─────────────────────────────────────────────────────────────────────────────
// EmployeeService Repository — returns empty
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryEmployeeServiceRepository : IEmployeeServiceRepository
{
    public Task<EmployeeService?> GetByIdAsync(Guid employeeServiceId) =>
        Task.FromResult<EmployeeService?>(null);

    public Task<IEnumerable<EmployeeService>> GetByEmployeeIdAsync(Guid employeeId) =>
        Task.FromResult(Enumerable.Empty<EmployeeService>());

    public Task<IEnumerable<EmployeeService>> GetByServiceIdAsync(Guid serviceId) =>
        Task.FromResult(Enumerable.Empty<EmployeeService>());

    public Task<bool> ExistsAsync(Guid employeeId, Guid serviceId) =>
        Task.FromResult(false);

    public Task<EmployeeService> CreateAsync(EmployeeService employeeService) =>
        Task.FromResult(employeeService);

    public Task DeleteAsync(Guid employeeServiceId) => Task.CompletedTask;

    public Task<int> GetServiceCountByEmployeeIdAsync(Guid employeeId) =>
        Task.FromResult(0);
}

// ─────────────────────────────────────────────────────────────────────────────
// Lead Repository — empty
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryLeadRepository : ILeadRepository
{
    private readonly List<Lead> _store = [];

    public Task<Lead?> GetByUserNumberAsync(string userNumber) =>
        Task.FromResult(_store.FirstOrDefault(l => l.UserNumber == userNumber));

    public Task<Lead?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber) =>
        Task.FromResult(_store.FirstOrDefault(l =>
            l.BusinessId == businessId && l.UserNumber == userNumber));

    public Task<Lead?> GetByIdAsync(Guid leadId) =>
        Task.FromResult(_store.FirstOrDefault(l => l.LeadId == leadId));

    public Task<IEnumerable<Lead>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult<IEnumerable<Lead>>(_store.Where(l => l.BusinessId == businessId));

    public Task<(IReadOnlyList<Lead> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        var q = _store.Where(l => l.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l =>
                l.UserNumber.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (l.CustomerName != null && l.CustomerName.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        var list = q.OrderByDescending(l => l.Timestamp).ToList();
        var total = list.Count;
        var items = list
            .Skip(Math.Max(0, (page - 1) * pageSize))
            .Take(pageSize)
            .ToList();
        return Task.FromResult<(IReadOnlyList<Lead> Items, int TotalCount)>((items, total));
    }

    public Task<Lead> CreateAsync(Lead lead)
    {
        _store.Add(lead);
        return Task.FromResult(lead);
    }

    public Task<Lead> UpdateAsync(Lead lead)
    {
        var idx = _store.FindIndex(l => l.LeadId == lead.LeadId);
        if (idx >= 0)
            _store[idx] = lead;
        else
            _store.Add(lead);
        return Task.FromResult(lead);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCategory Repository — Plan y Otros para tests
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryServiceCategoryRepository : IServiceCategoryRepository
{
    private readonly List<ServiceCategory> _store;

    public InMemoryServiceCategoryRepository(Guid businessId)
    {
        _store = new List<ServiceCategory>
        {
            new() { ServiceCategoryId = TestCategoryIds.Plan, BusinessId = businessId, Name = "Plan", DisplayOrder = 0, IsActive = true, CreatedAt = DateTime.UtcNow },
            new() { ServiceCategoryId = TestCategoryIds.Otros, BusinessId = businessId, Name = "Otros", DisplayOrder = 99, IsActive = true, CreatedAt = DateTime.UtcNow }
        };
    }

    public Task<ServiceCategory?> GetByIdAsync(Guid serviceCategoryId) =>
        Task.FromResult(_store.FirstOrDefault(c => c.ServiceCategoryId == serviceCategoryId));

    public Task<IEnumerable<ServiceCategory>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_store.Where(c => c.BusinessId == businessId));
}

// ─────────────────────────────────────────────────────────────────────────────
// BusinessAttachment Repository — vacío para tests
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryBusinessAttachmentRepository : IBusinessAttachmentRepository
{
    public Task<BusinessAttachment?> GetByIdAsync(Guid businessAttachmentId) =>
        Task.FromResult<BusinessAttachment?>(null);

    public Task<IEnumerable<BusinessAttachment>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(Enumerable.Empty<BusinessAttachment>());
}

// ─────────────────────────────────────────────────────────────────────────────
// ServiceAddOnRule Repository
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryServiceAddOnRuleRepository : IServiceAddOnRuleRepository
{
    private readonly List<ServiceAddOnRule> _rules;

    public InMemoryServiceAddOnRuleRepository(Guid businessId)
    {
        var planDeluxeId      = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var masajeExtraId     = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var planCat = new ServiceCategory { ServiceCategoryId = TestCategoryIds.Plan, Name = "Plan", DisplayOrder = 0 };
        var otrosCat = new ServiceCategory { ServiceCategoryId = TestCategoryIds.Otros, Name = "Otros", DisplayOrder = 99 };

        var planDeluxe = new Service
        {
            ServiceId = planDeluxeId,
            ServiceName = "Plan Deluxe",
            ServiceType = ServiceType.Standard,
            CategoryId = TestCategoryIds.Plan,
            ServiceCategory = planCat
        };

        var masajeExtra = new Service
        {
            ServiceId = masajeExtraId,
            ServiceName = "Masaje Extra 15m",
            ServiceType = ServiceType.AddOn,
            CategoryId = TestCategoryIds.Otros,
            ServiceCategory = otrosCat,
            Price = 15.00m,
            Description = "15 minutos de masaje relajante"
        };

        _rules = new List<ServiceAddOnRule>
        {
            new ServiceAddOnRule
            {
                ServiceAddOnRuleId = Guid.NewGuid(),
                BusinessId = businessId,
                CompatibleServiceId = planDeluxeId,
                AddOnServiceId = masajeExtraId,
                DisplayOrder = 1,
                
                // IMPORTANT: Populate navigation properties for InMemory usage
                CompatibleService = planDeluxe,
                AddOnService = masajeExtra
            }
        };
    }

    public Task<IEnumerable<ServiceAddOnRule>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_rules.Where(r => r.BusinessId == businessId));
}

// ─────────────────────────────────────────────────────────────────────────────
// ReservationAddOn Repository — empty
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryReservationAddOnRepository : IReservationAddOnRepository
{
    public Task AddAsync(ReservationAddOn addOn) => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// NoOp WhatsApp Service — para tests que no envían mensajes reales
// ─────────────────────────────────────────────────────────────────────────────

public class NoOpWhatsAppService : IWhatsAppService
{
    public Task AcknowledgeMessageAsync(string phoneNumberId, string accessToken, string whatsAppMessageId) => Task.CompletedTask;
    public Task SendTextMessageAsync(Guid businessId, string to, string message) => Task.CompletedTask;
    public Task SendImageMessageAsync(Guid businessId, string to, string imageUrl, string? caption = null) => Task.CompletedTask;
    public Task SendDocumentMessageAsync(Guid businessId, string to, string documentUrl, string? caption = null, string? filename = null) => Task.CompletedTask;
    public Task<bool> VerifyWebhookAsync(string mode, string token, string challenge) => Task.FromResult(true);
    public Task<Stream> DownloadMediaAsync(Guid businessId, string mediaId) => Task.FromResult<Stream>(Stream.Null);
}
