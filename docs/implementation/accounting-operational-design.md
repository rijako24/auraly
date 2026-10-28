# Operational accounting vertical slice

**Implemented:** 2026-08-01
**Branch:** `feature/auraly-commerce-accounting-engine`

## Planned PUC and voucher extension (2026-10-20)

The target contract is owned by section 13 of
[`accounting-open-vouchers-design.md`](accounting-open-vouchers-design.md).
It extends account semantics, source-line links, parties, advances and applications
through the existing financial processor. The authority matrix there applies to
all modules, including cash, banks, inventory, tax and reporting. It supersedes
the older restriction that manual vouchers cannot affect subledgers, once those
typed effects and their atomic posting are implemented together.

Account balances remain aggregates of posted journal lines. Open-item balances
are maintained projections of their opening and applied transactions, linked to
the same source lines. Neither accounts nor party masters acquire independently
editable financial totals. Existing sources without journal entries retain their
frozen processing mode; activation reconciles existing open items to opening
batches without creating those obligations again.

The local implementation replaces the former bank-to-single-auxiliary link
with configured auxiliaries that refer to the bank master, as defined in
[`bank-accounts-and-reconciliation-design.md`](bank-accounts-and-reconciliation-design.md).
This does not complete the remaining voucher effects or native mapping snapshots.
The receipt-cost valuation decision is tracked in
[`goods-receipts-design.md`](goods-receipts-design.md), not silently resolved here.

The same design now keeps both assisted invoice capture and links to existing
vouchers in goods receipts. A new acquisition with a positive supplier net opens
its payable regardless of immediate or deferred terms; a separate disbursement
applies the actual payment. An advance application has no new cash effect. Account
defaults belong to the existing category/account mappings. Bank references and
manual financial effects are frozen on acceptance; the complete snapshot of
native mappings remains a gate in section 13.6 of the owner design. An
immediate-payment label never selects a bank or proves settlement.

The original verified defect was that `LoadGoodsReceiptFactsAsync` returned
`SettlementSourceMissing` for `CreatesPayable=false`, while the financial-effects
handler opens no payable. With accounting inactive, `AccountingEntryOnly` may
also result in no financial source/job. New purchase capture now always requests
its payable; the store rejects a new false request after checking historical
replay. Processing reuses the existing financial owner, preserving old payloads.
Historical omissions require a scoped reconciliation before any repair; replaying
the inventory receipt or guessing its bank is not an authorized repair.

Manual version-4 payable recognition also reuses that owner. Native single
openings adapt to `OpenPayablesAsync`; the batch inserts obligations and opening
transactions once, with no per-line I/O. The source-line identity disambiguates
multiple obligations for the same party and binds them in the existing journal
command. The account remains on that immutable line. Initial local SQL/API cases
verify rollback at binding, stable accepted IDs, explicit retry and subsequent
assisted payment using the original accounts. Receivable openings now share
`OpenReceivablesSql` with their existing native preparations, preserving the
sale/reconciliation attribution of closure corrections. Customer and party-only
manual openings passed the expanded 215-case SQL/API regression. The later
consolidation of closure notices into the common financial outbox and the shared
BuildingBlocks settlement policy require their updated checks before closure.
Advances and the remaining manual effects are not certified by those cases.

### Opening links for balances created by closure corrections (2026-10-22)

The financial processor now binds a new cash-sale receivable to the journal line
of the reconciliation that opened it. Its navigation still identifies the original
sale; its existing `Opening` transaction identifies the reconciliation. The
temporary `JournalLine.OpeningDocumentId` disambiguates the original sale while
posting; it is not another persisted financial reference or account balance.
Corrected tenders for one sale produce one net opening line, consistent with the
existing financial-effects writer. Ordinary source openings and employee claims
keep their existing binding. The binding validates party, original document and
amount in the existing journal-lines command and transaction, with indexed source
and position lookups rather than scanning all balances of the business.

The local regression reproduced a missing account link with the previous processor:
the accounting case failed while the historical commercial-only case passed.
The corrected code passed 46 integration cases: five closure-opening cases,
25 work-session API cases, 13 receivables cases and three payables cases.
All 845 Foundation tests also passed, including the opening-metadata invariant
and the architecture checks for the single financial writer and acceptance owner.
An additional 73 posting integration cases passed against the same code: 26
general accounting cases, 24 voucher-draft cases, 22 goods-receipt cases and the
expense-capture journey. This includes the unchanged ordinary opening links,
100 additional-cost documents, zero-valued receipts and immediate-due purchases.
The same API build passed the combined 16-case browser regression: six real
local journeys (manual/clone/auxiliary, receivable payment, expense/payment,
receipt, order editing/recovery/batch invoicing and direct POS sale/printing),
plus ten intercepted adjustment/payment-state cases. The latter cover accepted,
rejected and uncertain outcomes and never substitute for the six real journeys.
Current auxiliary and receivable screenshots were inspected after that run.
The two-sale case includes partially offsetting corrections on two tenders,
checks distinct opening lines, preserves the original sale IDs and verifies one
journal-lines command, no new journal-lines command on replay, and the original
two-second processing budget. Existing employee-claim cases also verify rollback
after an injected failure at binding. These checks do not certify the remaining
manual payment, historical cutover, bank or receipt-cost extensions.
No historical posted closure is repaired on replay. Existing schema and immutable
payloads remain compatible; rolling the code back would restore the original
missing-link defect for later closures, so the older worker is not a repair tool.

### Historical posted portfolio links at deployment (2026-10-27)

`20261027_LinkPostedPortfolioOpenings.sql` runs after the additive opening-link
columns exist and before the new API/worker release. It links only receivables and
payables whose original job was `Posted` with an accounting entry. The candidate
must be the one immutable line of that source with the same tenant, business,
party, opening amount, side, account nature and source hash; the account must
remain postable. Ambiguous or missing candidates abort the whole transaction.
The script is idempotent and writes only the two opening-line references. It
does not change the journal, amount, application, accepted payment or source
mode. Commercial-only positions still require explicit accounting reconciliation
and are never assigned today's mapping by this migration.

Production read-only preflight before this change found 676 posted receivables
and 946 posted payables, each with exactly one matching journal line. The same
snapshot contained 33 open commercial-only receivables: 32 in a business whose
accounting is still configuring, and one in an active accounting business. The
last position must be reviewed explicitly before claiming full portfolio cutover;
this migration cannot make a missing journal appear. DEV's two accepted,
unapplied collections target a separate migrated commercial-only receivable.
Their pending reservations and source date prevent the current opening-balance
validator from reconciling it. Replaying those jobs before reconciliation would
not apply the collections. A later DEV audit found 80 open migrated
commercial-only receivables in that business: four with six posted collection
movements and 76 without prior collections. The two pending collections target
one of the four. A posted collection covering two invoices may have one shared
control-account credit, so reconciliation proves the full payment credit rather
than requiring one journal line per application.

