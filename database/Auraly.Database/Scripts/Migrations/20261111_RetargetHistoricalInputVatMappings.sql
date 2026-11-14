-- Reviewed DEV configuration only. Other environments are not assumed to have
-- these identities. The final coverage check requires their own reviewed plan
-- if a Ready business still has incompatible required accounts.
-- The existing effective range is retained so NEW backdated purchases remain
-- supported. Posted journals and identities already pinned in jobs do not change.
DECLARE @InputVatUpgradePlanJson nvarchar(max)=N'[
  {
    "tenantId":"a0a10000-0000-0000-0000-000000000000",
    "mappingId":"37039cfd-b4c6-41d1-9589-8ff96e8451fa",
    "sourceAccountId":"689badd3-cc49-499b-908a-9fcb68379621",
    "effectiveFrom":"2026-01-01",
    "effectiveTo":null,
    "targetAccountId":"87b7c16a-8cea-44f5-9f1b-6cc97a937f8e",
    "targetCode":"240811",
    "targetName":"IVA descontable en compras"
  }
]';
:r .\20261111_ApplyInputVatMappingUpgrade.sql
