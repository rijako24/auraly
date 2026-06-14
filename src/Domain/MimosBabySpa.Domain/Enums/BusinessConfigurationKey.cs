namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Keys stored in BusinessConfigurations.
/// Agent-owned business flow settings live in Agents.SettingsJson.
/// </summary>
public enum BusinessConfigurationKey
{
    /// <summary>Obsolete; post-payment messages live in Agents.SettingsJson messageSequences.</summary>
    PaymentConfirmationMessages = 1,
    SchedulingPolicy = 2
}
