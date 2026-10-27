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

Adjustments require a non-zero signed quantity and an explicit reason. Outbound
adjustments use current average cost. The current signed-adjustment contract
preserves negative balances and their last average through zero, as exercised by
`Every_adjustment_uses_the_canonical_average_cost_across_zero_and_negative_stock`.
The earlier assertion that every negative adjustment rejects negative stock did
not describe that runtime; batching preserves the existing admission behavior
requested by the user. An explicit cost affects valuation only for inbound
adjustments; otherwise the current inventory valuation supplies the cost.

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

## Receipt-origin attribution in the existing ledger — 2026-10-25

`SqlInventoryLedgerWriter.PostBatchAsync` also records quantity provenance. The
pure domain calculation `InventoryOriginAttribution` consumes the oldest open
origins of the physical business/warehouse/product balance. It does not calculate a unit cost,
validate stock availability or change the average-cost pool. Production input
consumes ingredient origins; production output starts its own product origin.
This scope does not propagate a later ingredient charge into finished products.

`inventory.MovementOrigins` contains immutable signed portions of the existing
kardex movement. `inventory.OriginPositions` is its derived open quantity detail,
not an independently editable stock or value balance. Only the existing ledger
writer writes both, in the same session transaction as `InventoryBalances`,
kardex and outbox. There is no additional job, engine, writer, queue or replay
effect. Existing document-engine identity and sequence remain authoritative.

`InventoryBalances` carries a nullable origin context and observed physical
sequence for that warehouse/product. These are coverage metadata, not another
quantity or value. Before the first operation, positive local stock without compatible coverage
is explicitly unattributed. It is consumed before new known receipts. A gap left
by an older physical writer starts another context without guessing receipts or
blocking ordinary sales, purchases or returns. Prior context detail is retained
as historical evidence and excluded from current attribution. In a continuous
context, a quantity mismatch is an error rather than a silent reconstruction.
Historical receipts still require the separately approved explicit reconciliation
before automatic late-cost valuation; this does not reconcile all production use.

Incoming quantity that covers a negative balance is recorded but is not retained
as available receipt origin. Unbacked issues have an explicit unknown origin.
Transfers carry the exact portions of their outgoing leg, including transit and
damage-warehouse legs. Sales returns restore portions of the referenced issue;
previous partial restorations are aggregated from immutable return facts. Old
issues without those facts return unattributed quantity and retain the native
return owner's existing quantity limits. Purchase returns record the original
receipt reference and consume the warehouse's current FIFO portions; that
commercial reference does not claim that already consumed original units remain.
Value-only adjustments preserve this coverage and never consume quantities.

Load/persist remain the same two SQL commands. Reads are limited to open detail
of touched warehouse/product contexts and the explicitly referenced movements; no entire
historical kardex is loaded. A lookup groups open detail before the domain loop.
Writes replace only the touched current-context detail and append immutable
movement portions. Acceptance budget: 1/20/500 distinct products, twenty warm
samples, two commands and p95 below the existing two-second local gate. Tests
must also cover native physical flows, partial returns, transfer lineage,
negative stock, legacy gaps, value-only coverage and a failure after balance and
movement writes. These checks are in progress; no full-cost-revision completion
or whole-change audit is claimed here.

User correction on 2026-10-25: a receipt of five units in warehouse A followed
by an issue of three from warehouse B leaves all five receipt units in A.
The attempted attribution across the average valuation pool was incorrect and
was removed before deployment. Average valuation does not move physical stock
or its origins between warehouses or shared-price businesses. Only an explicit
transfer carries those origins. Regression cases exercise both independent
warehouses and businesses sharing prices, plus the actual transfer legs.

The stock-reconstruction architecture guard initially also rejected the bounded
sum of portions already returned to one issue. That sum does not supply physical
stock: it only prevents restoring an origin twice. The guard recognizes only a
qualified, positive `MovementOrigins` aggregate constrained by both referenced
movement and origin. Unqualified sums, kardex sums, CTE-based stock sums, and a
second forbidden sum in the same file retain their rejection tests. No runtime
stock authority or historical reconstruction policy changes.

