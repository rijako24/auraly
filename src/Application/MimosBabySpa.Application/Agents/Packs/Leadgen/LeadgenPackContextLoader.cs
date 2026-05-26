namespace MimosBabySpa.Application.Agents.Packs.Leadgen;

public sealed class LeadgenPackContextLoader : IPackContextLoader
{
    public string PackId => LeadgenPackIds.Leadgen;

    public Task LoadAsync(AgentToolContext session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
