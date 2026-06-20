using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public sealed class CommerceConfig
{
    public bool Enabled { get; init; }
    public CommerceProvider Provider { get; init; } = CommerceProvider.Local;
}
