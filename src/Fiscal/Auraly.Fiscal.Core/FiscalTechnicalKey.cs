namespace Auraly.Fiscal.Core;

public sealed class FiscalTechnicalKey
{
    private readonly string _value;

    public FiscalTechnicalKey(string value, string version)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A fiscal technical key is required.", nameof(value));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A technical key version is required.", nameof(version));
        }

        _value = value;
        Version = version.Trim();
    }

    public string Version { get; }

    public ReadOnlySpan<char> Reveal() => _value.AsSpan();

    public override string ToString() => $"FiscalTechnicalKey({Version}, [REDACTED])";
}
