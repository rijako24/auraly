-- A profile references an existing rule series (country + code).
-- A global anchor admits this tenant's published versions. Once a local version
-- is effective it owns that series, including retirement/expiry; never fall
-- back to global percentages to hide an invalid local configuration.
-- A tenant anchor never consumes another scope's versions.
-- Published versions supersede earlier versions from their EffectiveFrom.
-- Editing a profile publishes its private series. Before that series starts,
-- the explicitly configured anchor remains applicable; other profiles keep it.
-- A same-date correction selects the newest immutable revision.
-- Retiring the newest version never silently restores an older percentage.
CREATE FUNCTION payroll.ProfileRuleConfiguration
(
    @TenantId UNIQUEIDENTIFIER,
    @ProfileId UNIQUEIDENTIFIER,
    @PeriodStart DATE,
    @PeriodEnd DATE
)
RETURNS TABLE
AS RETURN
(
    SELECT chosen.RuleSetId,
      CONVERT(nvarchar(40),CASE
        WHEN profile.ProfileId IS NULL OR profile.IsActive=0 THEN N'ProfileUnavailable'
        WHEN anchor.RuleSetId IS NULL OR (anchor.TenantId IS NOT NULL AND anchor.TenantId<>@TenantId) THEN N'RuleSeriesUnavailable'
        WHEN chosen.RuleSetId IS NULL OR chosen.Status<>N'Approved' THEN N'ParametersNotApproved'
        WHEN chosen.EffectiveTo<@PeriodEnd THEN N'ParametersExpired'
        WHEN EXISTS(SELECT 1 FROM payroll.RuleSets boundary
          WHERE boundary.CountryCode=anchor.CountryCode AND boundary.Code IN(chosen.Code,own.Code)
            AND (boundary.TenantId=@TenantId OR boundary.TenantId IS NULL AND chosen.TenantId IS NULL)
            AND boundary.Status IN(N'Approved',N'Retired')
            AND boundary.EffectiveFrom>@PeriodStart AND boundary.EffectiveFrom<=@PeriodEnd)
          THEN N'PeriodCrossesVersions' END) Issue
    FROM (VALUES(1)) singleton(Value)
    LEFT JOIN payroll.AccountingProfiles profile ON profile.ProfileId=@ProfileId AND profile.TenantId=@TenantId
    LEFT JOIN payroll.RuleSets anchor ON anchor.RuleSetId=profile.RuleSetId
    CROSS APPLY(SELECT N'PROFILE-'+UPPER(REPLACE(CONVERT(nvarchar(36),@ProfileId),N'-',N'')) Code) own
    OUTER APPLY(SELECT TOP(1) version.RuleSetId,version.TenantId,version.Status,version.EffectiveTo,version.Code
      FROM payroll.RuleSets version
      WHERE version.CountryCode=anchor.CountryCode AND version.Code IN(anchor.Code,own.Code)
        AND (version.TenantId=@TenantId OR version.TenantId IS NULL AND anchor.TenantId IS NULL)
        AND version.Status IN(N'Approved',N'Retired') AND version.EffectiveFrom<=@PeriodStart
      ORDER BY CASE WHEN version.Code=own.Code THEN 0 ELSE 1 END,
        CASE WHEN version.TenantId=@TenantId THEN 0 ELSE 1 END,version.EffectiveFrom DESC,version.Revision DESC) chosen
);
GO