For that specific historical case, a version-3 reconciled opening can carry
`HistoricalCatchup=true`. The accountant selects the receivable control account
already proven by every posted collection and an explicit counterpart account.
Positions without posted collections require an active receivable auxiliary
configured with debtor, debit nature and posting enabled.
The same opening validator then requires one original commercial-only opening,
exact transaction arithmetic, a matching posted journal credit for each earlier
collection's full payment (including a payment split across invoices), the
original amount on the new opening line, no previous original journal and no
pending adjustment. It checks the original portfolio transaction
date rather than the import job's technical acceptance date. Accepted but
unapplied collections stay reserved; after the original line is linked, the
existing accounting retry processes them once. The ordinary opening path and
its stricter cut remain unchanged. No payment or balance is written by this
reconciliation outside `SqlAccountingPostingProcessor`.

The production cutover must quiesce old API and worker writers before publishing
the DACPAC, verify that every posted position is linked, and start only the new
workers/API afterward. Releasing the schema while an old writer continues to
create unlinked posted obligations would reopen the gap. Rollback of only the
binary to an old writer is likewise unsafe after the cutover. The three focused
SQL/API regressions cover posted receivable and payable settlement through the
existing processor, repeat migration, and transactional rejection of an invalid
historical account. The SQL project builds with no diagnostics. This evidence
does not certify commercial-only reconciliation or authorize a production
deployment.

### Portfolio payments and manual vouchers share their acceptance

Source/job persistence now converges in `dbo.AccountingSourcesAccept`, called in
the originating command's transaction. The existing `SqlAccountingPostingJobWriter`
has moved from shared persistence into Accounting Infrastructure; it adapts both
persisted operational payloads and pure financial snapshots to the same bounded
SQL acceptance. Manual vouchers, opening batches, payroll, service invoices,
restaurant tips and work-session closures also reuse this SQL owner. No balance,
cash or journal effect was moved out of `SqlAccountingPostingProcessor`.
The procedure freezes the accounting mode once, scopes every business to its
tenant, inserts source and job atomically, and leaves replay unchanged. It does
not repair missing or inconsistent jobs: source/job scope, hash, date and frozen
mode must agree on replay. The existing explicit legacy retry remains separate
from command acceptance. One batch of up to 100 inputs uses one SQL command;
opening activation groups its sources in batches rather than issuing one command
per opening. The local acceptance budget is one SQL command and at most two
seconds for 100 small sources, measured without concurrent builds or suites.
The same acceptance returns the number of matching durable jobs, including
replays and inputs skipped while accounting is inactive. Work-session
reconciliation consumes that result through the shared writer; it does not
rebuild the source envelope/hash or issue its own post-acceptance lookup.
Manual confirmation also uses its new-source count and authoritative single
job status. It has no separate hash/readiness/replay decision. Its strict-book
requirement keeps the configuration update lock before obligation locks; a new
source is tentative until manual validation and the draft marker commit in the
same transaction. A failed validation rolls that acceptance back completely.
Integration coverage must verify rollback, scope, mode preservation
and the affected document flows before this consolidation is considered verified.

The source's nullable `AccountingPostingJobId` is a derived existence link,
protected by a composite FK to the job's tenant, business, document ID and type.
Acceptance sets it only for newly accepted sources inside the same transaction;
replay still does not repair metadata or work. The existing explicit legacy
recovery sets it in its own transaction. The schema backfill links existing
matching jobs in keyset batches of 1,000 and never creates work, entries or
historical modes. The filtered null-link index limits missing-job classification
to exceptional candidates; legacy nulls still use the original scoped anti-join.
This link is not evidence of matching payload, date, mode or processing success.
Explicit legacy recovery reads the mode from that immutable source, never from
today's tenant activation. A proven `false` keeps commercial-only processing;
a proven `true` can post once. An unknown mode rejects recovery before creating
work or a link. Retrying the recovered job preserves its identity and cannot
create another entry for that source.
The local 100,000-source inbox gate passed with this schema: 20 samples per
scenario, one SQL command per page, 50 details at most, and the original 800 ms
p95 budget. First/next pages measured 161.6/300.9 ms; rare/frequent parties
(5/50,005 matches) measured 223.1/580.1 ms; a missing search measured 121.2 ms.
The existing-job link backfill took 8.9 seconds. Manual-party backfill/replay
took 53.5/30.0 seconds with the original setup/migration timeouts. Captured
execution plans after measurement reported no spills.
This controlled local run held two Codex UI renderers at 256 MB each to avoid
the SQL working-set pressure observed in failed preparations. Original UI
limits were restored and verified afterwards. SQL settings, the tested server,
volumes, samples, constraints and budgets were unchanged; no build, browser or
SQL diagnostic ran during page measurement. This result does not certify
production p95 or complete the remaining voucher/bank/purchasing redesign.
The filtered index includes its nullable predicate column, following
[Microsoft KB 3051225](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/performance/filtered-index-with-column-is-null);
otherwise SQL Server can ignore that index and read the wide source instead.

The derived manual-party backfill and its final coverage validation both traverse
keyset batches of 100 sources. Final validation does not parse the entire history
in one memory grant. Every insertion pass resolves the immutable references,
validates their tenant and checks for missing derived rows. When it inserts no
rows, that complete pass is also the coverage check; a replay does not repeat
the same JSON traversal. If any rows were inserted, the separate final validation
pass remains mandatory. Existing index rows never bypass source/party validation.
Legacy writers still must be stopped before the reader
cutover; this is an idempotent schema migration, not an online repair process.

The local SQL/API regression verifies repair of missing derived rows, unchanged
replay and an invalid legacy principal even when derived rows already exist.
That invalid input still stops the migration with its tenant-reference error;
the index is not evidence that allows skipping source validation. All 80 cases
in the current combined receipt/payment/acceptance/subscription run passed.
Migration timing and inbox latency are separate from that functional result;
their subsequent controlled local measurement is described above.

The existing assisted payment UI is `PortfolioPaymentWizard`, used by payables,
receivables, expenses and POS. Its services/stores already accept payment headers,
tenders, applications and the financial source/job atomically. The accounting
processor then applies balances, transactions and cash-session effects together
with the journal when required. Manual vouchers currently block the effective
AR/AP mappings; they do not yet provide this payment behavior.

Native supplier payments and customer collections now delegate their scoped
validation and acceptance writes to `accounting.PortfolioPaymentAccept`, within
the caller's existing serializable transaction. It validates the bounded set of
at most 100 positions through `accounting.ObligationAvailability`, the authorized
session and bank dimensions, and inserts the payment header, tenders and
applications together. It does not commit, create a job, apply a balance or write
a journal. `AccountingSourcesAccept` and the posting processor retain those
responsibilities. The former inline validation and inserts in both portfolio
stores are removed. Their existing replay, numbering and transport contracts
remain; a replay never calls this acceptance participant again.

The position lookup is a set operation constrained by its primary keys; it does
not traverse unrelated positions or pending documents. The participant uses one
SQL command irrespective of the number of allocations. The native acceptance
stores execute four commands (position/replay lock, the shared document-number
allocator, this participant and source/job acceptance), excluding transaction
boundaries and subsequent processing. The target for a warm batch of 100 is
below two seconds without concurrent diagnostic/build load. This consolidation
is a prerequisite for the manual groups, not an assertion that those groups,
advances or the original-account cutover have been enabled.

Native replay also scopes the position locks, payment, source and job to the
authenticated tenant/business. A payment whose source or job is missing reports
an explicit conflict requiring review; it does not repair or accept it again.
An ID and retry key that select two different payments are a conflict, with at
most two rows from the same replay command. No extra query is introduced. SQL
errors translated to domain failures retain their original exception, and an
already-aborted transaction is not rolled back a second time, preserving the
original failure and the existing bounded deadlock policy.