Compatibility is additive and has no historical backfill. Publish the schema
before its consuming binary. An older writer can still use the nullable columns;
its physical sequence invalidates coverage explicitly. Rolling back that binary
must retain the additive tables and immutable facts; do not delete provenance or
replay previously completed physical documents. No remote deployment is authorized.

Correction verification on 2026-10-25: the canonical Release DACPAC and the five
affected Debug projects compile. All 985 Foundation cases pass. The isolated
API/SQL run passes all 132 cases in `auraly-warehouse-correction-regression.trx`,
including the two warehouse/shared-price counterexamples, explicit transfers,
partial returns, ingredient production, purchase receipt/return, both payment
captures, advances, partial cost associations, completed replay and both schema
upgrade preservation cases. These replace the rejected pool-attribution test
expectations; that rejected implementation is not acceptance evidence.

The physical origin batch retains two SQL commands for 1/20/500 products;
twenty warm samples give p95 131.2/108.8/1684.6 ms. Value-only adjustments retain
two commands and p95 32.1/179.9/379.0 ms. Both meet the existing two-second gate
without another diagnostic SQL workload. The full purchase-return request
retains 46 commands for either one or twenty lines. These are local measurements,
not a production throughput guarantee.

The scoped audit checked all origin implementation/schema/call-site changes:
only `SqlInventoryLedgerWriter` writes the derived origin detail, in the same
document-engine transaction; physical quantity stays keyed by business,
warehouse and resolved stock product. References are tenant/business scoped,
return restoration is bounded by its original issue, and the fault test rolls
back origin facts with balances and movements. Reads/writes stay batched, with
no I/O inside the attribution loop, new UI loader, polling, job or engine.
Financial call-site review also confirms native/manual acceptance converges on
`accounting.PortfolioPaymentAccept` and application on the same posting
processor's `ApplyPortfolioPaymentsAsync`. This is a scoped correction audit;
the entire accounting/purchasing change still requires its final audit and the
public late-charge/financial-discount work remains unfinished.

## Valuation evidence prerequisite — 2026-10-22

The approved purchasing cost-attribution design requires identifying the oldest
remaining receipt origins while preserving average-cost valuation. The existing
`SqlInventoryLedgerWriter` now records its valuation mode, cost-pool scope,
quantity and book value before/after on each newly written `InventoryMovement`.
The scope distinguishes a business pool, the shared tenant pool and the excluded
damage warehouse. These are immutable movement facts from the already locked
state and persisted balances, not another editable inventory balance.

The snapshot uses the sum of the rounded warehouse values actually persisted,
so its value delta matches `ValueChange`. Aggregate fields retain the existing
quantity/money scales with wider precision than one warehouse's balance. The
same batch command persists the evidence with the balance and movement; no
extra I/O, new writer or valuation formula is introduced. The schema accepts
either complete evidence with matching deltas or all NULL for older writers.
Historical rows remain NULL, without a reconstruction or backfill. A rollback
retains these facts and can still accept older NULL evidence; automatic later
cost attribution must require complete compatible trace data.

This prerequisite does not implement receipt-origin allocations, their return
and transfer lineage, historical reconciliation or the subsequent value-only
cost revision. Their design owner remains `goods-receipts-design.md`.

Verification on 2026-10-22: the SQL project and Release integration build passed.
The 58-case inventory/receipt/return suite passed, as did all 847 Foundation
tests. The focused five-case ledger run additionally corrupts the new evidence
inside the existing ledger command, after balance writes, and proves SQL rolls
back the complete balance snapshot, kardex and sale lines. The already accepted
sale header remains unprocessed and the existing job becomes `RetryScheduled`;
its due retry applies once and replay is inert. The test initially expected the
accepted header to disappear; that incorrect expectation was corrected without
changing acceptance or the worker. The twenty-line case retains its existing
command-count gate, and shared-pool, negative-stock and damage cases verify the
recorded facts. The audit found one production writer, unchanged tenant/warehouse
locks, no extra command, and no new job, balance authority, catalog or UI loader.

