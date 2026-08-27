using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.DocumentProcessing;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlDispatchCashDifferenceDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => DispatchAccountingDocumentTypes.CashDifference;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<DispatchCashDifferencePayload>(
            document.Payload,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException(
                "The dispatch cash-difference payload is invalid.");
        if (payload.DispatchSettlementId != document.DocumentId.Value ||
            payload.BusinessId != document.BusinessId.Value ||
            payload.TenantId != document.TenantId.Value || payload.Difference == 0 ||
            decimal.Round(payload.CashReceived - payload.ExpectedCash, 4) !=
            decimal.Round(payload.Difference, 4))
            throw new InvalidOperationException(
                "The dispatch cash-difference envelope does not match its payload.");

        await SqlAccountingPostingJobWriter.InsertAsync(
            sessions.Current, document, payload.OccurredAt, ids, timeProvider,
            cancellationToken);
    }
}