Native payment numbering now reuses `SqlOperationalDocumentAllocator`, with its
configured-series entry point. The duplicate series/cursor queries in Payables
and Receivables are removed. Payments still reject a missing or exhausted
series; this entry point never provisions a substitute. Other existing document
callers retain their provisioning behavior. The common lookup also requires
the authenticated tenant for payments before any cursor write. Its business
join uses the existing primary key; the consolidation removes one command and
adds no per-allocation I/O. Numbering and payment acceptance use the same
injected `TimeProvider` instant. A SQL trigger
in the disposable fixture exposed both previous paths attempting a cursor write
on a cross-tenant replay; transaction rollback had hidden that attempted access.
Both regressions failed against the previous binaries. The change retains the
existing document allocator and financial acceptance transaction; no separate
permission flow, numbering service or financial processor is introduced.
The first scoped-query fix passed 31 SQL/API cases including both complete
payment journeys and POS. The subsequent allocator consolidation has four
additional SQL regressions for missing/exhausted series in both directions,
checking that rejection creates no series or cursor consumption, plus a
canonical-owner architecture gate. Those four SQL regressions and all 836
Foundation tests passed. The 21.000-source availability gate also passed through
the native stores: four commands for 100 allocations (29,2 ms payable and
33,8 ms receivable in the warm measured samples), and one for a replay. These
checks preserve the existing numbering failures and acceptance owners; they do
not enable the pending manual-payment groups or replace the global regression.

Local verification of this prerequisite on 2026-10-21: the SQL project and API
Release builds passed, followed by 49 real SQL/API tests covering both payment
directions, expenses, sessions and POS. Fault injection at application insertion
left no payment, tender, application, source, job, transaction or journal, and
retrying the same identity succeeded once. Missing-source replay returned an
explicit conflict without recreating it; cross-tenant replay and a party-only
receivable offered as a customer collection were rejected. The prior binaries
failed those replay/party regressions. All 832 Foundation tests passed, including
the single acceptance-writer gate.

The 21,000-source performance gate passed on the consolidated allocator. It
retains 10,500 positions of each kind and measures availability for 1/50/500,
50-row first/next pages, party and empty-result filters, the common participant,
and complete native store acceptance for 1/100 allocations. Availability and
pages each used one SQL command; the slowest page sample was 464.9 ms, below
750 ms. The 100-allocation participant samples used one command and 19.9/14.5 ms
for payable/receivable. Complete native acceptance used four commands at both
sizes, with 100-allocation samples of 29.2/33.8 ms; all were below two seconds.
Each explicit replay used one command and returned the accepted payment without
another reservation. These are warm local samples, not a measured production p95.

The synthetic-history preparation took most of this run's 21 minutes under low
physical memory on the 6 GB machine. Diagnostic requests were confined to that
preparation; metadata still showed only 20,351 jobs when resident memory was
released from the verified Codex UI processes. No application was closed and
no SQL setting was changed. Warm-up and measured scenarios ran after preparing
all 21,000 sources, without concurrent SQL diagnostics, builds or frontend work.
Earlier attempts either found a savepoint error in the measurement harness or
timed out in preparation; they are not successful performance evidence. Neither
volume, timeout nor budget was relaxed. This gate does not certify manual groups,
the complete redesign, the full browser regression or a deployment.

The three remaining closure-payment correction timeouts passed in an isolated
repeat on the same Release binaries, with no source or timeout change: supplier
payment correction, customer collection correction and the non-cash correction
preview. This is focused compatibility evidence, not a replacement for the
complete regression or the outstanding representative performance gate.

`PaymentTenderBreakdown` owns the common tender invariants, including transfer
reference, card franchise/approval and the supported bank dimension. Payables and
Receivables normalize their transport contracts and translate domain errors;
they do not repeat those evidence rules. Their existing restriction of a bank
selection to transfers remains until the bank/auxiliary cutover is implemented
in the same contract for both surfaces.

`Payables.OutstandingAmount` is the only mutable outstanding balance, expressed
in `CurrencyCode`. `FunctionalOutstandingAmount` has no readers and duplicated
that balance without being updated by native payments. It is removed from the
opening writer, cancellation and closure correction. No
replacement balance, synchronization or repair process is introduced. The
original functional amount and exchange rate remain immutable acquisition
facts; any historical functional equivalent is a projection using the frozen
rate, not another editable balance. Payments still support COP only; this change
does not add foreign-currency settlement or exchange remeasurement.

The nullable legacy column stays in the declarative schema solely for rolling
release compatibility. New positions leave it NULL; no current API, processor,
backfill or report reads or writes it. It is not a second balance authority.
Retire it after all legacy API/workers and external writers have migrated and
their rollback window is closed. Until then, an old binary can still use the old
column without breaking new readers or requiring a synchronization process.

The offline DacFx comparison against the earlier DACPAC demonstrated that
`DropObjectsNotInSource=False` does not preserve a removed column on a retained
table: it generated `DROP COLUMN FunctionalOutstandingAmount` and a data-loss
guard for any populated Payables table. Keep `BlockOnPossibleDataLoss=True`;
never bypass that guard or enable global dropping. Removing the compatibility
column is a separate reviewed release through the existing pre-DACPAC migration
owner, before computing its deployment plan. Its migration must preserve the
authoritative outstanding, original functional amount and exchange rate, with
a tested rollback. No productive data is altered by this local implementation.

The planned extension reuses the same settlement validation, availability checks,
transaction participants and payment applicators for both entry surfaces, as
specified in section 13.5 of
[`accounting-open-vouchers-design.md`](accounting-open-vouchers-design.md).
`SupplierPaymentApplications` and `CustomerPaymentApplications` reserve monetary
payments regardless of capture surface. Frozen voucher lines reserve only the
distinct non-payment effects. There must be no second reservation of the same
payment and no manual payment journal followed by an automatic duplicate journal.

Manual payment groups reference the voucher's parent source/job; standalone
assisted payments retain their current source types. Queries, histories, session
closures, retries and reversals must follow that explicit relationship instead
of assuming payment ID equals source ID. Both paths must use each obligation's
original account, not today's AR/AP default mapping. Cross-surface race and
equivalence tests are release gates, including receivables as well as payables.

## Open vouchers (2026-09-30)

The approved workspace and its remaining export/attachment limitations are owned by
[`accounting-open-vouchers-design.md`](accounting-open-vouchers-design.md).
Financial Traceability now owns **Nuevo comprobante**, persisted unsent captures,
explicit **Contabilizar**, status and native retries. Portfolio adjustments start
from the selected obligation in CxC/CxP. Both reuse `AccountingService`, the native
source/job acceptance and `SqlAccountingPostingProcessor`; no second financial
engine, writer or queue is introduced. `accounting.VoucherDrafts` stores mutable
capture only, with optimistic versions and atomic freezing on acceptance.

Saving creates no financial/fiscal effects. Sending requires `accounting.manual.send`,
including retained legacy direct-confirmation endpoints. Native accepted commands
replay without republishing. Pending draft decreases participate in the existing
obligation availability checks. See the owner document for locking and test evidence.

