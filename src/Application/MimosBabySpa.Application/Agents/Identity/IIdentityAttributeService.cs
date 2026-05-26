using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Identity;

/// <summary>
/// Atributos de identidad persistentes del cliente (por rol semántico), independientes del vertical.
/// </summary>
public interface IIdentityAttributeService
{
    Task SyncFromFactAsync(
        AgentToolContext ctx,
        FactSchemaEntry schemaEntry,
        string value,
        CancellationToken cancellationToken = default);

    string? GetByRole(AgentToolContext ctx, string role);
}
