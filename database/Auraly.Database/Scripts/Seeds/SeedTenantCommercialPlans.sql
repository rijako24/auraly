SET NOCOUNT ON;

DECLARE @Now DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();
DECLARE @BillingBusinessId UNIQUEIDENTIFIER='A0A10000-0000-0000-0000-000000000001';
DECLARE @SalesTaxProfileId UNIQUEIDENTIFIER=(
    SELECT TaxProfileId FROM dbo.TaxProfiles
    WHERE BusinessId=@BillingBusinessId AND DianTaxCode=N'01' AND Rate=19 AND IsActive=1);

IF @SalesTaxProfileId IS NULL
    THROW 51000,'SeedTenantCommercialPlans requiere el IVA de venta de Auraly.',1;

IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Resource=N'subscription.manage')
    INSERT dbo.Permissions(PermissionId,Module,Action,Resource,Description,CreatedAt)
    VALUES(NEWID(),N'Subscription',N'Manage',N'subscription.manage',
           N'Consultar y modificar la renovación de la suscripción',SYSUTCDATETIME());

DECLARE @Services TABLE
(
    Id UNIQUEIDENTIFIER NOT NULL, Code NVARCHAR(48) NOT NULL, Name NVARCHAR(120) NOT NULL,
    Description NVARCHAR(500) NULL, UnitLabel NVARCHAR(80) NOT NULL, UnitSize INT NOT NULL,
    MonthlyNetPrice DECIMAL(19,4) NOT NULL
);
INSERT @Services VALUES
 ('13000000-0000-0000-0000-000000000000',N'starter',N'Plan Inicio',N'Suscripción mensual al software Auraly para una operación pequeña.',N'plan',1,60000),
 ('13000000-0000-0000-0000-000000000001',N'essential',N'Plan Esencial',N'Suscripción mensual al software Auraly.',N'plan',1,119900),
 ('13000000-0000-0000-0000-000000000002',N'business',N'Plan Negocio',N'Suscripción mensual al software Auraly.',N'plan',1,299900),
 ('13000000-0000-0000-0000-000000000003',N'company',N'Plan Empresa',N'Suscripción mensual al software Auraly.',N'plan',1,449900),
 ('13000000-0000-0000-0000-000000000004',N'corporate',N'Personalizado',N'Suscripción de software con capacidad superior al plan Empresa.',N'plan',1,0),
 ('13000000-0000-0000-0000-000000000101',N'full_user',N'Usuario completo adicional',N'Capacidad mensual adicional de usuario completo.',N'usuario',1,30000),
 ('13000000-0000-0000-0000-000000000102',N'seller_user',N'Usuario vendedor adicional',N'Capacidad mensual adicional de usuario vendedor.',N'usuario',1,10000),
 ('13000000-0000-0000-0000-000000000103',N'pos_device',N'Caja adicional',N'Capacidad mensual adicional de punto de venta.',N'caja',1,20000),
 ('13000000-0000-0000-0000-000000000104',N'dian_document_pack',N'Paquete documentos DIAN',N'Capacidad mensual de documentos electrónicos.',N'1.000 documentos',1000,20000),
 ('13000000-0000-0000-0000-000000000105',N'payroll_employee_pack',N'Paquete empleados de nómina',N'Capacidad mensual para liquidar nómina.',N'10 empleados',10,25000);

MERGE billing.BillableServices AS target
USING @Services AS source
ON target.BusinessId=@BillingBusinessId AND target.Code=source.Code
WHEN MATCHED THEN UPDATE SET Name=source.Name,Description=source.Description,
 UnitLabel=source.UnitLabel,UnitSize=source.UnitSize,CurrencyCode='COP',UnitPrice=source.MonthlyNetPrice,
 SalesTaxProfileId=@SalesTaxProfileId,IsActive=1,UpdatedAt=@Now
WHEN NOT MATCHED THEN INSERT
 (BillableServiceId,BusinessId,Code,Name,Description,UnitLabel,UnitSize,CurrencyCode,UnitPrice,
  SalesTaxProfileId,IsActive,CreatedAt,UpdatedAt)
 VALUES(source.Id,@BillingBusinessId,source.Code,source.Name,source.Description,source.UnitLabel,
  source.UnitSize,'COP',source.MonthlyNetPrice,@SalesTaxProfileId,1,@Now,@Now);