The old Accounting manual section has been removed. Expenses retain their native
capture, tax preview, accounting and fiscal support flow described in
[`expenses-account-selection-and-withholdings-design.md`](expenses-account-selection-and-withholdings-design.md).
`ReportViewer` now supports downloadable PDF/XLSX; large filtered reports remain
paginated and their page exports are explicitly labelled.

## Boundary

Accounting is a physical .NET module with contracts, domain, application and
infrastructure libraries. It uses the same SQL Server database and `dbo`, but has
its own connection factory and does not reference another module's infrastructure.

`TenantId` owns accounts, periods and vouchers because Tenant is the legal entity.
`BusinessId` identifies the establishment that originated the entry. Cost centers
belong to a Business and classify entry lines. The current processor resolves an
active center automatically for every posting. The bounded correction design
(editable and replaceable default, existing rules, stable retries and returns)
is owned by section 8 of `../decision-contabilidad-minima-colombia-y-cumplimiento.md`;
that design is not a claim that its pending corrections are already implemented.

## Durable flow

```text
physical document handler -> inventory effects + canonical accounting source/job signal
financial document acceptance -> AccountingSourceDocuments + AccountingPostingJobs
  -> existing accounting queue / AccountingProcessingCoordinator
  -> separate serializable financial-accounting transaction
  -> financial subledger effects
  -> when AccountingEntryRequired: open period + effective mappings + balanced immutable entry
  -> AccountingPostingJob CommercialEffectsApplied or Posted
```

The topology is governed by
`../decision-cuatro-motores-operacion-contabilidad-fiscal-reporting.md`.
Accounting has its own canonical queue and durable job; financial-only documents
do not enter `DocumentProcessingJobs` or advance inventory cursors. Duplicate
activation signals reuse the same job and cannot repeat financial or physical
effects. The earlier completion-observer description is superseded by that
decision.

If a period or account mapping is missing, the work becomes
`AccountingPendingConfiguration`. This is an accepted derived state: it does not
erase an emitted invoice or reapply its operational effects, but it prevents that
period from closing. An authorized explicit retry uses the original immutable
source and recognized inventory cost.

## Implemented posting rules

Sales invoice:

- debit each payment account or accounts receivable;
- credit sales revenue;
- credit output VAT when non-zero;
- debit cost of goods sold and credit inventory using the recognized movement
value when the product manages stock.

Credit sales, customer collections and returns applied to receivables preserve
their commercial subledger effects when accounting is not active. The job
freezes `AccountingEntryRequired` at acceptance. A later accounting activation
does not post that historical activity retroactively; opening balances provide
the explicit cutover.

Sales return / credit note:

- debit sales returns;
- debit output VAT reversal when non-zero;
- credit the refund account or customer-credit liability;
- debit inventory and credit cost of goods sold only for value actually restored
  by the operational return handler.

Goods receipt / purchase:

- debit inventory for the acquisition amount of products that manage stock;
- when a receipt covers negative stock, post the difference between acquisition
  amount and the movement's book-value change against cost of goods sold and
  inventory in the same balanced receipt entry; the net inventory posting then
  equals `InventoryMovements.ValueChange`;
- debit purchases expense for non-inventoriable concepts;
- debit input VAT only when the immutable line explicitly uses
  `DeductibleInputVat`;
- capitalize purchase VAT into inventory or expense when the line explicitly
  uses `CapitalizedCost`;
- credit accounts payable for the immutable receipt total.

An ordinary product's positive initial cost seeds its zero-quantity balance.
Sales through zero and into negative stock retain the last weighted-average
unit cost, so each sale posts inventory and cost of goods sold. The tenant's
`InventoryCostBasis` selects a commercial pricing basis, not a journal method.

A zero-rate line must use `NotApplicable`. A positive-rate line must explicitly
select deductible or capitalized treatment; the server never infers this from the
tax rate. A receipt that neither creates a payable nor carries settlement
evidence becomes `AccountingPendingConfiguration/SettlementSourceMissing`;
Auraly does not invent a cash or bank credit.

The journal validator rejects zero-sided, double-sided or unbalanced entries.
`AccountingJournal` also owns the four-decimal rounding and persisted monetary
range for each line and entry total. Manual acceptance calls that same validator;
it does not sum raw values and round the aggregate using a different rule.
Invalid monetary entries fail before source/job acceptance. The original request
serialization remains unchanged for historical replay hashes, while the posting
writer persists the normalized lines returned by this validator. Validation
errors retain their domain exception and expose a clear input error to the user.
The journal header and line description columns accept 500 characters, matching
the existing manual capture/API contract. Widening these two columns preserves
historical data and needs no backfill; it does not change source serialization.
An application rollback retains the widened schema. Shrinking those columns is
not part of rollback once longer descriptions have been accepted.
Local acceptance evidence: the prior runtime failed three of four precision
cases (two invalid commands reached processing and returned 500; one valid
rounded entry was rejected). After sharing the domain validator, all five
SQL/API precision/range cases passed, and rejected commands left no source,
job or entry. All 835 Foundation tests passed, including the monetary range
boundaries and the canonical-owner architecture checks. The four 500-character
header/line cases reproduced SQL truncation through both direct confirmation
and saved-draft send, then all passed with the widened schema in the 195-case
SQL/API regression. That run passed 187 cases and failed eight: two reservation
tests lacked native payment numbering, and six fiscal/closure queries timed out.
An isolated diagnostic confirmed the two input errors named the missing
`PayablePayment` series. Their setup now invokes the existing
`BusinessDocumentSeriesDefaultsProvision` before exercising the balance conflict;
it neither relaxes the expected conflict nor duplicates availability validation.
Their corrected rerun passed both cases. The same focused repeat passed the
six prior fiscal/closure timeout cases without changing their runtime or timeout.
Two additional strengthened tenant-isolation cases failed on the previous
numbering lookup as described above, then passed in the 31-case focused
regression. The subsequent shared-allocator change still requires its rerun.
SQL Release build passed with no warnings or errors. The consistent auxiliary
read and its concurrent-posting regression also passed in this broad run.
An immutable goods receipt with a zero financial total and no journal lines
completes its existing accounting job as `CommercialEffectsApplied` without allocating a journal
number or inserting an empty entry. This covers free merchandise and a full
commercial discount. If inventory valuation produces a monetary adjustment,
the normal balanced entry is still required. The terminal job makes retries
idempotent; manual vouchers continue to require a valid balanced entry.
Posted entries have a tenant-wide `ASI-0000000001` voucher number, preserve the
source payload hash and cannot be updated through the API.

## Configuration and reports

Connected authenticated endpoints cover:

- postable accounts;
- hierarchical/default cost centers;
- non-overlapping accounting periods;
- tenant or business account-category mappings with effective dates;
- explicit retry of pending documents;
- entry lookup by source document;
- trial balance by date range and authenticated business;
- period close guarded by pending postings.

## Operational differences and tenant defaults

Inventory and cash differences reuse the ordered document engine and the single
`SqlAccountingPostingProcessor`; they do not introduce a second ledger writer.
An inventory payload freezes the reason's configured semantic category and
optional cost center. Cash differences use their stable shortage/overage
categories. In both cases the processor resolves the effective tenant/business
account mapping and open period when it posts:

- stock-count and manual-adjustment increases or decreases use the category on
  their `BusinessReasons` row against `Inventory`;
- damage and expired stock remove inventory value against the configured damage
  category;
