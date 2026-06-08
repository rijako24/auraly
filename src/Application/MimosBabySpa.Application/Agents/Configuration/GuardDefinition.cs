namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Precondiciones declarativas por tool. Gramática de requires:
///   fact:key
///   verification:availability_checked
///   verification:customer_identified
///   state:payment_confirmed_no_slot
///   flag:verbal_confirmation
/// </summary>
public sealed class GuardDefinition
{
    public IReadOnlyList<string> Requires { get; init; } = [];
}
