namespace MimosBabySpa.Application.Agents.Packs;

public interface IPackContextLoader
{
    string PackId { get; }

    Task LoadAsync(AgentToolContext session, CancellationToken cancellationToken = default);
}