- a product conversion allocates only the surviving input cost to its outputs
  and posts the recognized loss against the configured conversion-loss category;
- a transfer receipt may either remain partial or, with an authorized difference
  reason, close the outstanding quantity as a final loss from inventory in
  transit against the configured transfer-loss category;
- work-session shortages/overages use `CashShortageExpense` and
  `CashOverageIncome`; dispatch settlements use the independently configurable
  `DispatchCashShortageExpense` and `DispatchCashOverageIncome` categories.
  Their other side is always `Cash`.

`DispatchSettlementOperations` remains the durable owner of the multi-step
operational settlement (returns, receivable payments and final closure). Once
the cash difference is immutable it accepts `DispatchCashDifference` into the
canonical document stream, whose handler only creates the standard accounting
job. The dispatcher/coordinator is an activation signal; the durable job tables
remain the recovery and idempotency authority.

The default Colombian accounting profile provisions every tenant with 49
required accounts and effective mappings for these categories, an open period,
a default cost center and accounting-enabled operational reasons. It also maps
every active POS payment method (`Cash`, debit/credit card, transfer, customer
credit, bank transfer and deposit) to a category backed by a postable account.
Tenants may override effective account mappings and reason cost centers without
changing operational code.

Tenant provisioning also creates the assignable `ACCOUNTANT` role. Its
deterministic permission matrix covers accounting configuration and posting,
tax withholdings, payroll, expenses, payables, receivables, fiscal configuration
and the read/reconciliation permissions needed for cash, inventory, purchasing,
sales and dispatch evidence. It deliberately excludes user/role administration
and operational inventory confirmations. `ADMINISTRATOR` continues to receive
every non-platform tenant permission, while the cashier, supervisor and
administrative presets keep their existing operational boundaries.

## Native opening balances and activation

Opening balances are an accounting aggregate, not fields embedded in tenant
settings. `AccountingOpeningBalanceBatches` owns one dated batch per business;
`AccountingOpeningBalanceLines` owns its account, optional party, optional cost
center, description and one debit or credit side. Drafts are editable with
row-version concurrency. Approval requires at least two valid posting lines,
active tenant/business dimensions and equal positive debit and credit totals.

### Explicit reconciliation of commercial-only positions

The existing opening capture optionally identifies one existing payable or
receivable per line. Its stable `LineId` and nullable `ExistingPayableId` /
`ExistingReceivableId` extend `AccountingOpeningBalanceLines`; they do not
introduce another opening aggregate or editable portfolio balance. The accountant
selects the account and party explicitly. The account must declare the matching
financial function, nature and party requirement. The opening line contains the
current outstanding amount, preserving the original amount and prior payments.

`accounting.OpeningPositionsValidate` owns the reconciliation checks for capture,
approval and posting. It locks only selected positions and validates business,
tenant, COP, source mode explicitly commercial-only, completion before the cut,
absence of an earlier journal binding, pending payments or adjustments of either
direction, and agreement with transaction history. Movements at/after the cut or
already included in the book require explicit review. A draft does not reserve a
balance; approval and posting therefore revalidate under their transactions.

Linked openings use payload version 3, which older workers reject. Unlinked
openings retain their previous payload and replay hash. The existing processor
validates linked openings before journal insertion and binds the existing
position to its identified journal line in the existing batch insertion command.
It creates no obligation or portfolio transaction and changes no historical
payment. A failed check leaves the existing job pending configuration with code
`OpeningReconciliationRequired` and an actionable message; no journal or partial
link is committed. Correcting the existing data and explicitly processing the
same accepted opening uses the normal coordinator, source and job.

Capture/approval invoke the validator inside their existing SQL commands. Linked
posting adds one set-based validation command, independent of line count; the
binding reuses its locked proof in the same transaction. Capture retains its
three-command budget and two-second local gate (existing 2/50/500-line gates
remain). The view uses the existing paginated open-position selector after an
explicit account/party selection, does not fetch hidden rows, retains line IDs on
save/reopen, and disables approval while edits are unsaved. Its returned batch
updates the sole opening-query owner without another opening GET.

This additive implementation does not activate strict original-account readers
for native payments or prove complete cutover by itself. Those readers, the
zero-opening guard and full portfolio/inventory reconciliation remain separate
requirements of the approved cutover. Rollback must not allow an older worker to
consume version-3 jobs as ordinary unlinked openings; it leaves them rejected for
the compatible processor.

Local evidence for this additive opening slice: 74/74 integration cases passed
across accounting, drafts, native payables/receivables and obligation availability;
847/847 Foundation tests passed. Linked capture retained three SQL commands for
2/50/500 lines; the 500-line sample took 181.4 ms. The audit found that SQL NULL
semantics let an unclassified legacy account pass the function check; its new
two-case regression failed before the explicit NULL rejection and passed after.
The availability precision cases already passed without changing their arithmetic.
The real API/SQL browser journey then covered selection, four-decimal amounts,
save/reopen, approval, activation, one opening journal and the unchanged original
debt/payment. Together with the three focused browser scenarios it passed 4/4.
That journey exposed an empty HTTP 200 for an absent batch: the opening endpoint
now explicitly returns JSON `null`, matching its nullable client contract. Its
API regression and the complete opening activation/replay lifecycle passed.
Frontend typecheck, focused lint and production build passed. These local
checks do not certify the remaining financial cutover or a production rollout.
After the HTTP correction, the native opening lifecycle passed again, followed
by 30/30 closure/work-session integration cases. The browser compatibility
regression passed all 16 scenarios across its runs: manual/clone/auxiliary,
receivable payment, expense/payment, orders, receipt, POS and ten intercepted
payment/adjustment cases. The receipt run exposed an incorrect test locator:
supplier association is an inline section, not a dialog. The test now waits for
either the added line or that explicit association action and exercises both
paths without changing purchase behavior. Its focused lint and rerun passed.
Together with the four opening scenarios, this provides 20 passing browser
scenarios for this slice. The synthetic opening fixture's accounting settings
were restored, and the temporary test servers were stopped. The final user
demo remains deferred until the remaining design is implemented and audited.

### Activation lifecycle

Activation has two catalog-backed modes:

- `ZeroDeclared` changes the tenant directly to `Ready` and creates no opening
  entry;
- `ImportedAndApproved` requires an approved batch for every active business on
  the effective date. Activation stores an immutable
  `AccountingOpeningBalance` source and one `AccountingPostingJob` per batch,
  then publishes them to the existing accounting queue. The tenant remains
  `Configuring` until the accounting processor has posted every opening entry.

Provisioning the Colombian profile leaves `AccountingTenantSettings` in
`Configuring` without an effective date: accounts, mappings, period and cost
centers are ready, but document writers remain disabled until an authorized
user activates accounting explicitly. If the user initially selected
`ZeroDeclared`, the opening batch can still be replaced by
`ImportedAndApproved` only while no accounting source document or entry has
been accepted since that activation. The first movement makes that decision
immutable.