MERGE billing.TenantCommercialPlans AS target
USING (VALUES
 ('11000000-0000-0000-0000-000000000000',N'starter',CAST(0.15 AS decimal(9,6)),1,0,1,100,0,0,0,N'["POS","Facturación electrónica","100 documentos DIAN al mes"]'),
 ('11000000-0000-0000-0000-000000000001',N'essential',CAST(0.15 AS decimal(9,6)),3,0,1,500,10,0,0,N'["POS","Facturación electrónica","Contabilidad","Nómina"]'),
 ('11000000-0000-0000-0000-000000000002',N'business',CAST(0.15 AS decimal(9,6)),8,0,3,1500,30,1,0,N'["POS","Facturación electrónica","Contabilidad","Nómina","Soporte prioritario"]'),
 ('11000000-0000-0000-0000-000000000003',N'company',CAST(0.15 AS decimal(9,6)),12,0,5,3000,100,0,0,N'["POS","Facturación electrónica","Contabilidad","Nómina","Soporte prioritario"]'),
 ('11000000-0000-0000-0000-000000000004',N'corporate',CAST(0.15 AS decimal(9,6)),0,0,0,0,0,0,1,N'["Capacidad superior a Empresa","Acompañamiento especializado"]')
) source(Id,Code,AnnualDiscount,FullUsers,SellerUsers,PosDevices,DianDocuments,PayrollEmployees,Recommended,Custom,Features)
ON target.BillableServiceId=(SELECT BillableServiceId FROM billing.BillableServices WHERE BusinessId=@BillingBusinessId AND Code=source.Code)
WHEN MATCHED THEN UPDATE SET AnnualDiscountRate=source.AnnualDiscount,
 IncludedFullUsers=source.FullUsers,IncludedSellerUsers=source.SellerUsers,
 IncludedPosDevices=source.PosDevices,IncludedDianDocuments=source.DianDocuments,
 IncludedPayrollEmployees=source.PayrollEmployees,IsRecommended=source.Recommended,
 IsCustom=source.Custom,FeaturesJson=source.Features,IsActive=1,UpdatedAt=@Now
WHEN NOT MATCHED THEN INSERT
 (TenantCommercialPlanId,BillableServiceId,AnnualDiscountRate,IncludedFullUsers,
  IncludedSellerUsers,IncludedPosDevices,IncludedDianDocuments,IncludedPayrollEmployees,
  IsRecommended,IsCustom,IsActive,FeaturesJson,CreatedAt,UpdatedAt)
VALUES(CONVERT(uniqueidentifier,source.Id),
 (SELECT BillableServiceId FROM billing.BillableServices WHERE BusinessId=@BillingBusinessId AND Code=source.Code),
 source.AnnualDiscount,source.FullUsers,source.SellerUsers,source.PosDevices,source.DianDocuments,
 source.PayrollEmployees,source.Recommended,source.Custom,1,source.Features,@Now,@Now);

MERGE billing.TenantCommercialAddOns AS target
USING (VALUES
 ('12000000-0000-0000-0000-000000000001',N'full_user'),
 ('12000000-0000-0000-0000-000000000002',N'seller_user'),
 ('12000000-0000-0000-0000-000000000003',N'pos_device'),
 ('12000000-0000-0000-0000-000000000004',N'dian_document_pack'),
 ('12000000-0000-0000-0000-000000000005',N'payroll_employee_pack')
) source(Id,Code)
ON target.BillableServiceId=(SELECT BillableServiceId FROM billing.BillableServices WHERE BusinessId=@BillingBusinessId AND Code=source.Code)
WHEN MATCHED THEN UPDATE SET IsActive=1,UpdatedAt=@Now
WHEN NOT MATCHED THEN INSERT(TenantCommercialAddOnId,BillableServiceId,IsActive,CreatedAt,UpdatedAt)
VALUES(CONVERT(uniqueidentifier,source.Id),
 (SELECT BillableServiceId FROM billing.BillableServices WHERE BusinessId=@BillingBusinessId AND Code=source.Code),1,@Now,@Now);
GO
