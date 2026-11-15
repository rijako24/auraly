-- Reviewed DEV configuration only. Other environments are not assumed to have
-- these identities. The final coverage check requires their own reviewed plan
-- if a Ready business still has incompatible required accounts.
-- The existing effective range is retained so NEW backdated purchases remain
-- supported. Posted journals and identities already pinned in jobs do not change.
DECLARE @PostingMappingUpgradePlanJson nvarchar(max)=N'[
  {
    "category":"InputVat",
    "sourceCode":"240810", "sourceName":"IVA descontable", "sourceType":"Asset",
    "sourceFunction":null, "sourceSide":null, "sourceBankAccountId":null,
    "targetType":"Liability", "targetFunction":"General", "targetSide":"Debit",
    "tenantId":"a0a10000-0000-0000-0000-000000000000",
    "mappingId":"37039cfd-b4c6-41d1-9589-8ff96e8451fa",
    "sourceAccountId":"689badd3-cc49-499b-908a-9fcb68379621",
    "effectiveFrom":"2026-01-01",
    "effectiveTo":null,
    "targetAccountId":"87b7c16a-8cea-44f5-9f1b-6cc97a937f8e",
    "targetCode":"240811",
    "targetName":"IVA descontable en compras"
  },
  {
    "category":"Cash",
    "tenantId":"a0a10000-0000-0000-0000-000000000000",
    "mappingId":"3715f936-db2c-4454-9300-dbf9cc81fc00",
    "sourceAccountId":"37263576-d5cc-48ae-9051-c3455b1543f8",
    "sourceCode":"110505", "sourceName":"Caja general", "sourceType":"Asset",
    "sourceFunction":"Bank", "sourceSide":"Debit",
    "sourceBankAccountId":"fa9bc2b6-bf4b-4b37-be7f-1d51e98725c3",
    "effectiveFrom":"2026-01-01", "effectiveTo":null,
    "targetAccountId":"ced19d74-1152-4dad-b15b-082cebeea45b",
    "targetCode":"110506", "targetName":"Caja operativa",
    "targetType":"Asset", "targetFunction":"Cash", "targetSide":"Debit"
  }
]';
IF N'$(DeploymentEnvironment)'<>N'dev' SET @PostingMappingUpgradePlanJson=N'[]';
:r .\20261115_ApplyReviewedPostingMappingUpgrade.sql
