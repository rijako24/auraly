IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE [name] = N'CK_ProductLinks_Capability'
      AND [parent_object_id] = OBJECT_ID(N'dbo.ProductLinks')
)
BEGIN
    ALTER TABLE dbo.ProductLinks DROP CONSTRAINT CK_ProductLinks_Capability;
END;
GO
