using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _context;

    public EmployeeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Employee?> GetByIdAsync(Guid employeeId)
    {
        return await _context.Employees
            .Include(e => e.EmployeeServices)
                .ThenInclude(es => es.Service)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
    }

    public async Task<IEnumerable<Employee>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.Employees
            .Include(e => e.EmployeeServices)
                .ThenInclude(es => es.Service)
            .Where(e => e.BusinessId == businessId)
            .OrderBy(e => e.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Employee>> GetActiveByBusinessIdAsync(Guid businessId)
    {
        return await _context.Employees
            .Include(e => e.EmployeeServices)
                .ThenInclude(es => es.Service)
            .Where(e => e.BusinessId == businessId && e.IsActive)
            .OrderBy(e => e.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Employee>> GetByBusinessIdAndServiceIdAsync(Guid businessId, Guid serviceId)
    {
        return await _context.Employees
            .Include(e => e.EmployeeServices)
                .ThenInclude(es => es.Service)
            .Where(e => e.BusinessId == businessId 
                && e.IsActive 
                && e.EmployeeServices.Any(es => es.ServiceId == serviceId))
            .OrderBy(e => e.Name)
            .ToListAsync();
    }

    public Task<Employee> CreateAsync(Employee employee)
    {
        _context.Employees.Add(employee);
        return Task.FromResult(employee);
    }

    public Task<Employee> UpdateAsync(Employee employee)
    {
        _context.Employees.Update(employee);
        return Task.FromResult(employee);
    }
}
