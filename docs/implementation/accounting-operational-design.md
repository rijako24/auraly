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
sale or sales return handler
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

- operational and fiscal debit note, then its posting rule;
- purchases, payables, receipts, payments and inventory-operation postings;
- manual vouchers, reversals and authorized reopening;
- account/party/center ledgers beyond the trial balance;
- tax/withholding engine and regulatory reporting;
- reconciliations and statutory financial statements.
