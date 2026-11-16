-- CustomerProductPriceResolve was retired by d122f0a1 in favor of
-- CommercePriceResolver. Retire only the reviewed internal definition, before
-- DacFx binds the remaining schema. No price, document or ledger row changes.
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.CustomerProductPriceResolve') IS NOT NULL
BEGIN
    IF NOT EXISTS(SELECT 1 FROM sys.sql_modules
      WHERE object_id=OBJECT_ID(N'dbo.CustomerProductPriceResolve',N'IF')
        AND HASHBYTES('SHA2_256',CONVERT(varbinary(max),REPLACE(definition,CHAR(13),N'')))=
          0x544FA55050602E63226019AA457BBC36DE8BB8EA5E6BF4DD5236F82953945493)
      THROW 51980,N'La función de precios difiere de la versión retirada revisada; no se modificó.',1;
    IF EXISTS(SELECT 1 FROM sys.sql_expression_dependencies
      WHERE referenced_id=OBJECT_ID(N'dbo.CustomerProductPriceResolve'))
      THROW 51981,N'La función de precios tiene dependientes; se requiere revisar su migración.',1;
    DROP FUNCTION dbo.CustomerProductPriceResolve;
END;