Fresh isolated integration databases were deployed from the current DACPAC.
Schema comparison against the retained browser fixture could not finish because
SQL waited for memory (`RESOURCE_SEMAPHORE`); that attempt was canceled. Only in
the explicitly identified local fixture, the six additive columns and CHECK were
then applied transactionally from the canonical SQL definition. This prepares
browser testing; it is not evidence of a successful existing-database DACPAC
upgrade or authorization for a remote deployment.

## Inventory batches for receipt attribution — 2026-10-24, verified locally

Preserving receipt origins across dispatch/transit/receipt must extend the same
ledger transaction. This prerequisite batches the existing inventory stages;
it does not yet create origin allocations or change the valuation method.
The writer's existing batch accepts one business/document with its warehouses
and preserves the original movement order, including interleaved outbound and
inbound movements. Movement identity remains document/type/line/movement-type.
An inbound movement may use the recognized cost of an earlier movement in that
same in-memory batch; this is an internal dependency, not another cost formula,
API field, durable state or writer. The scalar entry point keeps delegating to
the same batch owner. No financial effect is added to a transfer without loss.

Acceptance and processing retain the existing tables, hashes, scopes, locks,
partial receipts, frozen dispatch cost and replay. Requested products resolve
in one set through `SqlProductLinkResolution`; line persistence and stage-result
updates must also use sets. The ledger keeps exactly one load and one persist
command for a transfer stage with 1–500 lines, including its two warehouses.
Compare one and twenty distinct products: dispatch/receipt SQL trips must stay
constant and document processing must remain below two seconds in the isolated
test environment. Verify original quantities/value, transit, partial receipt,
loss, rollback, replay and the order reservation/release consumers. No schema,
new job, UI workflow, FIFO balance or historical reconstruction is introduced.

The same processor also batches counts, signed adjustments and damage movements;
conversion and production retain two dependent phases, inputs then outputs, inside
the same document transaction. `InventoryOperationRules` still allocates output
cost and conversion loss from the valued inputs. Result lines persist once per
operation, including zero-change count lines. Confirmation updates count quantities
by set. The old per-line `ApplyAsync` and scalar result writer are removed;
there is no new processor or alternative calculation path. These additional
consumers share the verified inventory, production and order regression below.

The red regression recorded 28/161 SQL commands for dispatch and 25/158 for
partial receipt with one/twenty products. After batching, the final isolated run
recorded 26/26 for dispatch, 23/23 for receipt and 40/40 for the initial adjustment.
Each stage exercised exactly two ledger commands, independently of product count.
Document processing took 31.1/60.0 ms for dispatch and 30.6/49.5 ms for receipt,
under the two-second gate. These are local samples, not production percentiles.
The tests verify original, destination and transit quantities, average and value
by set, the partial status and replay without another physical movement.

A fault in the last result line after the ledger write makes the stage update
incomplete. Its SQL guard fails and the document transaction rolls back both
warehouses, kardex, dispatch results and outbox. The accepted header remains
`DispatchPending`; the existing worker processes that same transfer once at its
durable retry time. A second delivery is inert. No cursor, status or retry date
is edited in SQL to recover the test.

The initial transfer regression passed 72 SQL/API cases; the next 60 included
order recovery/invoicing and production. After removing the remaining scalar
loops, the combined fresh run passed 112 cases, including counts, adjustments
through negative stock, damage, transfers/loss, conversions, production, sales,
returns, receipts and order consumers. All 875 Foundation tests passed afterward.
Backend/integration and API Release builds passed with the four preexisting
NuGet advisories in the fiscal dependency. The modified browser test passed lint.

