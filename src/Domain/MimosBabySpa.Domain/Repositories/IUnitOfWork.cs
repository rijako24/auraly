namespace MimosBabySpa.Domain.Repositories;

public interface IUnitOfWork : IDisposable
{
    IConversationRepository Conversations { get; }
    IMessageRepository Messages { get; }
    ILeadRepository Leads { get; }
    IBusinessRepository Businesses { get; }
    IBusinessWhatsAppNumberRepository BusinessWhatsAppNumbers { get; }
    IBusinessConfigurationRepository BusinessConfigurations { get; }
    ISystemConfigurationRepository SystemConfigurations { get; }
    IConversationContextRepository ConversationContexts { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
