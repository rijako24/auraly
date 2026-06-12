using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Services;

public class InboundMessageDeduplicationService : IInboundMessageDeduplicationService
{
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
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedMessageId = providerMessageId.Trim();

        _context.InboundMessageReceipts.Add(new InboundMessageReceipt
        {
            InboundMessageReceiptId = Guid.NewGuid(),
            BusinessId = businessId,
            Provider = normalizedProvider,
            ProviderMessageId = normalizedMessageId,
            Status = ProcessingStatus,
            ReceivedAtUtc = now,
            ProcessingStartedAtUtc = now,
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

            var existing = await _context.InboundMessageReceipts
                .SingleOrDefaultAsync(r =>
                    r.BusinessId == businessId &&
                    r.Provider == normalizedProvider &&
                    r.ProviderMessageId == normalizedMessageId,
                    ct);

            if (existing is null)
                return false;

            if (existing.Status == FailedStatus ||
                (existing.Status == ProcessingStatus && existing.ProcessingStartedAtUtc <= now.Subtract(ProcessingLease)))
            {
                var previousStatus = existing.Status;
                existing.Status = ProcessingStatus;
                existing.ProcessingStartedAtUtc = now;
                existing.UpdatedAtUtc = now;
                existing.LastError = null;
                existing.ProcessedAtUtc = null;

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
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var messageIds = providerMessageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqlException sqlException &&
            sqlException.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627);
    }
}