Repeating an already completed activation returns its persisted effective date,
currency, opening mode and activation timestamp. The store locks that tenant's
settings first; an identical `Ready` request does not revalidate the later
configuration or rewrite settings, cursors, sources or jobs. The existing
opening-balance editability query still determines the returned capability.
Completed imports do not republish opening jobs from the application service.
Changing the request does not turn activation into an edit endpoint: the existing
validated replacement of a zero opening by an approved import remains available
only before the first movement. Other accepted no-op requests return the stored
settings, rather than presenting their request values as saved.
The regression reproduced the incorrect timestamp before this correction.
SQL/API verification then passed both the completed zero-opening replay and the
approved-import lifecycle/replay, with two SQL commands per completed replay.
Before posting an imported opening, the acceptance result reports `Configuring`
with a null activation timestamp, matching the persisted settings. The same
approved-import lifecycle test covers that intermediate result and completes
through the existing processor. The separate report-volume and dimension
validation checks also passed after their corrections, as recorded in the open
vouchers design; this does not declare the full accounting regression complete.

Operational documents created before accounting activation do not close this
window, even if a technical posting job for one of them is created afterwards.
Only an accounting source accepted after activation or an entry posted after
activation makes the zero-opening decision immutable.

Withholding rules remain owned by the taxation module and are presented inside
the accounting workspace. Their required responsibilities and every
customer/supplier tax profile must reference active values from the canonical
`tax-responsibility` catalog. Party screens do not assign rules manually: they
show the rules that currently match direction, responsibilities and
jurisdiction; the withholding engine remains authoritative for vigency,
concept and minimum-base evaluation when a document is processed.

The accounting Retentions tab also presents administration of that canonical
responsibility catalog to users with `catalog.update`. Catalog values are not
created inside a withholding rule: the rule only selects existing values and
requires all selected responsibilities on the applicable supplier (purchase)
or customer (sale). An empty selection means that responsibility does not
restrict the rule. Creating a catalog value updates the same `reference.Options`
source consumed by party profiles and withholding validation; it does not create
a tenant-specific or parallel responsibility store.

Responsibilities are RUT classifications, never rates. A rule models one exact
fiscal scenario, so its selected responsibilities are conjunctive. Alternative
legal scenarios use separate rules with their own rate, base, concept and
effective dates. This avoids treating unrelated RUT qualities as interchangeable;
in particular, SIMPLE exceptions differ between income/ICA and VAT and cannot be
implemented as a generic OR across responsibility codes.

The processor uses the same open-period validation, journal validator, voucher
cursor, immutable source hash and `AccountingEntries`/`AccountingEntryLines`
writer as every other accounting document. The batch becomes `Posted` in that
same serializable transaction. Only the last successful batch changes the
tenant to `Ready`; therefore later operational documents cannot create
accounting jobs before the complete opening position exists.

The opening entry establishes general-ledger balances used by trial balance,
ledgers and statements. A party dimension is mandatory when its PUC account
requires one. Inventory quantities and individually payable/receivable source
documents remain owned by their operational subledgers; their import flows must
reconcile to the opening general-ledger lines and must not be simulated by this
batch.

Stable UI choices such as opening mode and account nature are rows in the
canonical `reference.Options` table. Accounts, parties, cost centers, periods,
opening batches and opening lines retain dedicated domain tables. Free text is
limited to business data such as names and descriptions.

Financial traceability filters by date range, accounting state and the exact
source document type. The `accounting-document-type` reference catalog supplies
the visible operation selector and its codes must remain equal to
`AccountingProcessingPolicy.DocumentTypes`; an integration regression prevents
the catalog and posting engine from diverging. The same filter is applied to the
paged screen query and every page included in its exported report.

Permissions are seeded idempotently for administrators:

- `accounting.read`;
- `accounting.configure`;
- `accounting.periods.manage`;
- `accounting.postings.retry`.

## Deliberate limits

This slice does not claim that Auraly is already a complete Colombian accounting
system. The governing scope remains
`decision-contabilidad-minima-colombia-y-cumplimiento.md`.

The bank-account master is now implemented as tenant-scoped accounting data.
Each active account links to one active, postable asset auxiliary in the PUC,
has one canonical account type from `reference.Options`, and at most one active
primary account exists per tenant. POS catalog/configuration synchronization
downloads that master only to enrolled devices. Transfer sales debit the
selected bank auxiliary and transfer refunds credit it through the existing
accounting processor; neither API nor UI writes journal lines directly.

Still pending:

- minimum bank reconciliation before presenting Auraly as capable of a complete
  accounting close. Its bounded functional design, Colombian bank-charge/GMF
  treatment and canonical accounting integration are defined in
  `bank-accounts-and-reconciliation-design.md`;
- operational and fiscal debit note, then its posting rule;
- inventory movements outside the explicitly connected operation types must be
  added through the same canonical document handler and accounting job path;
- convergence of the current supplier master into Party before supplier and
  exogenous ledgers are considered complete;
- the saved manual-voucher lifecycle described above, reversals and authorized
  reopening; multi-line manual acceptance itself is already implemented;
- full printable/exportable account/party/center auxiliary reports beyond the
  paged trial-balance drill-down described below;
- tax/withholding engine and regulatory reporting;
- reconciliations and statutory financial statements.

The current ledger can already debit and credit bank PUC accounts and report
their book balance. Until the minimum reconciliation above exists, the
accountant must compare that balance with the bank statement externally and
post supported adjustments through audited manual vouchers. This is enough for
initial operation, but it does not satisfy the acceptance gate for a fully
reconciled accounting close.

Approved next UI change: account adjustments will be created from Cuentas por cobrar / Cuentas por pagar in the context of their party and obligation. Financial traceability already shows their document and posting state. The relocation and saved-draft lifecycle are pending; the current creation entry remains in Accounting.

## PUC account maintenance (implemented locally)

`Plan de cuentas` is a list with a header, server search, server pagination and
`Nueva cuenta`. Clicking a row, pressing Enter on it or using `Editar` opens the
same account dialog. The permanent creation form beside the list is removed.
Read-only users can consult the list; mutations require `accounting.configure`.
The nature selector uses `reference.Options/accounting-account-type`.

`AccountingService` and `SqlAccountingStore` retain sole ownership of accounts.
`PUT /accounting/accounts/{accountId}` edits the name and `RequiresParty` on the
tenant's existing `AccountingAccounts` row. Code, nature, posting capability and
identity remain stable, so existing journal lines and references retain their
classification. The requirement for a party applies when subsequent entries are
validated; this operation does not rewrite historical entries or pending sources.
An active bank's PUC account cannot start requiring a party. This validation and
the write share a serializable transaction and the account lock used by bank
configuration.

The existing SQL row version is exposed additively in account responses and
required for editing. Stale divergent edits return 409. An identical replay
returns the current authoritative record without another update. Tenant scope
comes from authenticated context. No schema migration, new ledger writer, job,
queue, fiscal transmission or background refresh is introduced.

`GET /accounting/accounts?page=...&pageSize=...&search=...` returns a bounded page,
including structural and inactive accounts. The legacy request without query
parameters remains compatible. The list and account dropdowns reuse the same
SQL query owner; dropdowns retain active/postable and expense-only restrictions.
Page and count use one SQL command. An edit also uses one SQL command and has a
local acceptance budget of two seconds, including commit.

The PUC list owns its load; the legacy full-plan query is disabled while it is
visible. Editing without a search updates the row from the mutation response,
with no GET. Creating or renaming under an active search performs one bounded
page read because page membership and total count can change. Other cached
pages/selectors are marked stale without fetching. Reverting this UI/API change
requires no data rollback; saved names and party requirements remain valid.

