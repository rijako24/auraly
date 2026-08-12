using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Extensions.Options;

namespace Auraly.Pos.Edge.Host;

/// <summary>
/// Initializes the local POS store without requiring an enrolled device.
/// </summary>
public static class PosStorageBootstrap
{
    public static async Task InitializeAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var connectionString = $"Data Source={fullPath}";
        var keyDirectory = Path.Combine(directory ?? AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(keyDirectory);
        var clock = TimeProvider.System;
        var ids = new Uuid7AuralyIdGenerator(clock);
        var identities = new PosLocalIdentityStore(connectionString, keyDirectory, ids, clock);
        var leaseVerifier = new PosOfflineLeaseVerifier(Options.Create(new PosOfflineLeaseTrustOptions()));
        var leases = new PosOfflineLeaseStore(connectionString, Guid.Empty, Guid.Empty, leaseVerifier, clock);
        var catalog = new PosCatalogStore(connectionString);
        var drafts = new PosDraftStore(connectionString, ids, clock);
        var sales = new PosEdgeSaleStore(
            connectionString,
            new ConfirmOfflineSaleService(
                new PermissionAuthorizer(new PosLocalPermissionProvider(new PosLocalSessionAccessor()))));
        var issuance = new PosDraftIssuanceStore(connectionString, ids, clock);

        // EF EnsureCreated must run before the hand-written stores create their
        // tables; otherwise an existing SQLite file makes EF skip its model.
        await sales.InitializeAsync(cancellationToken);
        await issuance.InitializeAsync(cancellationToken);
        await identities.InitializeAsync(cancellationToken);
        await leases.InitializeAsync(cancellationToken);
        await catalog.InitializeAsync(cancellationToken);
        await drafts.InitializeAsync(cancellationToken);
    }
}