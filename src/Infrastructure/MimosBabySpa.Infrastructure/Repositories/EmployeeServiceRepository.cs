using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class EmployeeServiceRepository : IEmployeeServiceRepository
{
    private readonly ApplicationDbContext _context;

    public EmployeeServiceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<EmployeeService?> GetByIdAsync(Guid employeeServiceId)
    {
        return await _context.EmployeeServices
            .Include(es => es.Employee)
            .Include(es => es.Service)
            .FirstOrDefaultAsync(es => es.EmployeeServiceId == employeeServiceId);
    }

    public async Task<IEnumerable<EmployeeService>> GetByEmployeeIdAsync(Guid employeeId)
    {
        return await _context.EmployeeServices
            .Include(es => es.Service)
            .Where(es => es.EmployeeId == employeeId)
            .ToListAsync();
    }

    public async Task<IEnumerable<EmployeeService>> GetByServiceIdAsync(Guid serviceId)
    {
        return await _context.EmployeeServices
            .Include(es => es.Employee)
            .Where(es => es.ServiceId == serviceId)
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(Guid employeeId, Guid serviceId)
    {
        return await _context.EmployeeServices
            .AnyAsync(es => es.EmployeeId == employeeId && es.ServiceId == serviceId);
    }

    public Task<EmployeeService> CreateAsync(EmployeeService employeeService)
    {
        _context.EmployeeServices.Add(employeeService);
        return Task.FromResult(employeeService);
    }

    public async Task DeleteAsync(Guid employeeServiceId)
    {
        var employeeService = await _context.EmployeeServices
            .FirstOrDefaultAsync(es => es.EmployeeServiceId == employeeServiceId);
        
        if (employeeService != null)
        {
            _context.EmployeeServices.Remove(employeeService);
        }
    }

    public async Task<int> GetServiceCountByEmployeeIdAsync(Guid employeeId)
    {
        return await _context.EmployeeServices
            .Where(es => es.EmployeeId == employeeId)
            .CountAsync();
    }
}