Regression coverage: `AccountingAccountEditingTests` exercises HTTP permissions,
tenant isolation, stale writes, replay, atomic bank validation, persisted reload,
pagination and SQL command counts. `account-editing.spec.ts` covers row/keyboard
opening, creation, saving/reopening, conflict, query failure/retry, search and
read-only access. The shared retentions workspace lives outside its route module
so both embedded and standalone rendering satisfy Next.js page export rules.

Local verification on 2026-09-30: backend Release build and frontend production
build passed; focused ESLint passed. Both SQL editing regressions passed after
the replay check, including the one-command/two-second budget. All eight browser
cases passed (three PUC journeys, account search and four return reprint paths).
Visual inspection verified the list header, row action, keyboard opening and
the visible page size. No deployment or physical-printer certification was done.

## Account auxiliary query and trial-balance detail (local work)

`AccountingService` / `SqlAccountingStore.GetAccountMovementsPageAsync` own the
account auxiliary query. It reads posted `AccountingEntries` and
`AccountingEntryLines`, scoped by authenticated tenant/business, dates, account
range, line party and cost center. Account/party masters hold no duplicated
balance. One SQL command first captures the filtered journal keys, dates and
amounts in a query-local temporary set. It keeps the reader's existing isolation
and does not introduce serializable range locks or change database settings.
Opening/period aggregates and page keys derive
from that same captured set. Only the bounded page is enriched with details.
This set is neither a persisted projection nor a second balance authority.
The opening and running
balances use the same filters; ordering includes entry and line identity so a
second page continues the first one's balance. The line party snapshot, original
source number and reference are shown instead of substituting the main party or
a shortened GUID. A missing historical taxable base remains unknown, never zero.

The trial-balance dialog consumes this paged contract (50 rows), preserves totals
for the complete filtered period, and resets to page one when party or center
changes. Its parent supplies the existing center catalog; the dialog does not
load it again. It loads detail only while open, and refresh/retry are explicit
actions. Errors are visible and do not become an empty successful report.

The pre-existing `accountCode` request without pagination retains its array
response, including more than 200 movements. It adapts to this same query owner;
it does not keep an alternate journal SQL implementation. The admin no longer
uses this legacy form. It remains unbounded for existing client compatibility;
removing that contract requires an explicit client migration, not truncation or
a new failure for an otherwise valid historical account.

The audit reproduced a concurrency defect in the prior two-read implementation:
a voucher posted between the aggregates and the page produced a total count of
two but four visible rows. The deterministic SQL/API regression pauses that
boundary with a test-only application lock, posts through the public voucher
acceptance and canonical motor, then releases the read. The fix captures journal
facts once; the regression passed against the corrected runtime, and a later
explicit refresh includes the newly posted voucher. There is no
production synchronization hook or alternative journal writer.

The API/SQL functional test verifies openings, different line/header parties,
scope, period totals, empty pages and balance continuity. Its initial warm
functional sample used one SQL command and 157.3 ms. Its expanded SQL/API rerun
passed, including one 202-line voucher, all 204 rows through the legacy array,
50-row pages and the maximum supported date; the warm measured page used one
SQL command and 218.4 ms. The consistent-read rerun passed both functional tests
and measured its warm sample at 84.9 ms with one command. The separate
100,000-unrelated-line gate then passed with 103 local period movements:
the first and second 50-row pages used one command each, at 124.0 and 123.3 ms,
below 750 ms. Its trial balance used one command and 246.9 ms. The later
195-case regression repeated the final read without additional range locks:
100,000 unrelated lines, one command per page, 115.2/116.8 ms for pages one/two,
and 251.2 ms for the trial balance. A prior rerun
timed out in history setup and is not counted as a report measurement. The
synthetic business now includes its default cost center so it cannot invalidate
later activation tests in the shared disposable fixture; the data volume and
timeouts were not reduced or widened. The browser test with 201 movements passed with
HTTP simulated: page/filter/reset behavior, party/source/reference display,
four-decimal precision, full-period totals, one center-catalog read, one detail
request per explicit page/filter action, visible failure and retry, cached reopen
and a viewport-contained mobile dialog. The final frontend build and focused
lint passed; the complete lint had zero errors and nine warnings. These checks
do not replace a browser regression against the real backend. The account-range
UI and shared hierarchy drill-down are extended in section 13 of the open-vouchers
design, with their own verification. Printing/PDF/Excel and the remaining full-design evidence
remain pending. This detail view is not presented as the complete report in the
user's photograph or as completion of the accounting redesign.

### Recorded journal-line bases

Manual capture accepts an optional signed `BaseAmount`, distinct from debit and
credit. Empty remains unknown; explicit zero remains zero. The draft, immutable
source and journal line preserve that evidence through the existing acceptance
and posting owners. `AccountingJournal` applies the same four-decimal storage
precision and persisted range; it does not infer a tax, base or sign from the
financial sides. Existing/native lines without captured evidence remain null.
The existing account/entry index covers this field for the auxiliary aggregates.

Both clone paths preserve the base: copying an existing capture and copying a
posted entry that has no draft. Replay hashes omit new null properties so old
payloads retain their representation; changing a recorded base on an accepted
identity conflicts instead of modifying its journal. The auxiliary captures bases
with its other filtered facts. A period/account total is known only when every
period line has a recorded base; otherwise the total stays null, while individual
known bases remain visible. No base total is persisted independently.

The capture UI reuses the signed decimal input. Clearing it sends null, and
typing zero sends zero. It adds no query, refresh or financial effect. Existing
voucher download formats are not extended by this data-capture slice; a later
format change requires its canonical versioned template. The existing auxiliary
base column now reads stored evidence. Deployment must update schema and all
API/posting workers before exposing the input; after accepting non-null bases,
rollback keeps the columns and a worker that preserves them. UI rollback alone
does not erase those records or justify replaying their effects.

Local verification on 2026-10-22: SQL/API regression 251/251, Foundation 844/844,
frontend unit checks 250/250, typecheck, focused lint and production build passed.
The SQL scenarios include signed/zero/unknown bases, both clone paths, immutable
replay and native financial/purchasing/order flows. The auxiliary's 100,000-line
unrelated-history gate retained one command per page (102.1/93.5 ms).
Two intercepted-HTTP browser journeys passed on the production frontend: the
auxiliary with more than 200 movements and draft capture/edit/send/clone with
base preservation. The browser fixture was corrected to use the shared input's
decimal-point contract; the product input and other number fields were unchanged.
These browser tests are separate from the SQL/API run and do not certify a real
browser-to-backend journey or the remaining redesign.

The post-change review traced the optional field through contracts, draft storage,
source hashing, journal normalization, the existing batch writer, entry reads,
cloning and auxiliary aggregation. It found no second writer, stored total,
per-line I/O, polling or additional frontend load owner in this slice. Deployment
and the full-design/file-by-file audit remain pending.

### Opening-capture batching prerequisite for cutover

The existing opening-balance store now validates and inserts its lines as one
typed set inside the same serializable save transaction. Save and approval share
the scoped dimension validation; they retain the active party/role, required
party, posting-account and cost-center checks. An account restricted to another
business is invalid in both paths. A validation failure rolls back the whole
command, preserving the previous draft and its version. Header/version handling,
approval and financial activation keep their existing owners and contracts.
The authoritative saved capture is read with one scoped query, including its
ordered lines. No financial source or job is created by saving or approving.

