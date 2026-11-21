using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

/// <summary>Publishes existing durable reporting work only after its financial source commits.</summary>
public sealed class SqlAccountingReportingCompletionObserver(
    SqlServerConnectionFactory connections,
    SalesReportingProcessingCoordinator reporting) : IAccountingPostingCompletionObserver
{
    public async Task ObserveAsync(Guid tenantId, Guid businessId, Guid documentId,
        string documentType, CancellationToken cancellationToken)
    {
        if (!SalesReportingProcessingPolicy.IsPaymentCorrection(documentType)) return;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT r.SourceVersion FROM reporting.SalesReportingJobs r
            JOIN dbo.AccountingPostingJobs financial ON financial.SourceDocumentId=r.SourceDocumentId
              AND financial.SourceDocumentType=r.SourceDocumentType AND financial.BusinessId=r.BusinessId
            WHERE r.SourceDocumentId=@Document AND r.SourceDocumentType=@Type AND r.BusinessId=@Business
              AND financial.TenantId=@Tenant AND financial.Status IN(N'Posted',N'CommercialEffectsApplied')
              AND r.Status IN(N'Pending',N'Failed') AND r.SourceVersion=1;
            """, connection);
        command.Parameters.AddWithValue("@Tenant", tenantId);
        command.Parameters.AddWithValue("@Business", businessId);
        command.Parameters.AddWithValue("@Document", documentId);
        command.Parameters.AddWithValue("@Type", documentType);
        if (await command.ExecuteScalarAsync(cancellationToken) is long version)
            await reporting.RequestProjectionAsync(businessId, documentId, documentType,
                cancellationToken, version);
    }
}
