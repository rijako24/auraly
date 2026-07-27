using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Fiscal.Core;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosEdgeSeriesProvision(
    Guid SeriesId,
    RegisterId RegisterId,
    string Prefix,
    string AuthorizationNumber,
    long RangeStart,
    long RangeEnd,
    DateOnly ValidUntil);

public sealed record PosEdgeIssueCommand(
    UserId UserId,
    DocumentId DocumentId,
    RegisterContext Register,
    DateTimeOffset IssuedAt,
    string SupplierTaxId,
    string CustomerIdentification,
    FiscalTechnicalKey TechnicalKey,
    FiscalEnvironment Environment,
    string QrValidationUrl,
    IReadOnlyCollection<OfflineSaleLine> Lines);

public sealed record PosEdgeIssueResult(
    DocumentId DocumentId,
    string FiscalNumber,
    string Cufe,
    decimal Total,
    Guid OutboxMessageId,
    bool WasAlreadyIssued);

public sealed record PosEdgeOutboxItem(
    Guid MessageId,
    DocumentId DocumentId,
    string Type,
    string Payload,
    int AttemptCount);

public sealed class PosEdgeSaleStore
{
    private readonly DbContextOptions<PosEdgeDbContext> _options;
    private readonly ConfirmOfflineSaleService _confirmationService;

    public PosEdgeSaleStore(string connectionString, ConfirmOfflineSaleService confirmationService)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        }

        _options = new DbContextOptionsBuilder<PosEdgeDbContext>()
            .UseSqlite(connectionString)
            .Options;
        _confirmationService = confirmationService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task ProvisionSeriesAsync(
        PosEdgeSeriesProvision provision,
        CancellationToken cancellationToken = default)
    {
        if (provision.RangeStart <= 0 || provision.RangeEnd < provision.RangeStart)
        {
            throw new ArgumentOutOfRangeException(nameof(provision));
        }

        await using var context = new PosEdgeDbContext(_options);
        var current = await context.FiscalSeriesCursors
            .SingleOrDefaultAsync(
                row => row.RegisterId == provision.RegisterId.Value,
                cancellationToken);
        if (current is not null && current.SeriesId != provision.SeriesId)
        {
            throw new InvalidOperationException("The register already has another provisioned fiscal series.");
        }

        if (current is null)
        {
            context.FiscalSeriesCursors.Add(new FiscalSeriesCursorRow
            {
                SeriesId = provision.SeriesId,
                RegisterId = provision.RegisterId.Value,
                Prefix = provision.Prefix.Trim().ToUpperInvariant(),
                AuthorizationNumber = provision.AuthorizationNumber.Trim(),
                NextConsecutive = provision.RangeStart,
                RangeEnd = provision.RangeEnd,
                ValidUntil = provision.ValidUntil,
                IsActive = true
            });
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<PosEdgeIssueResult> IssueAsync(
        PosEdgeIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        var existing = await context.IssuedSales
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.DocumentId == command.DocumentId.Value,
                cancellationToken);
        if (existing is not null)
        {
            var existingOutbox = await context.Outbox
                .AsNoTracking()
                .SingleAsync(
                    row => row.DocumentId == command.DocumentId.Value,
                    cancellationToken);
            return new PosEdgeIssueResult(
                command.DocumentId,
                existing.FiscalNumber,
                existing.Cufe,
                existing.Total,
                existingOutbox.MessageId,
                WasAlreadyIssued: true);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var cursor = await context.FiscalSeriesCursors
            .SingleAsync(
                row => row.RegisterId == command.Register.RegisterId.Value,
                cancellationToken);
        var issueDate = DateOnly.FromDateTime(command.IssuedAt.Date);
        if (!cursor.IsActive || issueDate > cursor.ValidUntil)
        {
            throw new InvalidOperationException("The fiscal series is inactive or expired.");
        }

        if (cursor.NextConsecutive > cursor.RangeEnd)
        {
            throw new InvalidOperationException("The fiscal series is exhausted.");
        }

        var consecutive = cursor.NextConsecutive;
        cursor.NextConsecutive++;
        var fiscalNumber = new FiscalNumberAssignment(
            cursor.SeriesId,
            cursor.Prefix,
            consecutive,
            $"{cursor.Prefix}{consecutive}",
            cursor.AuthorizationNumber);
        var confirmed = _confirmationService.Confirm(new ConfirmOfflineSaleCommand(
            command.UserId,
            command.DocumentId,
            command.Register,
            fiscalNumber,
            command.IssuedAt,
            command.SupplierTaxId,
            command.CustomerIdentification,
            command.TechnicalKey,
            command.Environment,
            command.QrValidationUrl,
            command.Lines));
        var snapshot = confirmed.Invoice.FiscalSnapshot
            ?? throw new InvalidOperationException("The sale was not fiscally frozen.");

        context.IssuedSales.Add(new IssuedSaleRow
        {
            DocumentId = command.DocumentId.Value,
            FiscalNumber = fiscalNumber.FullNumber,
            Cufe = snapshot.Cufe,
            Total = confirmed.Invoice.PayableAmount,
            IssuedAt = command.IssuedAt,
            FiscalSnapshotJson = JsonSerializer.Serialize(snapshot)
        });
        context.Outbox.Add(new PosOutboxRow
        {
            MessageId = confirmed.OutboxMessage.Id,
            DocumentId = command.DocumentId.Value,
            Type = confirmed.OutboxMessage.Type,
            Payload = confirmed.OutboxMessage.Payload,
            Status = PosOutboxStatus.Pending,
            CreatedAt = command.IssuedAt
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PosEdgeIssueResult(
            command.DocumentId,
            fiscalNumber.FullNumber,
            snapshot.Cufe,
            confirmed.Invoice.PayableAmount,
            confirmed.OutboxMessage.Id,
            WasAlreadyIssued: false);
    }

    public async Task<IReadOnlyCollection<PosEdgeOutboxItem>> GetPendingOutboxAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        return await context.Outbox
            .AsNoTracking()
            .Where(row => row.Status == PosOutboxStatus.Pending)
            .OrderBy(row => row.MessageId)
            .Select(row => new PosEdgeOutboxItem(
                row.MessageId,
                new DocumentId(row.DocumentId),
                row.Type,
                row.Payload,
                row.AttemptCount))
            .ToArrayAsync(cancellationToken);
    }

    public async Task MarkUploadedAsync(
        Guid messageId,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        var row = await context.Outbox.SingleAsync(
            item => item.MessageId == messageId,
            cancellationToken);
        if (row.Status == PosOutboxStatus.Uploaded)
        {
            return;
        }

        row.AttemptCount++;
        row.Status = PosOutboxStatus.Uploaded;
        row.UploadedAt = uploadedAt;
        await context.SaveChangesAsync(cancellationToken);
    }
}
