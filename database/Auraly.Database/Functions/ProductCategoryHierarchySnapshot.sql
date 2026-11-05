CREATE FUNCTION [catalog].[ProductCategoryHierarchySnapshot](@TenantId UNIQUEIDENTIFIER,@CategoryId UNIQUEIDENTIFIER)
RETURNS TABLE
AS RETURN
(
    WITH ancestors AS(
      SELECT ProductCategoryId,ParentProductCategoryId,Name,0 Hop
      FROM dbo.ProductCategories WHERE TenantId=@TenantId AND ProductCategoryId=@CategoryId
      UNION ALL
      SELECT parent.ProductCategoryId,parent.ParentProductCategoryId,parent.Name,child.Hop+1
      FROM ancestors child JOIN dbo.ProductCategories parent ON parent.TenantId=@TenantId
        AND parent.ProductCategoryId=child.ParentProductCategoryId WHERE child.Hop<3
    ), levels AS(SELECT *,MAX(Hop) OVER()-Hop Depth FROM ancestors)
    SELECT (SELECT
      MAX(CASE WHEN Depth=0 THEN CONVERT(nvarchar(36),ProductCategoryId) END) AreaId,
      MAX(CASE WHEN Depth=0 THEN Name END) AreaName,
      MAX(CASE WHEN Depth=1 THEN CONVERT(nvarchar(36),ProductCategoryId) END) LineId,
      MAX(CASE WHEN Depth=1 THEN Name END) LineName,
      MAX(CASE WHEN Depth=2 THEN CONVERT(nvarchar(36),ProductCategoryId) END) GroupId,
      MAX(CASE WHEN Depth=2 THEN Name END) GroupName,
      MAX(CASE WHEN Depth=3 THEN CONVERT(nvarchar(36),ProductCategoryId) END) SubgroupId,
      MAX(CASE WHEN Depth=3 THEN Name END) SubgroupName
      FROM levels FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) JsonValue
);
GO
