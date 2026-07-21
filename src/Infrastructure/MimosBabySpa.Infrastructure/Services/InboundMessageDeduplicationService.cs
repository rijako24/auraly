using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Services;

public class InboundMessageDeduplicationService : IInboundMessageDeduplicationService
{
    private const string ReceivedStatus = "Received";
    private const string QueuedStatus = "Queued";
    private const string ProcessingStatus = "Processing";
    private const string ProcessedStatus = "Processed";
    private const string FailedStatus = "Failed";
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InboundMessageDeduplicationService> _logger;

    public InboundMessageDeduplicationService(
        ApplicationDbContext context,
        ILogger<InboundMessageDeduplicationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> TryBeginProcessingAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
            return true;

        var now = DateTime.UtcNow;
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedMessageId = NormalizeMessageId(providerMessageId);

        _context.InboundMessageReceipts.Add(new InboundMessageReceipt
        {
            InboundMessageReceiptId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = normalizedProvider,
            ProviderMessageId = normalizedMessageId,
            Status = ProcessingStatus,
            ReceivedAtUtc = now,
            ProcessingStartedAtUtc = now,
            UpdatedAtUtc = now,
            AttemptCount = 1
        });

        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _context.ChangeTracker.Clear();

            var existing = await FindReceiptAsync(businessId, normalizedProvider, normalizedMessageId, ct);
            if (existing is null)
                return false;

            if (CanRetryProcessing(existing, now))
            {
                var previousStatus = existing.Status;
                existing.Status = ProcessingStatus;
                existing.ProcessingStartedAtUtc = now;
                existing.UpdatedAtUtc = now;
                existing.LastError = null;
                existing.ProcessedAtUtc = null;
                existing.AttemptCount++;

                await _context.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Reintentando mensaje inbound {ProviderMessageId} para negocio {BusinessId}; estado anterior {Status}",
                    normalizedMessageId,
                    businessId,
                    previousStatus);

                return true;
            }

            return false;
        }
    }

    public async Task<bool> TryRecordReceivedAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        string userNumber,
        string? customerName,
        string rawEntryJson,
        DateTime receivedAtUtc,
        DateTime processingDueAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
            return false;

        var normalizedProvider = NormalizeProvider(provider);
        var normalizedMessageId = NormalizeMessageId(providerMessageId);
        var now = DateTime.UtcNow;

        _context.InboundMessageReceipts.Add(new InboundMessageReceipt
        {
            InboundMessageReceiptId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = normalizedProvider,
            ProviderMessageId = normalizedMessageId,
            UserNumber = userNumber.Trim(),
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim(),
            RawEntryJson = rawEntryJson,
            Status = ReceivedStatus,
            ReceivedAtUtc = receivedAtUtc,
            ProcessingDueAtUtc = processingDueAtUtc,
            ProcessingStartedAtUtc = DateTime.MinValue,
            UpdatedAtUtc = now
        });

        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task MarkQueuedAsync(
        Guid businessId,
        string provider,
        string providerMessageId,
        DateTime processingDueAtUtc,
        CancellationToken ct = default)
    {
        var receipt = await FindReceiptAsync(
            businessId,
            NormalizeProvider(provider),
            NormalizeMessageId(providerMessageId),
            ct);

        if (receipt is null || receipt.Status == ProcessedStatus)
            return;

        var now = DateTime.UtcNow;
        receipt.Status = QueuedStatus;
        receipt.QueuedAtUtc = now;
        receipt.ProcessingDueAtUtc = processingDueAtUtc;
        receipt.UpdatedAtUtc = now;
        receipt.LastError = null;

        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<InboundMessageReceipt>> GetPendingConversationMessagesAsync(
        Guid businessId,
        string provider,
        string userNumber,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var leaseCutoff = now.Subtract(ProcessingLease);
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedUserNumber = userNumber.Trim();

        return await _context.InboundMessageReceipts
            .Where(r =>
                r.BusinessId == businessId &&
                r.Provider == normalizedProvider &&
                r.UserNumber == normalizedUserNumber &&
                (r.Status == ReceivedStatus ||
                 r.Status == QueuedStatus ||
                 r.Status == FailedStatus ||
                 (r.Status == ProcessingStatus && r.ProcessingStartedAtUtc <= leaseCutoff)))
            .OrderBy(r => r.ReceivedAtUtc)
            .ThenBy(r => r.ProviderMessageId)
            .ToListAsync(ct);
    }

    public Task<bool> HasConversationMessageReceivedAfterAsync(
        Guid businessId,
        string provider,
        string userNumber,
        DateTime receivedAfterUtc,
        CancellationToken ct = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedUserNumber = userNumber.Trim();
        return _context.InboundMessageReceipts.AnyAsync(receipt =>
            receipt.BusinessId == businessId
            && receipt.Provider == normalizedProvider
            && receipt.UserNumber == normalizedUserNumber
            && receipt.ReceivedAtUtc > receivedAfterUtc,
            ct);
    }

    public async Task MarkProcessingAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
        CancellationToken ct = default)
    {
        var receipts = await GetReceiptsAsync(businessId, provider, providerMessageIds, ct);
        var now = DateTime.UtcNow;

        foreach (var receipt in receipts)
        {
            receipt.Status = ProcessingStatus;
            receipt.ProcessingStartedAtUtc = now;
            receipt.UpdatedAtUtc = now;
            receipt.AttemptCount++;
            receipt.LastError = null;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task MarkProcessedAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
        CancellationToken ct = default)
    {
        var receipts = await GetReceiptsAsync(businessId, provider, providerMessageIds, ct);
        var now = DateTime.UtcNow;

        foreach (var receipt in receipts)
        {
            receipt.Status = ProcessedStatus;
            receipt.ProcessedAtUtc = now;
            receipt.UpdatedAtUtc = now;
            receipt.LastError = null;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
        string error,
        CancellationToken ct = default)
    {
        var receipts = await GetReceiptsAsync(businessId, provider, providerMessageIds, ct);
        var now = DateTime.UtcNow;
        var truncatedError = error.Length > 4000 ? error[..4000] : error;

        foreach (var receipt in receipts)
        {
            receipt.Status = FailedStatus;
            receipt.UpdatedAtUtc = now;
            receipt.LastError = truncatedError;
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task<List<InboundMessageReceipt>> GetReceiptsAsync(
        Guid businessId,
        string provider,
        IEnumerable<string> providerMessageIds,
        CancellationToken ct)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var messageIds = providerMessageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeMessageId)
            .Distinct()
            .ToList();

        if (messageIds.Count == 0)
            return [];

        return await _context.InboundMessageReceipts
            .Where(r =>
                r.BusinessId == businessId &&
                r.Provider == normalizedProvider &&
                messageIds.Contains(r.ProviderMessageId))
            .ToListAsync(ct);
    }

    private Task<InboundMessageReceipt?> FindReceiptAsync(
        Guid businessId,
        string normalizedProvider,
        string normalizedMessageId,
        CancellationToken ct)
    {
        return _context.InboundMessageReceipts
            .SingleOrDefaultAsync(r =>
                r.BusinessId == businessId &&
                r.Provider == normalizedProvider &&
                r.ProviderMessageId == normalizedMessageId,
                ct);
    }

    private static bool CanRetryProcessing(InboundMessageReceipt receipt, DateTime now)
    {
        return receipt.Status == FailedStatus ||
            (receipt.Status == ProcessingStatus && receipt.ProcessingStartedAtUtc <= now.Subtract(ProcessingLease));
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant();

    private static string NormalizeMessageId(string providerMessageId) => providerMessageId.Trim();

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlException &&
            sqlException.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627);
    }
}
