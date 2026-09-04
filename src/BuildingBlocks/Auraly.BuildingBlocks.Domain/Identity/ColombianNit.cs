namespace Auraly.BuildingBlocks.Domain.Identity;

public static class ColombianNit
{
    private static readonly int[] Weights =
        [71, 67, 59, 53, 47, 43, 41, 37, 29, 23, 19, 17, 13, 7, 3];

    public static bool TryCalculateVerificationDigit(string? nit, out int verificationDigit)
    {
        verificationDigit = 0;
        var value = nit?.Trim();
        if (value is null || value.Length is < 3 or > 15 || !value.All(char.IsDigit))
            return false;

        var offset = Weights.Length - value.Length;
        var sum = value.Select((digit, index) => (digit - '0') * Weights[offset + index]).Sum();
        var remainder = sum % 11;
        verificationDigit = remainder is 0 or 1 ? remainder : 11 - remainder;
        return true;
    }

    public static int CalculateVerificationDigit(string nit) =>
        TryCalculateVerificationDigit(nit, out var verificationDigit)
            ? verificationDigit
            : throw new ArgumentException(
                "El NIT debe contener entre 3 y 15 dígitos.", nameof(nit));
}
