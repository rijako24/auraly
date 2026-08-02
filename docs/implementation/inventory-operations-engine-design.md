# Inventory operations engine slice

**Implemented:** 2026-08-01
**Branch:** `feature/auraly-commerce-accounting-engine`

## Scope

This slice connects four inventory documents end to end:

- stock count (`StockCount`, Auraly prefix `CTI`);
- inventory adjustment (`InventoryAdjustment`, `AJI`);
- warehouse transfer (`WarehouseTransfer`, `TRB`);
- product conversion (`ProductConversion`, `CNV`).

It does not introduce another engine, receipt table, SQL job or polling loop. Every
confirmed document uses the existing durable path:

```text
authenticated API
  -> InventoryOperations + InventoryOperationLines
  -> DocumentProcessingJobs + immutable payload
  -> one broker message for that document
  -> registered document handler
  -> InventoryBalances + InventoryMovements + server outbox
```

The document, number, immutable payload, processing sequence and durable job are
accepted in one serializable SQL transaction.

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

A transfer locks source and destination keys in canonical warehouse/product order.
The source exit and destination receipt run in the same transaction. The source
average cost travels with the quantity, so total inventory value is preserved.
Neither side can commit independently.

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
- `inventory.transfers.confirm`;
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

- explicit damage/average module;
- reversal documents instead of deletion;
- accounting journals and period controls consuming the durable operational facts;
- web screens for counts, adjustments, transfers and conversions;
- evolution from the current connected `InventoryMovements` foundation to the
  header/line kardex model when reporting requirements require it.
