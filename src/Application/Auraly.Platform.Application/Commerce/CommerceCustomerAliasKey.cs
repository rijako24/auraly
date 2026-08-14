using System.Security.Cryptography;
using System.Text;

namespace Auraly.Platform.Application.Commerce;

public static class CommerceCustomerAliasKey
{
    private const int MaximumLength = 100;

    public static string Resolve(
        CommerceCustomerReference? customer,
        string? channelPhone)
    {
        var external = FromExternalCustomer(customer);
        return external.Length > 0
            ? external
            : LocalProductCandidateRetriever.NormalizeCustomerKey(channelPhone);
    }

    public static string FromExternalCustomer(CommerceCustomerReference? customer)
    {
        if (customer is null
            || string.IsNullOrWhiteSpace(customer.ExternalAccountId)
            || string.IsNullOrWhiteSpace(customer.ExternalCustomerId))
        {
            return string.Empty;
        }

        var provider = customer.Provider.ToString().ToLowerInvariant();
        var account = NormalizePart(customer.ExternalAccountId);
        var externalCustomer = NormalizePart(customer.ExternalCustomerId);
        if (account.Length == 0 || externalCustomer.Length == 0)
            return string.Empty;

        var value = $"{provider}:{account}:{externalCustomer}";
        if (value.Length <= MaximumLength)
            return value;

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
        return $"{provider}:{hash}";
    }

    private static string NormalizePart(string value) =>
        string.Join(
            '-',
            value.Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n', ':', '/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
