-- Eligibility for the existing payroll payment batch, shared by preview and acceptance.
-- PaymentMethodCode and bank destination come from the calculated employee snapshot;
-- subsequent edits to the employment never move an accepted accrual to another group.
CREATE FUNCTION payroll.PayableRunEmployees(
    @TenantId uniqueidentifier,@BusinessId uniqueidentifier,@RunId uniqueidentifier)
RETURNS TABLE
AS RETURN
(
    SELECT employee.PayrollRunEmployeeId,employee.PartyId,employee.NetPayable,
      method.OptionId PaymentMethodOptionId,method.Label PaymentMethodName,
      method.MetadataCode SettlementCategory,method.Code PaymentMethodCode,
      COALESCE(JSON_VALUE(employee.EmployeeSnapshotJson,'$.EmployeeName'),party.DisplayName,
        party.LegalName,party.Identification) EmployeeName,
      CAST(CASE WHEN method.Code<>N'Transfer' OR
        (NULLIF(LTRIM(RTRIM(JSON_VALUE(employee.EmployeeSnapshotJson,'$.Bank'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(employee.EmployeeSnapshotJson,'$.BankAccountType'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(employee.EmployeeSnapshotJson,'$.BankAccountNumber'))),N'') IS NOT NULL)
        THEN 1 ELSE 0 END AS bit) BankDestinationComplete
    FROM payroll.Runs run
    JOIN payroll.RunEmployees employee ON employee.PayrollRunId=run.PayrollRunId AND employee.TenantId=run.TenantId
    JOIN dbo.Parties party ON party.PartyId=employee.PartyId AND party.TenantId=run.TenantId
    JOIN payroll.CatalogOptions method ON method.CatalogCode=N'payroll-payment-method'
      AND method.Code IN(N'Cash',N'Transfer') AND method.IsActive=1
      AND method.DianCode=JSON_VALUE(employee.EmployeeSnapshotJson,'$.PaymentMethodCode')
      AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions other
        WHERE other.CatalogCode=method.CatalogCode AND other.Code IN(N'Cash',N'Transfer')
          AND other.DianCode=method.DianCode AND other.OptionId<>method.OptionId)
    WHERE run.PayrollRunId=@RunId AND run.TenantId=@TenantId AND run.BusinessId=@BusinessId
      AND run.Status=N'Approved' AND employee.NetPayable>0
      AND EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings settings WHERE settings.TenantId=run.TenantId AND settings.Status=N'Ready')
      AND EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs posting
        WHERE posting.TenantId=run.TenantId AND posting.BusinessId=run.BusinessId
          AND posting.SourceDocumentId=run.PayrollRunId
          AND posting.SourceDocumentType=CASE run.RunKind WHEN N'Adjustment' THEN N'PayrollAdjustment' ELSE N'PayrollAccrual' END
          AND posting.Status=N'Posted')
      AND NOT EXISTS(SELECT 1 FROM payroll.PaymentLines paid
        JOIN payroll.PaymentBatches batch ON batch.PaymentBatchId=paid.PaymentBatchId
        WHERE paid.PayrollRunEmployeeId=employee.PayrollRunEmployeeId AND batch.Status<>N'Voided')
);
