using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private readonly ILoggerFactory? _loggerFactory;
    private IConversationRepository? _conversations;
    private IMessageRepository? _messages;
    private ILeadRepository? _leads;
    private IBusinessRepository? _businesses;
    private IBusinessWhatsAppNumberRepository? _businessWhatsAppNumbers;
    private IBusinessConfigurationRepository? _businessConfigurations;
    private ISystemConfigurationRepository? _systemConfigurations;
    private IConversationContextRepository? _conversationContexts;
    private IReservationRepository? _reservations;
    private IServiceRepository? _services;
    private IBusinessResourceRepository? _businessResources;
    private IServiceCoexistenceRuleRepository? _serviceCoexistenceRules;
    private IEmployeeRepository? _employees;
    private IEmployeeServiceRepository? _employeeServices;
    private IConversationStateRepository? _conversationStates;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IConversationRepository Conversations =>
        _conversations ??= new ConversationRepository(_context);

    public IMessageRepository Messages =>
        _messages ??= new MessageRepository(_context);

    public ILeadRepository Leads =>
        _leads ??= new LeadRepository(_context);

    public IBusinessRepository Businesses =>
        _businesses ??= new BusinessRepository(_context);

    public IBusinessWhatsAppNumberRepository BusinessWhatsAppNumbers =>
        _businessWhatsAppNumbers ??= new BusinessWhatsAppNumberRepository(_context);

    public IBusinessConfigurationRepository BusinessConfigurations =>
        _businessConfigurations ??= new BusinessConfigurationRepository(_context);

    public ISystemConfigurationRepository SystemConfigurations =>
        _systemConfigurations ??= new SystemConfigurationRepository(_context);

    public IConversationContextRepository ConversationContexts =>
        _conversationContexts ??= new ConversationContextRepository(_context);

    public IReservationRepository Reservations =>
        _reservations ??= new ReservationRepository(_context);

    public IServiceRepository Services =>
        _services ??= new ServiceRepository(_context);

    public IBusinessResourceRepository BusinessResources =>
        _businessResources ??= new BusinessResourceRepository(_context);

    public IServiceCoexistenceRuleRepository ServiceCoexistenceRules =>
        _serviceCoexistenceRules ??= new ServiceCoexistenceRuleRepository(_context);

    public IEmployeeRepository Employees =>
        _employees ??= new EmployeeRepository(_context);

    public IEmployeeServiceRepository EmployeeServices =>
        _employeeServices ??= new EmployeeServiceRepository(_context);

    public IConversationStateRepository ConversationStates =>
        _conversationStates ??= new ConversationStateRepository(
            _context, 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConversationStateRepository>.Instance);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
