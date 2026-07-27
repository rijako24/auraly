namespace Auraly.BuildingBlocks.Application.Idempotency;

public readonly record struct IdempotencyKey
{
    public IdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new ArgumentException(
                "An idempotency key is required and cannot exceed 128 characters.",
                nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
