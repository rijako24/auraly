using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────────
// Service Repository — pre-populates the three MimosBabySpa services
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryServiceRepository : IServiceRepository
{
    private readonly List<Service> _store;

    public InMemoryServiceRepository(Guid businessId)
    {
        _store = new List<Service>
        {
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Marineritos",      DurationMinutes = 60, Price = 80, IsActive = true, Category = ServiceCategory.Plan, Tier = ServiceTier.Base,    CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Post Vacunas",    DurationMinutes = 60, Price = 90, IsActive = true, Category = ServiceCategory.Plan, Tier = ServiceTier.Premium, CreatedAt = DateTime.UtcNow },
            new() { ServiceId = Guid.NewGuid(), BusinessId = businessId, ServiceName = "Plan Aventuras Marinas", DurationMinutes = 75, Price = 100, IsActive = true, Category = ServiceCategory.Plan, Tier = ServiceTier.Deluxe, CreatedAt = DateTime.UtcNow },
            
            // Dedicated Plan for Add-On Test
            new() { ServiceId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), BusinessId = businessId, ServiceName = "Plan Deluxe", DurationMinutes = 90, Price = 120, IsActive = true, Category = ServiceCategory.Plan, Tier = ServiceTier.Deluxe, CreatedAt = DateTime.UtcNow },

            // Add-on service
            new() { ServiceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), BusinessId = businessId, ServiceName = "Masaje Extra 15m", DurationMinutes = 15, Price = 20, IsActive = true, Category = ServiceCategory.Plan, Tier = ServiceTier.Base, ServiceType = ServiceType.AddOn, CreatedAt = DateTime.UtcNow }
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
                 Key = BusinessConfigurationKey.EntityExtractionConfig,
                 Value = """
                 {
                   "SelectedAddOns": {
                     "Description": "Servicios extra seleccionados por el cliente",
                     "Type": "Text",
                     "IsRequired": false
                   }
                 }
                 """
             },
             new() { BusinessId = businessId, Key = BusinessConfigurationKey.OperatingHours, Value = "{}" },
             new() { BusinessId = businessId, Key = BusinessConfigurationKey.PaymentMethods, Value = "[]" },
             new() { BusinessId = businessId, Key = BusinessConfigurationKey.Integrations, Value = """{"googleCalendar":{"enabled":false,"calendarId":"primary"}}""" }
         };
         Console.WriteLine($"[DEBUG] Repo initialized with {_configs.Count} configs for {businessId}");
    }

    public Task<IEnumerable<BusinessConfiguration>> GetByBusinessIdAsync(Guid businessId) =>
        Task.FromResult<IEnumerable<BusinessConfiguration>>(_configs);

    public Task<BusinessConfiguration?> GetByBusinessIdAndKeyAsync(Guid businessId, BusinessConfigurationKey key)
    {
        var config = _configs.FirstOrDefault(c => c.BusinessId == businessId && c.Key == key);
        
        if (config == null && key == BusinessConfigurationKey.EntityExtractionConfig)
        {
             Console.WriteLine("[DEBUG] Force-returning EntityExtractionConfig (fallback)!");
             return Task.FromResult<BusinessConfiguration?>(new BusinessConfiguration
             {
                 BusinessId = businessId,
                 Key = BusinessConfigurationKey.EntityExtractionConfig,
                 Value = """
                 {
                   "SelectedAddOns": {
                     "Description": "Servicios extra seleccionados por el cliente",
                     "Type": "Text",
                     "IsRequired": false
                   }
                 }
                 """
             });
        }

        Console.WriteLine($"[DEBUG] Query Key={key}, Found={config != null}");
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

    public Task<Lead> CreateAsync(Lead lead) => Task.FromResult(lead);
    public Task<Lead> UpdateAsync(Lead lead) => Task.FromResult(lead);
}

// ─────────────────────────────────────────────────────────────────────────────
// ServiceAddOnRule Repository — empty
// ─────────────────────────────────────────────────────────────────────────────

public class InMemoryServiceAddOnRuleRepository : IServiceAddOnRuleRepository
{
    private readonly List<ServiceAddOnRule> _rules;

    public InMemoryServiceAddOnRuleRepository(Guid businessId)
    {
        var planDeluxeId      = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var masajeExtraId     = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        // Mock Service objects for navigation properties (REQUIRED by LoadedBusinessContext)
        var planDeluxe = new Service
        {
            ServiceId = planDeluxeId,
            ServiceName = "Plan Deluxe",
            ServiceType = ServiceType.Standard,
            Category = ServiceCategory.Plan 
        };

        var masajeExtra = new Service
        {
             ServiceId = masajeExtraId,
             ServiceName = "Masaje Extra 15m",
             ServiceType = ServiceType.AddOn,
             Category = ServiceCategory.Otro,
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
    public Task SendTextMessageAsync(Guid businessId, string to, string message) => Task.CompletedTask;
    public Task SendImageMessageAsync(Guid businessId, string to, string imageUrl, string? caption = null) => Task.CompletedTask;
    public Task<bool> VerifyWebhookAsync(string mode, string token, string challenge) => Task.FromResult(true);
    public Task<Stream> DownloadMediaAsync(Guid businessId, string mediaId) => Task.FromResult<Stream>(Stream.Null);
}
