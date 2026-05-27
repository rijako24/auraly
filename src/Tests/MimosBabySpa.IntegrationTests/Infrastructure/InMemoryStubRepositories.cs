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
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Marineritos",      DurationMinutes = 60, Price = 80,  IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Base,    CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Post Vacunas",    DurationMinutes = 60, Price = 90,  IsActive = true, CategoryId = TestCategoryIds.Plan, ServiceCategory = PlanCategory, Tier = ServiceTier.Premium, CreatedAt = DateTime.UtcNow },
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

    public Task<Service> CreateAsync(Service service) { _store.Add(service); return Task.FromResult(service); }
    public Task<Service> UpdateAsync(Service service) => Task.FromResult(service);

    public Task<(IReadOnlyList<Service> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct = default) =>
        throw new NotImplementedException();
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

    public Task<Business> CreateAsync(Business business) => Task.FromResult(business);
    public Task<Business> UpdateAsync(Business business) => Task.FromResult(business);

    public Task<IReadOnlyList<Business>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<(IReadOnlyList<Business> Items, int TotalCount)> GetPagedByTenantIdAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// BusinessConfiguration Repository — returns empty config
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryBusinessConfigurationRepository : IBusinessConfigurationRepository
{
    private readonly List<BusinessConfiguration> _configs;

    public InMemoryBusinessConfigurationRepository(Guid businessId)
    {
        _configs = new List<BusinessConfiguration>
        {
            new()
            {
                BusinessId = businessId,
                Key = BusinessConfigurationKey.Integrations,
                Value = """{"googleCalendar":{"enabled":false,"calendarId":"primary"}}"""
            },
            new()
            {
                BusinessId = businessId,
                Key = BusinessConfigurationKey.SchedulingPolicy,
                Value = """
                    {
                      "slotIntervalMinutes": 60,
                      "bufferBetweenAppointmentsMinutes": 0,
                      "requireEmployee": true,
                      "schedule": {
                        "monday": [{"open":"08:00","close":"18:00"}],
                        "tuesday": [{"open":"08:00","close":"18:00"}],
                        "wednesday": [{"open":"08:00","close":"18:00"}],
                        "thursday": [{"open":"08:00","close":"18:00"}],
                        "friday": [{"open":"08:00","close":"18:00"}],
                        "saturday": [{"open":"08:00","close":"18:00"}],
                        "sunday": []
                      }
                    }
                    """
            }
        };
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
            r.ReservationDateTime.HasValue &&
            r.ReservationDateTime.Value >= startDate &&
            r.ReservationDateTime.Value <= endDate));

    public Task<Reservation?> GetActiveByConversationIdAsync(Guid conversationId, CancellationToken ct = default) =>
        Task.FromResult(_store
            .Where(r => r.ConversationId == conversationId && r.Status.IsActiveDraft())
            .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<Reservation>> GetManageableByCustomerPhoneAsync(
        Guid businessId,
        string customerPhone,
        DateOnly businessToday,
        CancellationToken ct = default)
    {
        var phone = customerPhone.Trim();
        var list = _store
            .Where(r => r.BusinessId == businessId
                && !string.IsNullOrWhiteSpace(r.CustomerPhoneSnapshot)
                && string.Equals(r.CustomerPhoneSnapshot.Trim(), phone, StringComparison.OrdinalIgnoreCase)
                && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.OnHold)
                && (!r.ReservationDateTime.HasValue
                    || DateOnly.FromDateTime(r.ReservationDateTime.Value) >= businessToday))
            .OrderBy(r => r.ReservationDateTime)
            .ThenByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<Reservation>>(list);
    }

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
        Guid businessId, int page, int pageSize, string? search, DateTime? from, DateTime? to, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Reservation>> GetRecentByBusinessIdAsync(
        Guid businessId, int count, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<(Guid ServiceId, string ServiceName, int TotalReservations, decimal Revenue)>> GetTopServicesByBusinessIdAsync(
        Guid businessId, int top, DateTime? from, DateTime? to, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

public class InMemoryConversationContextRepository : IConversationContextRepository
{
    private readonly List<ConversationContext> _store = [];

    public Task<ConversationContext?> GetByConversationIdAndFieldAsync(Guid conversationId, string field) =>
        Task.FromResult(_store.FirstOrDefault(c =>
            c.ConversationId == conversationId &&
            string.Equals(c.Field, field, StringComparison.OrdinalIgnoreCase)));

    public Task<ConversationContext> CreateOrUpdateAsync(Guid conversationId, string field, string value)
    {
        var existing = _store.FirstOrDefault(c =>
            c.ConversationId == conversationId &&
            string.Equals(c.Field, field, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Value = value;
            return Task.FromResult(existing);
        }

        var created = new ConversationContext
        {
            ConversationContextId = Guid.NewGuid(),
            ConversationId = conversationId,
            Field = field,
            Value = value
        };
        _store.Add(created);
        return Task.FromResult(created);
    }

    public Task<IEnumerable<ConversationContext>> GetByConversationIdAsync(Guid conversationId) =>
        Task.FromResult(_store.Where(c => c.ConversationId == conversationId));

    public Task DeleteByConversationIdAsync(Guid conversationId)
    {
        _store.RemoveAll(c => c.ConversationId == conversationId);
        return Task.CompletedTask;
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

    public Task<Employee> CreateAsync(Employee employee) { _store.Add(employee); return Task.FromResult(employee); }
    public Task<Employee> UpdateAsync(Employee employee) => Task.FromResult(employee);

    public Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct = default) =>
        throw new NotImplementedException();
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
    public Task<Lead?> GetByUserNumberAsync(string userNumber) =>
        Task.FromResult<Lead?>(null);

    public Task<Lead?> GetByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber) =>
        Task.FromResult<Lead?>(null);

    public Task<Lead?> GetByIdAsync(Guid leadId) =>
        Task.FromResult<Lead?>(null);

    public Task<IEnumerable<Lead>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(Enumerable.Empty<Lead>());

    public Task<Lead> CreateAsync(Lead lead) => Task.FromResult(lead);
    public Task<Lead> UpdateAsync(Lead lead) => Task.FromResult(lead);

    public Task<(IReadOnlyList<Lead> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search, CancellationToken ct = default) =>
        throw new NotImplementedException();
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
    private readonly List<ReservationAddOn> _store = [];

    public Task AddAsync(ReservationAddOn addOn)
    {
        _store.Add(addOn);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReservationAddOn>> GetByReservationIdAsync(Guid reservationId) =>
        Task.FromResult<IReadOnlyList<ReservationAddOn>>(_store.Where(a => a.ReservationId == reservationId).ToList());

    public Task DeleteAsync(ReservationAddOn addOn)
    {
        _store.Remove(addOn);
        return Task.CompletedTask;
    }
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
