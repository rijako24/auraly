using System.Data.Common;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.BuildingBlocks.Infrastructure.Persistence;

// The caller retains the transaction; modules share the existing numbering writer.
public interface IOperationalDocumentNumberAllocator
{
    Task<AuralyDocumentNumberAssignment> AllocateAsync(DbConnection connection, DbTransaction transaction,
        Guid businessId, string documentType, DateTimeOffset now, CancellationToken cancellationToken);
}
