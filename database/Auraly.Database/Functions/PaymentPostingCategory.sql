CREATE FUNCTION accounting.PaymentPostingCategory(@SourceType nvarchar(64),@MethodCode nvarchar(32))
RETURNS TABLE
AS RETURN
(
    SELECT mapping.Category FROM dbo.AccountingConfigurationProfiles profile
    JOIN dbo.AccountingSourceCategoryMappings mapping ON mapping.ProfileCode=profile.ProfileCode
      AND mapping.SourceType=@SourceType AND mapping.SourceCode=@MethodCode
    WHERE profile.IsDefault=1 AND profile.IsActive=1
);
GO
