# Inventory operations engine slice

**Implemented:** 2026-08-01
**Transfer flow revised:** 2026-08-27

## Scope

This slice connects four inventory documents end to end:

- stock count (`StockCount`, Auraly prefix `CTI`);
- inventory adjustment (`InventoryAdjustment`, `AJI`);
- warehouse transfer (`WarehouseTransfer`, `TRB`);
- product conversion (`ProductConversion`, `CNV`).

It does not introduce another engine or polling loop. Every confirmed stage uses
the existing durable path. Transfer receipts are child records of the same `TRB`
header, not an alternative inventory writer:

```text
authenticated API
  -> InventoryOperations + InventoryOperationLines
     (+ InventoryTransferReceipts for each destination confirmation)
  -> DocumentProcessingJobs + immutable payload
  -> one broker message for that document
  -> registered document handler
  -> InventoryBalances + InventoryMovements + server outbox
```

The document, number, immutable payload, processing sequence and durable job are
accepted in one serializable SQL transaction.

The interactive count grid can apply a count without first persisting a named
physical-count draft through `POST /api/commerce/v1/stock-counts/apply`. This is
an application-level convenience endpoint, not another writer: it idempotently
orchestrates the existing start and confirm contracts and still enters the same
ordered document engine and `SqlInventoryLedgerWriter` path.

## Count semantics

Starting a count freezes `BaseInventorySequence` and `SystemQuantityAtBase` per
product. Confirmation applies:

```text
quantity change = counted quantity - system quantity at count base
```

It applies that difference to the current balance. A receipt, sale or transfer
posted after the count began is therefore preserved. A count does not overwrite
the current quantity.

## Adjustment semantics

Adjustments require a non-zero signed quantity and an explicit reason. Negative
adjustments use current average cost and cannot create negative stock. An explicit
cost is accepted only for inbound adjustments; otherwise the current inventory
valuation supplies the cost.

## Transfer semantics

A user transfer has two ordered stages. `Dispatch` removes all lines from the
source and places their quantity and value in the hidden `TRA` warehouse. It
freezes `DispatchedQuantity`, `DispatchUnitCost` and `DispatchValue`, and the
header becomes `Dispatched` (pending entry). `Receipt` is recovered by the
destination; `ReceivedQuantity` defaults to the pending quantity but can be
edited between zero and that pending balance. Differences require a reason.

Partial receipts leave the remainder in `TRA` and the header as
`PartiallyReceived`; the final receipt changes it to `Received`. Each stage is
atomic and idempotent, and `RowVersion` prevents concurrent over-receipt.

The only immediate mode is the non-public `ImmediateSystem`, used for intrinsic
order effects: one multi-line document reserves the complete order in `PED`, and
one multi-line document releases it from `PED` to the sales warehouse before
invoicing.

## Conversion semantics

MVP conversions are `Split` (one input, multiple outputs) or `Merge` (multiple
inputs, one output). Inputs are valued using the authoritative average cost. Their
total value is allocated to outputs either by explicit weights totaling 100% or by
output quantity. The final monetary residue is assigned deterministically to the
last output. Inputs and outputs commit atomically; negative input stock is rejected.

## Ordering, retry and dead-letter

`DocumentProcessingJobs` remains the sole durable processing authority. The
existing business sequence orders all four documents with sales, returns and goods
receipts. One broker delivery processes one document. RabbitMQ uses five bounded
attempts; the fifth failed attempt becomes `DeadLettered`, advances the business
cursor and allows the next document. Failed attempts roll back every inventory,
line-result and outbox effect.

## Security and scope

The API revalidates JWT identity, tenant/business scope and operation permissions:

- `inventory.counts.confirm`;
- `inventory.adjustments.confirm`;
- `inventory.transfers.dispatch`;
- `inventory.transfers.receive`;
- `inventory.transfers.resolve-difference`;
- `inventory.conversions.confirm`.

Warehouses and active stock-managed products must belong to the authenticated
business. Client-supplied business identifiers never broaden the authenticated
scope.

## Accounting boundary

`CostCenterId`, reason, recognized unit cost, value change and immutable document
snapshot are preserved. The same transaction emits `inventory.operation.processed`
through the server outbox. This is the connected source for the accounting posting
slice; this delivery does not claim that the general ledger, Colombian chart of
accounts or period closing is implemented.

## Intentional next work

- inventory reversals and controlled correction documents;
- reversal documents instead of deletion;
- accounting journals and period controls consuming the durable operational facts;
- web screens for counts, adjustments, transfers and conversions;
- evolution from the current connected `InventoryMovements` foundation to the
  header/line kardex model when reporting requirements require it.
