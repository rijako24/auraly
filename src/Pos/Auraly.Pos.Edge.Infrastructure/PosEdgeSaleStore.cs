using System.Data;
using System.Data.Common;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
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
    DateOnly ValidUntil,
    Guid FiscalAuthorizationId = default);

public sealed record OfflineSalePayment(
    string MethodCode,
    decimal Amount,
    string? Reference = null);

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
    IReadOnlyCollection<OfflineSaleLine> Lines,
    Guid DeviceId = default,
    IReadOnlyCollection<OfflineSalePayment>? Payments = null);

public sealed record PosFiscalNumberPreview(
    Guid SeriesId,
    string Prefix,
    long Consecutive,
    string FullNumber,
    bool IsAvailable);

public sealed record PosEdgeIssueResult(
    DocumentId DocumentId,
    string FiscalNumber,
    string Cufe,
    string QrPayload,
    decimal Total,
    Guid OutboxMessageId,
    bool WasAlreadyIssued);

public sealed record PosEdgeOutboxItem(
    Guid MessageId,
    DocumentId DocumentId,
    string Type,
    string Payload,
    int AttemptCount,
    string Status = PosOutboxStatus.Pending,
    DateTimeOffset? NextAttemptAt = null,
    DateTimeOffset? LeaseAcquiredAt = null,
    string? LastError = null,
    string? RemoteStatus = null,
    Guid? ServerReceiptId = null);

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
        await UpgradeFiscalSeriesAsync(context, cancellationToken);
        await UpgradeOutboxAsync(context, cancellationToken);
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
                FiscalAuthorizationId = provision.FiscalAuthorizationId == Guid.Empty
                    ? provision.SeriesId
                    : provision.FiscalAuthorizationId,
                NextConsecutive = provision.RangeStart,
                RangeEnd = provision.RangeEnd,
                ValidUntil = provision.ValidUntil,
                IsActive = true
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await BackfillFiscalAuthorizationAsync(
                context,
                provision.SeriesId,
                provision.FiscalAuthorizationId == Guid.Empty
                    ? provision.SeriesId
                    : provision.FiscalAuthorizationId,
                cancellationToken);
        }
    }

    public async Task<PosFiscalNumberPreview> PreviewNextFiscalNumberAsync(
        RegisterId registerId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        var cursor = await context.FiscalSeriesCursors
            .AsNoTracking()
            .SingleAsync(row => row.RegisterId == registerId.Value, cancellationToken);
        var issueDate = DateOnly.FromDateTime(issuedAt.Date);
        var available = cursor.IsActive &&
                        issueDate <= cursor.ValidUntil &&
                        cursor.NextConsecutive <= cursor.RangeEnd;
        return new PosFiscalNumberPreview(
            cursor.SeriesId,
            cursor.Prefix,
            cursor.NextConsecutive,
            $"{cursor.Prefix}{cursor.NextConsecutive}",
            available);
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
                PosSaleContractSerializer.Deserialize(existing.FiscalSnapshotJson).FiscalSnapshot.QrPayload,
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
        var upload = BuildUploadContract(
            command,
            confirmed,
            snapshot,
            fiscalNumber,
            cursor.FiscalAuthorizationId);
        var payload = PosSaleContractSerializer.Serialize(upload);

        context.IssuedSales.Add(new IssuedSaleRow
        {
            DocumentId = command.DocumentId.Value,
            FiscalNumber = fiscalNumber.FullNumber,
            Cufe = snapshot.Cufe,
            Total = confirmed.Invoice.PayableAmount,
            IssuedAt = command.IssuedAt,
            FiscalSnapshotJson = payload
        });
        context.Outbox.Add(new PosOutboxRow
        {
            MessageId = confirmed.OutboxMessage.Id,
            DocumentId = command.DocumentId.Value,
            Type = confirmed.OutboxMessage.Type,
            Payload = payload,
            Status = PosOutboxStatus.Pending,
            CreatedAt = command.IssuedAt
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PosEdgeIssueResult(
            command.DocumentId,
            fiscalNumber.FullNumber,
            snapshot.Cufe,
            snapshot.QrPayload,
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
            .Where(row => row.Status == PosOutboxStatus.Pending ||
                          row.Status == PosOutboxStatus.RetryScheduled)
            .OrderBy(row => row.MessageId)
            .Select(ToOutboxItem)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PosEdgeOutboxItem?> ClaimNextOutboxAsync(
        DateTimeOffset now,
        TimeSpan leaseTimeout,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var staleBefore = now - leaseTimeout;
        var candidates = await context.Outbox
            .Where(item =>
                item.Status == PosOutboxStatus.Pending ||
                item.Status == PosOutboxStatus.RetryScheduled ||
                item.Status == PosOutboxStatus.Uploading)
            .ToArrayAsync(cancellationToken);
        var row = candidates
            .Where(item =>
                item.Status == PosOutboxStatus.Pending ||
                (item.Status == PosOutboxStatus.RetryScheduled &&
                 (item.NextAttemptAt == null || item.NextAttemptAt <= now)) ||
                (item.Status == PosOutboxStatus.Uploading &&
                 item.LeaseAcquiredAt != null &&
                 item.LeaseAcquiredAt <= staleBefore))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.MessageId)
            .FirstOrDefault();
        if (row is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        row.Status = PosOutboxStatus.Uploading;
        row.AttemptCount++;
        row.LastAttemptAt = now;
        row.LeaseAcquiredAt = now;
        row.NextAttemptAt = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToOutboxItem.Compile().Invoke(row);
    }

    public async Task<PosEdgeOutboxItem?> GetOutboxAsync(
        DocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        return await context.Outbox
            .AsNoTracking()
            .Where(row => row.DocumentId == documentId.Value)
            .Select(ToOutboxItem)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task MarkUploadedAsync(
        Guid messageId,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken = default)
    {
        await MarkUploadedAsync(
            messageId,
            new PosSaleUploadResponse(
                Guid.Empty,
                Guid.Empty,
                PosSaleRemoteStatuses.FiscalVerified,
                string.Empty,
                null,
                false,
                uploadedAt,
                uploadedAt,
                null),
            uploadedAt,
            cancellationToken);
    }

    public async Task MarkUploadedAsync(
        Guid messageId,
        PosSaleUploadResponse response,
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

        if (row.AttemptCount == 0)
        {
            row.AttemptCount = 1;
        }

        row.Status = PosOutboxStatus.Uploaded;
        row.UploadedAt = uploadedAt;
        row.LeaseAcquiredAt = null;
        row.LastError = null;
        row.RemoteStatus = response.Status;
        row.ServerReceiptId = response.ReceiptId == Guid.Empty ? null : response.ReceiptId;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFiscalIntegrityConflictAsync(
        Guid messageId,
        PosSaleUploadResponse response,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        var row = await context.Outbox.SingleAsync(
            item => item.MessageId == messageId,
            cancellationToken);
        row.Status = PosOutboxStatus.FiscalIntegrityConflict;
        row.LeaseAcquiredAt = null;
        row.NextAttemptAt = null;
        row.LastError = response.Detail;
        row.RemoteStatus = response.Status;
        row.ServerReceiptId = response.ReceiptId == Guid.Empty ? null : response.ReceiptId;
        row.UploadedAt = occurredAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ScheduleRetryAsync(
        Guid messageId,
        DateTimeOffset nextAttemptAt,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        var row = await context.Outbox.SingleAsync(
            item => item.MessageId == messageId,
            cancellationToken);
        row.Status = PosOutboxStatus.RetryScheduled;
        row.LeaseAcquiredAt = null;
        row.NextAttemptAt = nextAttemptAt;
        row.LastError = Truncate(error);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedPermanentAsync(
        Guid messageId,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var context = new PosEdgeDbContext(_options);
        var row = await context.Outbox.SingleAsync(
            item => item.MessageId == messageId,
            cancellationToken);
        row.Status = PosOutboxStatus.FailedPermanent;
        row.LeaseAcquiredAt = null;
        row.NextAttemptAt = null;
        row.LastError = Truncate(error);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static PosSaleUploadRequest BuildUploadContract(
        PosEdgeIssueCommand command,
        ConfirmedOfflineSale confirmed,
        ImmutableFiscalSnapshot snapshot,
        FiscalNumberAssignment fiscalNumber,
        Guid fiscalAuthorizationId)
    {
        var lines = command.Lines
            .Select((line, index) => new PosSaleLineContract(
                index + 1,
                line.Product.ProductId.Value,
                line.Product.Name,
                line.Product.TaxCode,
                line.Quantity,
                line.UnitPrice,
                line.Discount,
                line.TaxAmount,
                decimal.Round(
                    (line.Quantity * line.UnitPrice) - line.Discount,
                    2,
                    MidpointRounding.ToEven),
                decimal.Round(
                    (line.Quantity * line.UnitPrice) - line.Discount,
                    2,
                    MidpointRounding.ToEven) + line.TaxAmount))
            .ToArray();
        var payments = command.Payments is { Count: > 0 }
            ? command.Payments
                .Select((payment, index) => new PosSalePaymentContract(
                    index + 1,
                    payment.MethodCode,
                    payment.Amount,
                    payment.Reference))
                .ToArray()
            : [new PosSalePaymentContract(1, "Cash", confirmed.Invoice.PayableAmount, null)];
        if (payments.Sum(payment => payment.Amount) != confirmed.Invoice.PayableAmount)
        {
            throw new InvalidOperationException("Payments must equal the payable amount.");
        }

        var taxes = lines
            .GroupBy(line => line.TaxCode, StringComparer.Ordinal)
            .Select(group => new PosSaleTaxContract(
                group.Key,
                group.Sum(line => line.TaxAmount)))
            .OrderBy(tax => tax.Code, StringComparer.Ordinal)
            .ToArray();
        return new PosSaleUploadRequest(
            command.Register.TenantId.Value,
            command.Register.BusinessId.Value,
            command.Register.LocationId.Value,
            command.Register.WarehouseId.Value,
            command.Register.RegisterId.Value,
            command.DeviceId,
            command.DocumentId.Value,
            new PosSaleFiscalSnapshotContract(
                fiscalNumber.SeriesId,
                fiscalAuthorizationId,
                fiscalNumber.AuthorizationNumber,
                PosSaleDocumentTypes.Invoice,
                snapshot.FiscalNumber,
                snapshot.Prefix,
                snapshot.Consecutive,
                snapshot.IssuedAt,
                command.SupplierTaxId,
                snapshot.CustomerIdentification,
                (int)command.Environment,
                command.TechnicalKey.Version,
                taxes,
                snapshot.UntaxedAmount,
                snapshot.TaxAmount,
                snapshot.PayableAmount,
                snapshot.Cufe,
                snapshot.QrPayload),
            lines,
            payments);
    }

    private static readonly System.Linq.Expressions.Expression<Func<PosOutboxRow, PosEdgeOutboxItem>>
        ToOutboxItem = row => new PosEdgeOutboxItem(
            row.MessageId,
            new DocumentId(row.DocumentId),
            row.Type,
            row.Payload,
            row.AttemptCount,
            row.Status,
            row.NextAttemptAt,
            row.LeaseAcquiredAt,
            row.LastError,
            row.RemoteStatus,
            row.ServerReceiptId);

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000];

    private static async Task BackfillFiscalAuthorizationAsync(
        PosEdgeDbContext context,
        Guid seriesId,
        Guid fiscalAuthorizationId,
        CancellationToken cancellationToken)
    {
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE FiscalSeriesCursors " +
            "SET FiscalAuthorizationId = $authorizationId " +
            "WHERE SeriesId = $seriesId " +
            "AND FiscalAuthorizationId = '00000000-0000-0000-0000-000000000000';";
        var authorizationParameter = command.CreateParameter();
        authorizationParameter.ParameterName = "$authorizationId";
        authorizationParameter.Value = fiscalAuthorizationId.ToString("D").ToUpperInvariant();
        command.Parameters.Add(authorizationParameter);
        var seriesParameter = command.CreateParameter();
        seriesParameter.ParameterName = "$seriesId";
        seriesParameter.Value = seriesId.ToString("D").ToUpperInvariant();
        command.Parameters.Add(seriesParameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static async Task UpgradeFiscalSeriesAsync(
        PosEdgeDbContext context,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('FiscalSeriesCursors');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("FiscalAuthorizationId"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "ALTER TABLE FiscalSeriesCursors ADD COLUMN FiscalAuthorizationId TEXT NOT NULL " +
                "DEFAULT '00000000-0000-0000-0000-000000000000';";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpgradeOutboxAsync(
        PosEdgeDbContext context,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('Outbox');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NextAttemptAt"] = "TEXT NULL",
            ["LeaseAcquiredAt"] = "TEXT NULL",
            ["LastAttemptAt"] = "TEXT NULL",
            ["LastError"] = "TEXT NULL",
            ["RemoteStatus"] = "TEXT NULL",
            ["ServerReceiptId"] = "TEXT NULL"
        };
        foreach (var addition in additions.Where(addition => !columns.Contains(addition.Key)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"ALTER TABLE Outbox ADD COLUMN {addition.Key} {addition.Value};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

