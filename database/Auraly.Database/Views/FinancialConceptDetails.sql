CREATE VIEW reference.FinancialConceptDetails AS
SELECT concept.FinancialConceptId,concept.TenantId,concept.Code,concept.Name,concept.AccountId,
  account.Code AccountCode,account.Name AccountName,account.FinancialFunction,
  concept.SideOptionId,side.Code SideCode,side.Label SideLabel,
  concept.UseForReceipts,concept.UseForSupplierPayments,concept.UseForVouchers,concept.IsActive,concept.SortOrder,concept.RowVersion,
  CONVERT(bit,CASE WHEN ready.AccountId IS NOT NULL THEN 1 ELSE 0 END) IsReady
FROM reference.FinancialConcepts concept
JOIN dbo.AccountingAccounts account ON account.AccountId=concept.AccountId AND account.TenantId=concept.TenantId
JOIN reference.Options side ON side.OptionId=concept.SideOptionId
OUTER APPLY accounting.FinancialConceptAccountReadiness(concept.TenantId,concept.AccountId,concept.SideOptionId,1) ready;
GO