Three real browser/API/SQL journeys passed in the retained isolated local
fixture: receipt with discount/associated expense and purchase return; order
editing/recovery/batch invoicing with stock; and direct POS with receipt printing.
The order test now creates an inventory-managed product and twenty units via
the canonical adjustment API. It invoices four units across the edited orders
and verifies sixteen remain after command replays. Its screenshot was inspected.
It does not validate an external DIAN transmission or a physical printer.
Evidence in TEMP: `auraly-transfer-batch-red`, `auraly-transfer-batch-green`,
`auraly-transfer-batch-audit-green`, `auraly-inventory-batch-green`,
`auraly-inventory-batch-foundation`, `auraly-inventory-batch-release-build` and
`auraly-inventory-batch-real-browser`, with TRX/logs as applicable.

Post-implementation audit reviewed writer, processor, store, consumers and tests.
There is one ordered writer and one valuation calculator; no alternate balance,
writer, engine, job, queue, catalog or UI flow was added. Document scope and every
warehouse/product are validated; cost dependencies must point backward to the
same managed stock product. Tenant/shared-pool boundaries, AVE exclusion, locks,
rounding and immutable facts remain in that writer. Every changed loop does only
in-memory work; all line writes and stage-result updates are sets. Long source
descriptions/codes are not truncated by JSON projection. The acceptance hash and
HTTP and durable-document contracts stay unchanged, and completed effects remain idempotent. The
scope needs no schema change or backfill: the cost dependency exists only within
the processing call, so the same persisted payloads/tables support rollback.
The diff passed its whitespace check. This closes the batch prerequisite, not
receipt-origin FIFO, later cost revisions or the global financial-design audit.
No remote deployment was performed.

## Cost-pool context prerequisite — 2026-10-24

The same ledger writer now records which businesses participated in each cost
pool when a movement was valued. `inventory.CostPoolEpochs` contains immutable
membership and the initial quantity at that historical cut. `CostPoolHeads`
contains only the context reference and the existing processing sequence observed
for a business/product/damage slot. It does not allocate a sequence, schedule
work or hold another current quantity/value. `InventoryBalances` remains the
stock authority and `InventoryValuationCalculator` remains the valuation owner.

The load locks the exact active members of the existing valuation pool, including
members with no balance row. AVE uses a separate slot. A changed membership, or
a movement written by an older binary without updating the context reference,
starts a new context; it does not pretend the previous stock came from a receipt.
Rejoining a previously shared pool after separate activity cannot reuse its old
context. Completed replay never repairs context or rewrites a historical movement.
The HTTP/document payloads, hashes and quantity/cost formulas are unchanged.

All new reads remain in the batch load and all new writes in the batch persist,
within the existing document transaction. Requested products and current pool
members bound those sets. Conditional head writes reject an unexpected context
change. Product/scope foreign keys protect movement and head references; tenant
membership is resolved by the same scoped loader, not accepted from the caller.
Historical movement links remain NULL, permitting an older writer during rollback;
the next compatible writer detects its sequence gap without replaying that work.

The 74-case SQL/API inventory/receipt/return suite passed, including negative
stock, damage, transfers, conversions, production, membership changes and a
simulated older writer. Fault injection verifies that context references roll
back with balances, kardex and line effects. The one/twenty-line tests retained
two ledger commands per phase and their existing processing budgets. Evidence:
`auraly-cost-pool-context-green.trx` in TEMP. The SQL Release and integration builds
passed; the latter retains the four preexisting fiscal-package advisories.

The upgrade test removes only the new movement columns in an exclusively owned
disposable fixture, leaving a completed purchase in place, then reapplies the
canonical DACPAC. It verifies the same table object ID, original column IDs,
movement values/times, received stock, debt, journal and inert replay. New facts
remain NULL. It passed after scoping the balance snapshot to the receiving
warehouse: the first assertion also counted empty balances that the existing
postdeployment seed legitimately adds for other warehouses. No seed or stock rule
was changed to satisfy the assertion. Evidence: `auraly-cost-context-upgrade-green`.

