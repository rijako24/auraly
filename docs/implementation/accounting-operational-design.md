# Operational accounting vertical slice

**Implemented:** 2026-08-01
**Branch:** `feature/auraly-commerce-accounting-engine`

## Boundary

Accounting is a physical .NET module with contracts, domain, application and
infrastructure libraries. It uses the same SQL Server database and `dbo`, but has
its own connection factory and does not reference another module's infrastructure.

`TenantId` owns accounts, periods and vouchers because Tenant is the legal entity.
`BusinessId` identifies the establishment that originated the entry. Cost centers
belong to a Business and remain an optional analytical dimension.

## Durable flow

```text
sale, sales return or goods receipt handler
  -> operational effects and AccountingPostingJob in the same SQL transaction
  -> DocumentProcessingJob Completed
  -> completion observer before broker ACK
  -> separate serializable accounting transaction
  -> open period + effective account mappings
  -> balanced immutable AccountingEntry + lines
  -> AccountingPostingJob Posted
```

There is still one broker message per source document. There is no accounting
poller and no second broker message. If the process stops after the operational
commit and before the accounting commit, the unacknowledged broker message is
delivered again. `DocumentProcessingWorker` recognizes the completed operational
job and runs the idempotent completion observer without repeating inventory,
payments, work-session movements or fiscal work.

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

Sales return / credit note:

- debit sales returns;
- debit output VAT reversal when non-zero;
- credit the refund account or customer-credit liability;
- debit inventory and credit cost of goods sold only for value actually restored
  by the operational return handler.

Goods receipt / purchase:

- debit inventory only with the value recognized by the inventory movement for
  products that manage stock;
- debit purchases expense for non-inventoriable concepts;
- debit input VAT only when the immutable line explicitly uses
  `DeductibleInputVat`;
- capitalize purchase VAT into inventory or expense when the line explicitly
  uses `CapitalizedCost`;
- credit accounts payable for the immutable receipt total.

A zero-rate line must use `NotApplicable`. A positive-rate line must explicitly
select deductible or capitalized treatment; the server never infers this from the
tax rate. A receipt that neither creates a payable nor carries settlement
evidence becomes `AccountingPendingConfiguration/SettlementSourceMissing`;
Auraly does not invent a cash or bank credit.

The journal validator rejects zero-sided, double-sided or unbalanced entries.
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

## Native opening balances and activation

Opening balances are an accounting aggregate, not fields embedded in tenant
settings. `AccountingOpeningBalanceBatches` owns one dated batch per business;
`AccountingOpeningBalanceLines` owns its account, optional party, optional cost
center, description and one debit or credit side. Drafts are editable with
row-version concurrency. Approval requires at least two valid posting lines,
active tenant/business dimensions and equal positive debit and credit totals.

Activation has two catalog-backed modes:

- `ZeroDeclared` changes the tenant directly to `Ready` and creates no opening
  entry;
- `ImportedAndApproved` requires an approved batch for every active business on
  the effective date. Activation stores an immutable
  `AccountingOpeningBalance` source and one `AccountingPostingJob` per batch,
  then publishes them to the existing accounting queue. The tenant remains
  `Configuring` until the accounting processor has posted every opening entry.

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

Permissions are seeded idempotently for administrators:

- `accounting.read`;
- `accounting.configure`;
- `accounting.periods.manage`;
- `accounting.postings.retry`.

## Deliberate limits

This slice does not claim that Auraly is already a complete Colombian accounting
system. The governing scope remains
`decision-contabilidad-minima-colombia-y-cumplimiento.md`.

Still pending:

- minimum bank reconciliation before presenting Auraly as capable of a complete
  accounting close: bank-account master linked to postable PUC accounts,
  idempotent CSV/XLSX statement import, statement movements, manual matching
  against posted receipts/payments/vouchers, unmatched differences, accounting
  treatment for fees and interest, audited close and authorized reopen. The
  first delivery does not require direct bank APIs, automatic matching,
  multi-movement suggestions or AI classification;
- operational and fiscal debit note, then its posting rule;
- supplier payments, payable settlements and other inventory-operation postings;
- convergence of the current supplier master into Party before supplier and
  exogenous ledgers are considered complete;
- manual vouchers, reversals and authorized reopening;
- account/party/center ledgers beyond the trial balance;
- tax/withholding engine and regulatory reporting;
- reconciliations and statutory financial statements.

The current ledger can already debit and credit bank PUC accounts and report
their book balance. Until the minimum reconciliation above exists, the
accountant must compare that balance with the bank statement externally and
post supported adjustments through audited manual vouchers. This is enough for
initial operation, but it does not satisfy the acceptance gate for a fully
reconciled accounting close.
