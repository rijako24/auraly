using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ReservationAddOnRepository : IReservationAddOnRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationAddOnRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ReservationAddOn addOn)
    {
        await _context.ReservationAddOns.AddAsync(addOn);
    }
}
