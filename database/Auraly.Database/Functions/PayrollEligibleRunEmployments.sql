CREATE FUNCTION [payroll].[EligibleRunEmployments]
(
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER
)
RETURNS TABLE
AS RETURN
(
    SELECT e.EmploymentId,e.PartyId,e.ContractNumber,e.MonthlySalary,e.StartDate,e.EndDate,
        e.SalaryTypeOptionId,e.RiskClassOptionId,e.WorkerSubtypeOptionId,e.AppliesTransportAllowance,e.RowVersion,
        r.PayFrequencyOptionId,selected.BaseWorkedDays,selected.TransportBaseWorkedDays,e.CostCenterId,e.WorkSite,
        CONVERT(bit,CASE WHEN r.SelectionInitialized=0 OR selected.IsSelected=1
            THEN 1 ELSE 0 END) AS IsSelected
    FROM payroll.Runs r
    JOIN payroll.Employments e ON e.TenantId=r.TenantId AND e.BusinessId=r.BusinessId
        AND e.IsActive=1 AND e.PayFrequencyOptionId=r.PayFrequencyOptionId
        AND e.StartDate<=r.PeriodEnd AND (e.EndDate IS NULL OR e.EndDate>=r.PeriodStart)
    LEFT JOIN payroll.RunEmploymentInputs selected ON selected.TenantId=r.TenantId
        AND selected.PayrollRunId=r.PayrollRunId AND selected.EmploymentId=e.EmploymentId
    WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.PayrollRunId=@RunId
);