The regression first exposed 9/129/1,254 commands for 2/50/500 lines. The updated
local focused run passed six cases, including the existing approved-opening
posting journey. Save now uses three commands at all three sizes, measured at
7.4/96.1/38.4 ms with the original two-second budget. Approval uses five. Coverage
checks complete replacement/order/totals, rejection without loss of the saved
capture, stale versions, business/tenant scope, revalidation after a draft's
account moves to another business and the previous decimal-parameter precision.
This is a prerequisite for the explicit legacy reconciliation in section 13.8;
it does not yet link legacy portfolio positions or enable manual payment groups.
The subsequent accounting vertical-slice run passed 26/26, including opening
activation, invoices/returns, bank reconciliation and the new capture cases.
The architecture-filter run passed 74/74. The backend Release build passed with
the four existing `System.Security.Cryptography.Xml` vulnerability warnings.
The post-change slice review traced application permissions, save/approval,
versioned replacement, the scoped reader and activation/source consumers. The
two per-line validation helpers became unused and were removed. Dimension rules
now have one owner shared by save/approval; no source envelope, posting, numbering,
external operation, schema or frontend flow changed in this prerequisite.
Its rollback requires only the previous binary and preserves the same captures;
it would also restore the prior excessive round trips and missing account-site
guard. No data rollback is required. Global redesign verification and the complete
file-by-file audit remain pending.

### Real-browser capture and journal precision

`admin/e2e/accounting-real-local.spec.ts` exercises the production frontend against
the ordinary API, authentication and an isolated SQL database. It does not
intercept HTTP. The existing `Testing` processing transport invokes the same
document/accounting processors; external DIAN submission, email and Azure push
are outside this local evidence. The fixture uses the public configuration API
and two active parties, then creates, saves, reloads, posts and clones a free
journal through the view. It verifies principal/line parties, negative/zero bases,
four-decimal amounts, an unchanged entry ID on replay, fresh clone/line IDs and
no ledger entry for the clone. This does not certify the pending manual financial
groups or the four-type editor.

The same journey opens the persisted journal, downloads PDF/XLSX, parses the
spreadsheet's real account/party/amount cells and checks the auxiliary row and
party filter. Opening this purely financial journal makes no fiscal-document GET.
Its browser-bound fixture requests use the browser's existing authenticated
session. Reload waits for the saved document URL to commit; a network response
alone is not evidence that navigation has finished.

Visual review exposed a display defect: the posted journal showed `1.200` for
the stored `1200.1234`. The added browser assertion failed against that build.
The journal's on-screen amounts/totals/difference now explicitly display up to
four decimals. Existing voucher download formatters/templates remain unchanged.
The auxiliary uses spaced columns, a wider desktop dialog and a compact table;
small screens keep its horizontal scroll. The real desktop test checks that the
balance column remains within the dialog, and the existing mobile/pagination
journey retains its bounds and single resource loader.

Local checks: frontend production build, typecheck and focused lint passed.
The combined browser run passed 5/5: three real API/authentication/commercial
view journeys and two intercepted-HTTP draft/auxiliary regressions. After the
last auxiliary layout adjustment, its two affected journeys passed again (1.1
minutes total). Screenshots in `work/comprobante-real-contabilizado.png` and
`work/libro-auxiliar-real.png` were visually reviewed. The slice audit found no
new loading owner, refetch, polling, financial calculation, source writer,
endpoint, schema or effect. The frontend `AccountingEntry` type now declares
the already-existing optional principal ID. UI rollback preserves every stored
record. Full expense/payment/purchase/order browser regression and the remaining
redesign are still pending; this is not a deployment or final local demo.


### Manual voucher header reference — compatible source capture

Both manual confirmation contracts (free journal and portfolio adjustment) now
carry their optional header `Reference`. The shared draft sender freezes the
saved reference in `AccountingSourceDocuments`;
account movements already read that source field. There is no new reference
column, report writer, balance or posting path. Null references remain omitted
from serialization, preserving older payload hashes and the current line-identity
version.

A sent draft is replayed with the reference recorded in its accepted source.
`ReadVoucherDraftAsync` projects that metadata in its existing scoped query;
`AcceptedReference` is excluded from JSON and is neither editable nor separately
persisted. This matters for older sent drafts: their saved header may contain a
reference that the old confirmation contract never froze. Replaying them must
retain the original absent field and hash. The captured header remains visible;
the auxiliary does not invent a historical source reference from it. Direct
confirmation still compares the complete request hash, including a non-null
reference, and rejects changes under an accepted identity.

The existing reference length rule is reused for direct confirmation. The read
continues to use one SQL command; an unchanged sent-draft replay retains its
three-command budget. All lookups use document type, tenant and business as well
as identity. There is no backfill or schema change. Deploy a compatible sender
before accepting references; rollback must retain that sender for new accepted
sources. A previous sender would omit their reference and conflict on replay;
frontend rollback remains compatible.

Evidence: the new two-case regression failed with the old sender and passed
with the correction, including immutable legacy payload/hash, one source/job/
entry, unchanged row version and the three-command sent replay. The complete
manual-draft class passed 24/24; canonical source acceptance passed 23/23.
The adjustment regression also preserves an old source with no reference while
retaining the new reference on replay, the same entry and the three-command
budget. Its old-sender reproduction failed only for the new referenced source;
the historical case passed. The combined SQL regression passed 90/90 in 2.1
minutes: drafts/source acceptance, native payables/receivables, order recovery
and batch invoicing. The fixture builds its historical adjustment from the saved
decimal value, preserving its serialized scale as the original sender did.
The real API/SQL browser journey passed in 50.2 seconds and asserts the saved
reference in the auxiliary, alongside posting, exports and cloning. The audit
confirmed that the existing scoped source join cannot multiply draft headers
and adds no SQL round trip or financial effect. These checks cover this capture
fix; they do not certify the pending manual financial groups or full redesign.

### Financial amount display — local browser regression

The real receivables journey reproduced a presentation defect: the stored
remaining amount was `900.25`, but the payment wizard displayed `900`. Financial
lists, summaries, details and the shared payment wizard now explicitly request
up to four decimals from the existing currency formatter. The shared checkout
also displays up to four decimals without forcing trailing zeros. Existing
formatter callers keep their default precision; no printed template or monetary
calculation changed. This introduces no new query, refresh owner, reservation,
balance, writer or payment path.

The native browser regression imports an isolated opening through Receivables,
collects `100.25` against `1000.5001`, and verifies `900.2501` in the API, customer
table, invoice table, detail and reopened wizard. It also checks one payment,
one application, a balanced journal with the customer's party and the same
result on replay. The rendered wizard was visually reviewed. The current
frontend production build, typecheck and focused lint passed, as did the 16
prechecks and 250 POS unit tests. The final combined browser run passed 16/16 in
4.2 minutes: six real API/authentication/SQL journeys, eight intercepted checkout
regressions and two adjustment-modal regressions. These include purchase receipt,
expense payment, order recovery/save/batch emission and direct sale with receipt
rendering. External DIAN and physical
printing are outside this local evidence. The temporary test servers are not the
final user demo, and this does not certify the remaining redesign.
