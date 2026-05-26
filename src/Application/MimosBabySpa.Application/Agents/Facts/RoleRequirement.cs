namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Declara un rol semántico requerido u opcional por una tool.
/// El motor resuelve rol → key (vía factSchema del tenant) → valor antes de ejecutar.
/// </summary>
public sealed record RoleRequirement(
    string Role,
    bool Required = true,
    string? Description = null,
    string? ArgName = null);
