CREATE FUNCTION [payroll].[WorkerSubtypeCalculationOptions]()
RETURNS TABLE
AS RETURN
(
    SELECT subtype.OptionId,subtype.IsActive,
        CONVERT(bit,CASE WHEN subtype.MetadataCode=N'PensionExempt' THEN 0 ELSE 1 END) AS PaysPensionContributions,
        HASHBYTES('SHA2_256',CONVERT(varbinary(max),(
            SELECT subtype.Code,subtype.MetadataCode,subtype.DianCode,subtype.IsActive
            FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER))) AS SourceVersion
    FROM payroll.CatalogOptions subtype WHERE subtype.CatalogCode=N'payroll-worker-subtype'
);
GO
CREATE FUNCTION [payroll].[ApplicableCalculationConcepts]
(
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER
)
RETURNS TABLE
AS RETURN
(
    SELECT c.ConceptId,c.Code,c.Name,n.Code AS NatureCode,m.Code AS CalculationMethodCode,
        a.Code AS AccountingCategoryCode,d.Code AS DianConceptCode,s.Code AS SystemRoleCode,
        c.IsSalaryBase,c.IsSocialSecurityBase,c.IsBenefitsBase,c.IsTaxWithholdingBase,
        c.RequiresDeductionAgreement,c.RowVersion,c.SystemRoleOptionId
    FROM payroll.Runs r
    JOIN payroll.Concepts c ON c.TenantId=r.TenantId AND c.IsActive=1
        AND c.EffectiveFrom<=r.PeriodEnd AND (c.EffectiveTo IS NULL OR c.EffectiveTo>=r.PeriodStart)
    JOIN payroll.CatalogOptions n ON n.OptionId=c.NatureOptionId
    JOIN payroll.CatalogOptions m ON m.OptionId=c.CalculationMethodOptionId
    JOIN payroll.CatalogOptions a ON a.OptionId=c.AccountingCategoryOptionId
    LEFT JOIN payroll.CatalogOptions d ON d.OptionId=c.DianConceptOptionId
    LEFT JOIN payroll.CatalogOptions s ON s.OptionId=c.SystemRoleOptionId
    WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.PayrollRunId=@RunId
);
GO
CREATE FUNCTION [payroll].[CalculationContextVersions]
(
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER
)
RETURNS TABLE
AS RETURN
(
    SELECT CONVERT(tinyint,0) AS SourceKind,r.TenantId AS SourceId,
        CONVERT(varbinary(32),COALESCE(CONVERT(binary(8),s.RowVersion),CONVERT(binary(8),0x0))) AS SourceVersion
    FROM payroll.Runs r LEFT JOIN payroll.Settings s ON s.TenantId=r.TenantId
    WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.PayrollRunId=@RunId
    UNION ALL
    SELECT CONVERT(tinyint,1),c.ConceptId,CONVERT(binary(8),c.RowVersion)
    FROM payroll.ApplicableCalculationConcepts(@TenantId,@BusinessId,@RunId) c
    WHERE c.SystemRoleOptionId IS NOT NULL
    UNION ALL
    SELECT CONVERT(tinyint,2),subtype.OptionId,CONVERT(varbinary(32),subtype.SourceVersion)
    FROM payroll.Runs r CROSS JOIN payroll.WorkerSubtypeCalculationOptions() subtype
    WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.PayrollRunId=@RunId
);
GO
CREATE FUNCTION [payroll].[CalculationNoveltyVersions]
(
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @EmployeeIds NVARCHAR(MAX)
)
RETURNS TABLE
AS RETURN
(
    SELECT n.NoveltyId,n.EmploymentId,n.ConceptId,CONVERT(binary(8),n.RowVersion) AS NoveltyVersion,
        COALESCE(CONVERT(binary(8),a.RowVersion),CONVERT(binary(8),0x0)) AS AgreementVersion,
        COALESCE(CONVERT(binary(8),c.RowVersion),CONVERT(binary(8),0x0)) AS ConceptVersion
    FROM payroll.Runs r
    JOIN OPENJSON(@EmployeeIds) WITH(EmploymentId uniqueidentifier) selected ON 1=1
    JOIN payroll.Employments e ON e.EmploymentId=selected.EmploymentId
        AND e.TenantId=r.TenantId AND e.BusinessId=r.BusinessId
    JOIN payroll.Novelties n ON n.EmploymentId=e.EmploymentId AND n.TenantId=r.TenantId
        AND n.BusinessId=r.BusinessId AND n.Status=N'Approved'
        AND n.StartDate<=r.PeriodEnd AND n.EndDate>=r.PeriodStart
    LEFT JOIN payroll.DeductionAgreements a ON a.DeductionAgreementId=n.DeductionAgreementId
        AND a.TenantId=n.TenantId AND a.EmploymentId=n.EmploymentId
    LEFT JOIN payroll.Concepts c ON c.ConceptId=n.ConceptId AND c.TenantId=n.TenantId
    WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.PayrollRunId=@RunId
);
GO
