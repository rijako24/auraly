using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlInventoryLedgerWriter
{
    private sealed record PoolHead(Guid BusinessId, Guid ProductId, bool IsDamageWarehouse,
        Guid EpochId, long LastProcessingSequence);
    private sealed record PoolEpoch(Guid EpochId, Guid ProductId, string ScopeKind,
        bool IsDamageWarehouse, IReadOnlyList<Guid> Members);
    private sealed record NewPoolEpoch(Guid EpochId, Guid ProductId, string ScopeKind,
        bool IsDamageWarehouse, string MemberBusinessIdsJson, decimal InitialQuantity);
    private sealed record PoolAssignment(Guid BusinessId, Guid ProductId, bool IsDamageWarehouse,
        Guid EpochId, long LastProcessingSequence, bool Existed, Guid? PreviousEpochId, long? PreviousSequence);

    // Provenance context follows the exact valuation pool supplied by the ledger.
    // It never supplies a quantity, price or valuation back to the calculator.
    private sealed class CostPoolContexts
    {
        private readonly LoadedState loaded;
        private readonly Guid businessId;
        private readonly long sequence;
        private readonly IAuralyIdGenerator ids;
        private readonly Guid[] members;
        private readonly Dictionary<(Guid Business, Guid Product, bool Damage), PoolHead> heads;
        private readonly Dictionary<Guid, PoolEpoch> epochs;
        private readonly Dictionary<(Guid Business, Guid Product, bool Damage), long> observedSequences;
        private readonly Dictionary<(Guid Product, bool Damage), Guid> selected = [];
        private readonly Dictionary<(Guid Business, Guid Product, bool Damage), PoolAssignment> assignments = [];
        public List<NewPoolEpoch> NewEpochs { get; } = [];
        public IReadOnlyCollection<PoolAssignment> Assignments => assignments.Values;

        public CostPoolContexts(LoadedState loaded, Guid businessId, long sequence, IAuralyIdGenerator ids)
        {
            this.loaded = loaded;
            this.businessId = businessId;
            this.sequence = sequence;
            this.ids = ids;
            members = loaded.MemberBusinesses.Order().ToArray();
            if (members.Length == 0 || !members.Contains(businessId) || members.Distinct().Count() != members.Length)
                throw new InvalidOperationException("The inventory cost pool has invalid business membership.");
            heads = loaded.Heads.ToDictionary(head => (head.BusinessId, head.ProductId, head.IsDamageWarehouse));
            epochs = loaded.Epochs.ToDictionary(epoch => epoch.EpochId);
            if (epochs.Values.Any(epoch => epoch.Members.Count == 0 || epoch.Members.Contains(Guid.Empty) ||
                epoch.Members.Distinct().Count() != epoch.Members.Count) || heads.Values.Any(head => !epochs.ContainsKey(head.EpochId)))
                throw new InvalidOperationException("The inventory cost context is missing or outside the tenant.");
            observedSequences = loaded.Balances.GroupBy(balance => (balance.BusinessId, balance.ProductId, balance.IsDamageWarehouse))
                .ToDictionary(group => group.Key, group => group.Max(balance => balance.LastProcessingSequence));
        }

        public Guid Resolve(Guid productId, bool damage, decimal quantityBefore)
        {
            if (selected.TryGetValue((productId, damage), out var existing)) return existing;
            Guid[] poolMembers = damage ? [businessId] : members;
            var kind = damage ? "DamageWarehouse" : loaded.SharesPrices ? "SharedTenant" : "Business";
            Guid? candidate = null;
            var continuous = true;
            foreach (var member in poolMembers)
            {
                var key = (member, productId, damage);
                if (!heads.TryGetValue(key, out var head) ||
                    head.LastProcessingSequence != observedSequences.GetValueOrDefault(key))
                {
                    continuous = false;
                    continue;
                }
                if (candidate is { } candidateId && candidateId != head.EpochId) continuous = false;
                candidate ??= head.EpochId;
            }
            if (candidate is { } previous)
            {
                var epoch = epochs[previous];
                continuous &= epoch.ProductId == productId && epoch.IsDamageWarehouse == damage &&
                    epoch.ScopeKind == kind && epoch.Members.Order().SequenceEqual(poolMembers);
            }

            Guid epochId;
            if (continuous && candidate is { } retained) epochId = retained;
            else
            {
                epochId = ids.NewId();
                NewEpochs.Add(new(epochId, productId, kind, damage, JsonSerializer.Serialize(poolMembers), quantityBefore));
                foreach (var member in poolMembers)
                    Assign(member, productId, damage, epochId,
                        observedSequences.GetValueOrDefault((member, productId, damage)));
            }
            Assign(businessId, productId, damage, epochId, sequence);
            selected.Add((productId, damage), epochId);
            return epochId;
        }

        private void Assign(Guid member, Guid product, bool damage, Guid epoch, long observedSequence)
        {
            var key = (member, product, damage);
            heads.TryGetValue(key, out var previous);
            assignments[key] = new(member, product, damage, epoch, observedSequence,
                previous is not null, previous?.EpochId, previous?.LastProcessingSequence);
        }
    }
}