The schema audit also moved this task's new columns after existing columns in
payments, credits, drafts, journals and purchase details. A comparison of the 55
existing tables in modified SQL files found no original column removed/reordered
and no new column interleaved with original columns. This avoids an unnecessary
column-order rebuild; it does not certify that every other schema change is online.
SQL Release compilation passed. No implicit positional INSERT/SELECT reader was
found for the affected tables. These declaration changes alter no rule or amount.

The scoped audit reviewed writer, context participant, SQL definitions, project
inclusion and tests. It found one transactional writer, no extra processing
engine, job, queue, editable balance, catalog, UI loader or I/O per movement.
The new metadata is not yet receipt-origin allocation, historical reconciliation
or a later cost revision; those remain pending under the purchasing design.
No remote deployment was performed.

The subsequent full Foundation run passed all 875 tests. The stronger schema
upgrade run passed both cases (`auraly-complete-schema-evidence-green`): its
comparisons now read the complete JSON scalar and parse it before comparing.
The audit reproduced SQL Server's chunking with a 5,000-character value: a direct
`FOR JSON` scalar read returned only 2,033 characters, while the scalar subquery
returned the full 5,015-character JSON document. This is a test-evidence fix,
consistent with [the documented SQL output contract](https://learn.microsoft.com/en-us/sql/relational-databases/json/format-query-results-as-json-with-for-json-sql-server).
No production JSON reader was changed. Rollback/replay snapshots and the
two/ten-freight-document comparison use the same complete-evidence approach.
Their focused repeat passed all five cases (`auraly-inventory-complete-evidence-green`).
The valuation-failure case deliberately starts without a derived context reference;
its failed attempt must create an epoch, then roll that epoch and its reference
back with the stock. The snapshot includes the complete context history. The
durable job's normal retry applies one movement; completed replay is inert.
API Release compilation passed. The retained disposable browser database was
updated successfully from the canonical DACPAC; its five preexisting direct
treasury movements compared identically before/after. All seven real browser/
API/SQL journeys then passed: direct cash, free journal/clone, customer collection,
automatic-account expense/payment, receipt/associated cost/purchase return/payment,
order editing/recovery/batch invoicing with stock, and POS with receipt printing.
Evidence: `auraly-cost-context-release-build`, `auraly-direct-real-local-schema`
and `auraly-cost-context-real-browser` in TEMP. This closes this prerequisite's
local verification and scoped audit, not the remaining purchasing/financial design.

## Value-only valuation in the existing ledger — 2026-10-25

`InventoryValuationCalculator` now accepts `ValueAdjustment` with a signed
monetary amount, zero quantity and no acquisition unit cost. It divides the
adjusted value by the positive quantity of the existing cost pool. Empty or
negative pools, an absent/zero monetary amount, negative resulting value,
non-stock products and the damage warehouse are rejected explicitly. The
owning cost revision must determine the portion eligible for remaining stock;
this operation does not infer receipt origins from the current balance.

`SqlInventoryLedgerWriter.PostBatchAsync` applies this mode using its same
load, locks, calculator, balance/kardex persistence and catalog outbox. It keeps
two SQL commands for a batch. There is no second writer, inventory balance,
processor, job table, API or UI workflow. Physical consumers retain their
existing modes and optional value amount remains absent. The calculator's
four-argument method is retained, forwarding to the same implementation, so
existing compiled consumers keep their signature.

The same writer validates business, document and document type against its
active document-engine session before I/O; its existing business load also
requires that session's tenant. Shared valuation can still update the pool's
authorized member businesses. A posting cannot select another document's or
business's target by supplying its IDs.

The existing unit-cost and warehouse money rounding remain authoritative.
The returned `ValueChange` is the aggregate change of the values actually
persisted, which can differ from the requested amount by rounding. For example,
two warehouses with 0.5 units valued at 1,000 each and a 0.0001 adjustment have
a new average of 1,000.0001; each warehouse rounds to 500.0001, so the book delta
is 0.0002. The subsequent accounting source must reconcile that actual delta
with its assigned cost; it must not blindly credit the requested amount.

The existing `InventoryMovements` CHECK accepts zero quantity only with complete
`ValueAdjustment` evidence. Non-zero movements from an older writer with NULL
evidence remain valid. No historical row is reconstructed or rewritten. A
rollback must retain the additive CHECK while value-only facts exist; it must
not delete those facts to restore an older constraint. The old-schema upgrade
fixture restores the original quantity CHECK while removing the new evidence,
then applies the canonical DACPAC and compares the complete original snapshot.

This is an internal valuation capability. The full late-charge capture,
receipt-origin attribution, historical reconciliation, financial discounts,
accounting reclassification and successive price preparation remain pending
under `goods-receipts-design.md`; this section does not announce them as usable.

Local verification: the API/infrastructure and canonical Release DACPAC compile.
All 15 writer-boundary SQL cases pass (`auraly-value-adjustment-ledger.trx`),
including whole-pool valuation, tenant/session guards, invalid physical inputs,
zero/negative eligibility, actual rounded delta and a failure after balance
updates followed by the owner's transaction rollback. In 20 isolated samples,
1/20/500 distinct products retained two ledger SQL commands, with p95
17.7/28.6/280.4 ms (maximum 27.9/30.0/345.7 ms), below the two-second budget.
These are local writer measurements, not a public late-charge endpoint benchmark.
The 17 new domain cases pass. The general Foundation run passed 965 of 966;
one existing POS case failed while deleting its temporary SQLite file. Its
focused repeat, together with the purchasing/valuation cases, passed all 87.
The general run is retained as failed; the repeat is not presented as a clean
full-suite run. Evidence: `auraly-value-adjustment-foundation-final.trx` and
`auraly-value-adjustment-foundation-focused.trx`.

The physical compatibility run approved 87/88 API/SQL cases for receipts,
referenced charges, sales, returns, transfers, conversion, production and linked
price publication. Its sole failure was the old-schema simulation omitting the
existing `IX_GoodsReceiptCostDocuments_SourceEntry` dependency before dropping
that column. The fixture now removes that index as well. Both upgrade cases
then passed in `auraly-value-adjustment-schema-upgrade.trx`, preserving original
table identity, documents, stock, costs, obligations and completed replay. No
runtime validation or assertion was relaxed to obtain those results.

The scoped post-implementation review covered the calculator, the complete
ledger writer and its scope/context, the project boundary, movement constraints,
both new test files, the upgrade fixture and these owner documents. Each rule
still has the same owner; there is no second inventory or financial flow,
business catalog, current balance, worker or queue. Tenant/session validation
uses zero additional SQL commands; the measured load/persist count remains two.
No frontend resource owner or network refresh policy changed in this slice.
The entire accounting/purchasing diff still requires its separate final audit;
this scoped review and the failed-then-repeated suites do not certify that whole
design as finished.

The retained local browser database was updated from the canonical DACPAC;
all 23 preexisting treasury movements compared identically. All nine real
browser/API/SQL journeys passed in `auraly-value-adjustment-real-ui.log`:
cash and bank treasury, journal save/post/clone, receivable collection, automatic
expense/payment/cancellation, both purchasing charge classifications and their
return/payment flows, order edit/recovery/batch invoicing, and POS sale/printing.
The expense detail was visually inspected after cancellation. These journeys
exercise the existing views and flows, not a public late-charge editor.
Only local test databases were updated. No DEV or production deployment ran.

## Intentional next work

- inventory reversals and controlled correction documents;
- reversal documents instead of deletion;
- accounting journals and period controls consuming the durable operational facts;
- web screens for counts, adjustments, transfers and conversions;
- evolution from the current connected `InventoryMovements` foundation to the
  header/line kardex model when reporting requirements require it.
