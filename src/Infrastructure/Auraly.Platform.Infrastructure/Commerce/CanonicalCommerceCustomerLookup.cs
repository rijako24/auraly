using System.Data;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Commerce;

public sealed class CanonicalCommerceCustomerLookup(ApplicationDbContext context)
    : ICanonicalCommerceCustomerLookup
{
    public async Task<CommerceCustomerReference?> FindAsync(
        Guid businessId,
        Guid integrationConnectionId,
        CommerceProvider provider,
        string phone,
        CancellationToken ct = default)
    {
        var normalizedPhone = string.Concat(phone.Where(char.IsDigit));
        if (normalizedPhone.Length == 0) return null;

        var connection = (SqlConnection)context.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (2)
                    externalCustomer.ExternalAccountId,
                    externalCustomer.ExternalCustomerId,
                    party.DisplayName,
                    phoneContact.Value
                FROM dbo.Customers customer
                INNER JOIN dbo.Parties party
                    ON party.PartyId=customer.PartyId AND party.IsActive=1
                INNER JOIN dbo.ExternalCommerceCustomers externalCustomer
                    ON externalCustomer.CustomerId=customer.CustomerId
                   AND externalCustomer.PartyId=party.PartyId
                   AND externalCustomer.BusinessId=customer.BusinessId
                   AND externalCustomer.IntegrationConnectionId=@IntegrationConnectionId
                   AND externalCustomer.ReconciliationStatus=N'Linked'
                   AND externalCustomer.IsActive=1
                INNER JOIN dbo.PartyContacts phoneContact
                    ON phoneContact.PartyId=party.PartyId
                   AND phoneContact.ContactType=N'Phone'
                   AND phoneContact.NormalizedValue=@Phone
                   AND phoneContact.IsActive=1
                WHERE customer.BusinessId=@BusinessId AND customer.IsActive=1
                ORDER BY phoneContact.IsPrimary DESC,party.CreatedAt,party.PartyId;
                """;
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@IntegrationConnectionId", integrationConnectionId);
            command.Parameters.AddWithValue("@Phone", normalizedPhone);
            var matches = new List<CommerceCustomerReference>(2);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                matches.Add(new CommerceCustomerReference(
                    provider,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            return matches.Count == 1 ? matches[0] : null;
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }
}
