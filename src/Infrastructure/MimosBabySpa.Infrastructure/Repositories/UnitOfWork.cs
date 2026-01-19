using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IConversationRepository? _conversations;
    private IMessageRepository? _messages;
    private ILeadRepository? _leads;
    private IBusinessRepository? _businesses;
    private IBusinessWhatsAppNumberRepository? _businessWhatsAppNumbers;
    private IBusinessConfigurationRepository? _businessConfigurations;
    private ISystemConfigurationRepository? _systemConfigurations;
    private IConversationContextRepository? _conversationContexts;

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

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
