SET NOCOUNT ON;

DECLARE @Now DATETIMEOFFSET(7)=SYSUTCDATETIME();
DECLARE @Options TABLE
(
    OptionId UNIQUEIDENTIFIER NOT NULL,
    CatalogCode NVARCHAR(64) NOT NULL,
    Code NVARCHAR(64) NOT NULL,
    Label NVARCHAR(160) NOT NULL,
    Description NVARCHAR(500) NULL,
    MetadataCode NVARCHAR(64) NULL,
    SortOrder INT NOT NULL
);

INSERT @Options VALUES
('71000000-0000-0000-0000-000000000001',N'payroll-contract-type',N'Indefinite',N'Término indefinido',NULL,NULL,10),
('71000000-0000-0000-0000-000000000002',N'payroll-contract-type',N'FixedTerm',N'Término fijo',NULL,NULL,20),
('71000000-0000-0000-0000-000000000003',N'payroll-contract-type',N'WorkOrLabor',N'Obra o labor',NULL,NULL,30),
('71100000-0000-0000-0000-000000000001',N'payroll-salary-type',N'Ordinary',N'Salario ordinario',NULL,NULL,10),
('71100000-0000-0000-0000-000000000002',N'payroll-salary-type',N'Integral',N'Salario integral',NULL,NULL,20),
('71200000-0000-0000-0000-000000000001',N'payroll-pay-frequency',N'Monthly',N'Mensual',NULL,N'30',10),
('71200000-0000-0000-0000-000000000002',N'payroll-pay-frequency',N'Semimonthly',N'Quincenal',NULL,N'15',20),
('71300000-0000-0000-0000-000000000001',N'payroll-risk-class',N'I',N'Clase I',NULL,N'0.00522',10),
('71300000-0000-0000-0000-000000000002',N'payroll-risk-class',N'II',N'Clase II',NULL,N'0.01044',20),
('71300000-0000-0000-0000-000000000003',N'payroll-risk-class',N'III',N'Clase III',NULL,N'0.02436',30),
('71300000-0000-0000-0000-000000000004',N'payroll-risk-class',N'IV',N'Clase IV',NULL,N'0.04350',40),
('71300000-0000-0000-0000-000000000005',N'payroll-risk-class',N'V',N'Clase V',NULL,N'0.06960',50),
('71400000-0000-0000-0000-000000000001',N'payroll-worker-type',N'01',N'Dependiente',NULL,NULL,10),
('71500000-0000-0000-0000-000000000001',N'payroll-worker-subtype',N'00',N'No aplica subtipo',NULL,NULL,10),
('71600000-0000-0000-0000-000000000001',N'payroll-payment-method',N'BankTransfer',N'Transferencia bancaria',NULL,N'Bank',10),
('71600000-0000-0000-0000-000000000002',N'payroll-payment-method',N'Cash',N'Efectivo',NULL,N'Cash',20),
('71610000-0000-0000-0000-000000000001',N'payroll-bank-account-type',N'Savings',N'Cuenta de ahorros',NULL,N'Ahorros',10),
('71610000-0000-0000-0000-000000000002',N'payroll-bank-account-type',N'Checking',N'Cuenta corriente',NULL,N'Corriente',20),
('71620000-0000-0000-0000-000000000001',N'payroll-bank',N'BancoBogota',N'Banco de Bogotá',NULL,N'001',10),
('71620000-0000-0000-0000-000000000002',N'payroll-bank',N'BancoPopular',N'Banco Popular',NULL,N'002',20),
('71620000-0000-0000-0000-000000000003',N'payroll-bank',N'Bancolombia',N'Bancolombia',NULL,N'007',30),
('71620000-0000-0000-0000-000000000004',N'payroll-bank',N'ScotiabankColpatria',N'Scotiabank Colpatria',NULL,N'009',40),
('71620000-0000-0000-0000-000000000005',N'payroll-bank',N'BBVA',N'BBVA Colombia',NULL,N'013',50),
('71620000-0000-0000-0000-000000000006',N'payroll-bank',N'BancoOccidente',N'Banco de Occidente',NULL,N'023',60),
('71620000-0000-0000-0000-000000000007',N'payroll-bank',N'BancoCajaSocial',N'Banco Caja Social',NULL,N'032',70),
('71620000-0000-0000-0000-000000000008',N'payroll-bank',N'Davivienda',N'Davivienda',NULL,N'051',80),
('71620000-0000-0000-0000-000000000009',N'payroll-bank',N'AVVillas',N'Banco AV Villas',NULL,N'052',90),
('71620000-0000-0000-0000-00000000000A',N'payroll-bank',N'Pichincha',N'Banco Pichincha',NULL,N'060',100),
('71620000-0000-0000-0000-00000000000B',N'payroll-bank',N'Falabella',N'Banco Falabella',NULL,N'062',110),
('71620000-0000-0000-0000-00000000000C',N'payroll-bank',N'Finandina',N'Banco Finandina',NULL,N'063',120),
('71700000-0000-0000-0000-000000000001',N'payroll-concept-nature',N'Earning',N'Devengado',NULL,NULL,10),
('71700000-0000-0000-0000-000000000002',N'payroll-concept-nature',N'Deduction',N'Deducción',NULL,NULL,20),
('71700000-0000-0000-0000-000000000003',N'payroll-concept-nature',N'EmployerContribution',N'Aporte del empleador',NULL,NULL,30),
('71700000-0000-0000-0000-000000000004',N'payroll-concept-nature',N'Provision',N'Provisión',NULL,NULL,40),
('71800000-0000-0000-0000-000000000001',N'payroll-calculation-method',N'FixedAmount',N'Valor fijo',NULL,NULL,10),
('71800000-0000-0000-0000-000000000002',N'payroll-calculation-method',N'QuantityByRate',N'Cantidad por tarifa',NULL,NULL,20),
('71800000-0000-0000-0000-000000000003',N'payroll-calculation-method',N'PercentageOfBase',N'Porcentaje de base',NULL,NULL,30),
('71800000-0000-0000-0000-000000000004',N'payroll-calculation-method',N'Statutory',N'Regla legal',NULL,NULL,40),
('71900000-0000-0000-0000-000000000001',N'payroll-concept-treatment',N'Salary',N'Salarial',NULL,NULL,10),
('71900000-0000-0000-0000-000000000002',N'payroll-concept-treatment',N'NonSalary',N'No salarial',NULL,NULL,20),
('71900000-0000-0000-0000-000000000003',N'payroll-concept-treatment',N'StatutoryDeduction',N'Deducción legal',NULL,NULL,30),
('71900000-0000-0000-0000-000000000004',N'payroll-concept-treatment',N'AuthorizedDeduction',N'Deducción autorizada',NULL,NULL,40),
('71A00000-0000-0000-0000-000000000001',N'payroll-deduction-authority',N'Law',N'Autorización legal',NULL,NULL,10),
('71A00000-0000-0000-0000-000000000002',N'payroll-deduction-authority',N'WrittenAuthorization',N'Autorización escrita',NULL,NULL,20),
('71A00000-0000-0000-0000-000000000003',N'payroll-deduction-authority',N'JudicialOrder',N'Orden judicial',NULL,NULL,30),
('71B00000-0000-0000-0000-000000000001',N'payroll-novelty-type',N'Amount',N'Valor',NULL,NULL,10),
('71B00000-0000-0000-0000-000000000002',N'payroll-novelty-type',N'Hours',N'Horas',NULL,NULL,20),
('71B00000-0000-0000-0000-000000000003',N'payroll-novelty-type',N'Days',N'Días',NULL,NULL,30),
('71C00000-0000-0000-0000-000000000001',N'payroll-accounting-category',N'SalaryExpense',N'Gasto de salarios',NULL,NULL,10),
('71C00000-0000-0000-0000-000000000002',N'payroll-accounting-category',N'VariableEarningsExpense',N'Gasto de horas y recargos',NULL,NULL,20),
('71C00000-0000-0000-0000-000000000003',N'payroll-accounting-category',N'TransportAllowanceExpense',N'Gasto de auxilio de transporte',NULL,NULL,30),
('71C00000-0000-0000-0000-000000000004',N'payroll-accounting-category',N'EmployerContributionsExpense',N'Gasto de aportes del empleador',NULL,NULL,40),
('71C00000-0000-0000-0000-000000000005',N'payroll-accounting-category',N'BenefitsExpense',N'Gasto de prestaciones',NULL,NULL,50),
('71C00000-0000-0000-0000-000000000006',N'payroll-accounting-category',N'EmployeeContributionsPayable',N'Aportes del trabajador por pagar',NULL,NULL,60),
('71C00000-0000-0000-0000-000000000007',N'payroll-accounting-category',N'PayrollWithholdingPayable',N'Retención laboral por pagar',NULL,NULL,70),
('71C00000-0000-0000-0000-000000000008',N'payroll-accounting-category',N'ThirdPartyDeductionsPayable',N'Deducciones a terceros por pagar',NULL,NULL,80),
('71C00000-0000-0000-0000-000000000009',N'payroll-accounting-category',N'NetPayrollPayable',N'Nómina por pagar',NULL,NULL,90),
('71C00000-0000-0000-0000-00000000000A',N'payroll-accounting-category',N'BenefitsProvisionPayable',N'Provisiones laborales por pagar',NULL,NULL,100),
('71C00000-0000-0000-0000-00000000000B',N'payroll-accounting-category',N'EmployeeLoansReceivable',N'Préstamos a empleados',NULL,NULL,110),
('71C00000-0000-0000-0000-00000000000C',N'payroll-accounting-category',N'EmployerHealthPayable',N'Salud del empleador por pagar',NULL,NULL,120),
('71C00000-0000-0000-0000-00000000000D',N'payroll-accounting-category',N'EmployerPensionPayable',N'Pensión del empleador por pagar',NULL,NULL,130),
('71C00000-0000-0000-0000-00000000000E',N'payroll-accounting-category',N'OccupationalRiskPayable',N'Riesgos laborales por pagar',NULL,NULL,140),
('71C00000-0000-0000-0000-00000000000F',N'payroll-accounting-category',N'ParafiscalContributionsPayable',N'Aportes parafiscales por pagar',NULL,NULL,150),
('71D00000-0000-0000-0000-000000000001',N'payroll-dian-concept',N'BasicSalary',N'Sueldo trabajado',NULL,N'SueldoTrabajado',10),
('71D00000-0000-0000-0000-000000000002',N'payroll-dian-concept',N'TransportAllowance',N'Auxilio de transporte',NULL,N'AuxilioTransporte',20),
('71D00000-0000-0000-0000-000000000003',N'payroll-dian-concept',N'HealthDeduction',N'Deducción de salud',NULL,N'Salud',30),
('71D00000-0000-0000-0000-000000000004',N'payroll-dian-concept',N'PensionDeduction',N'Deducción de pensión',NULL,N'FondoPension',40),
('71D00000-0000-0000-0000-000000000005',N'payroll-dian-concept',N'OtherEarning',N'Otro devengado',NULL,N'OtroConcepto',50),
('71D00000-0000-0000-0000-000000000006',N'payroll-dian-concept',N'OtherDeduction',N'Otra deducción',NULL,N'OtraDeduccion',60);
INSERT @Options VALUES
('71E00000-0000-0000-0000-000000000001',N'payroll-system-concept-role',N'BasicSalary',N'Salario básico',NULL,NULL,10),
('71E00000-0000-0000-0000-000000000002',N'payroll-system-concept-role',N'TransportAllowance',N'Auxilio de transporte',NULL,NULL,20),
('71E00000-0000-0000-0000-000000000003',N'payroll-system-concept-role',N'EmployeeHealth',N'Aporte de salud del trabajador',NULL,NULL,30),
('71E00000-0000-0000-0000-000000000004',N'payroll-system-concept-role',N'EmployeePension',N'Aporte de pensión del trabajador',NULL,NULL,40),
('71E00000-0000-0000-0000-000000000005',N'payroll-system-concept-role',N'EmployerHealth',N'Aporte de salud del empleador',NULL,NULL,50),
('71E00000-0000-0000-0000-000000000006',N'payroll-system-concept-role',N'EmployerPension',N'Aporte de pensión del empleador',NULL,NULL,60),
('71E00000-0000-0000-0000-000000000007',N'payroll-system-concept-role',N'OccupationalRisk',N'Aporte de riesgos laborales',NULL,NULL,70),
('71E00000-0000-0000-0000-000000000008',N'payroll-system-concept-role',N'CompensationFund',N'Aporte a caja de compensación',NULL,NULL,80),
('71E00000-0000-0000-0000-000000000009',N'payroll-system-concept-role',N'Sena',N'Aporte SENA',NULL,NULL,90),
('71E00000-0000-0000-0000-00000000000A',N'payroll-system-concept-role',N'Icbf',N'Aporte ICBF',NULL,NULL,100),
('71E00000-0000-0000-0000-00000000000B',N'payroll-system-concept-role',N'SeveranceProvision',N'Provisión de cesantías',NULL,NULL,110),
('71E00000-0000-0000-0000-00000000000C',N'payroll-system-concept-role',N'SeveranceInterestProvision',N'Provisión de intereses de cesantías',NULL,NULL,120),
('71E00000-0000-0000-0000-00000000000D',N'payroll-system-concept-role',N'ServiceBonusProvision',N'Provisión de prima',NULL,NULL,130),
('71E00000-0000-0000-0000-00000000000E',N'payroll-system-concept-role',N'VacationProvision',N'Provisión de vacaciones',NULL,NULL,140),
('71E00000-0000-0000-0000-00000000000F',N'payroll-system-concept-role',N'LaborWithholding',N'Retención laboral',NULL,NULL,150);
INSERT @Options VALUES
('71F00000-0000-0000-0000-000000000001',N'payroll-rule-parameter',N'MonthlyDays',N'Días base del mes',NULL,N'Days',10),
('71F00000-0000-0000-0000-000000000002',N'payroll-rule-parameter',N'MinimumMonthlySalary',N'Salario mínimo mensual',NULL,N'COP',20),
('71F00000-0000-0000-0000-000000000003',N'payroll-rule-parameter',N'TransportAllowance',N'Auxilio de transporte mensual',NULL,N'COP',30),
('71F00000-0000-0000-0000-000000000004',N'payroll-rule-parameter',N'TransportAllowanceSalaryLimitMultiple',N'Límite salarial para auxilio',NULL,N'Multiple',40),
('71F00000-0000-0000-0000-000000000005',N'payroll-rule-parameter',N'EmployeeHealthRate',N'Salud del trabajador',NULL,N'Rate',50),
('71F00000-0000-0000-0000-000000000006',N'payroll-rule-parameter',N'EmployeePensionRate',N'Pensión del trabajador',NULL,N'Rate',60),
('71F00000-0000-0000-0000-000000000007',N'payroll-rule-parameter',N'EmployerHealthRate',N'Salud del empleador',NULL,N'Rate',70),
('71F00000-0000-0000-0000-000000000008',N'payroll-rule-parameter',N'EmployerPensionRate',N'Pensión del empleador',NULL,N'Rate',80),
('71F00000-0000-0000-0000-000000000009',N'payroll-rule-parameter',N'CompensationFundRate',N'Caja de compensación',NULL,N'Rate',90),
('71F00000-0000-0000-0000-00000000000A',N'payroll-rule-parameter',N'SenaRate',N'Aporte SENA',NULL,N'Rate',100),
('71F00000-0000-0000-0000-00000000000B',N'payroll-rule-parameter',N'IcbfRate',N'Aporte ICBF',NULL,N'Rate',110),
('71F00000-0000-0000-0000-00000000000C',N'payroll-rule-parameter',N'SeveranceRate',N'Provisión de cesantías',NULL,N'Rate',120),
('71F00000-0000-0000-0000-00000000000D',N'payroll-rule-parameter',N'SeveranceInterestRate',N'Intereses de cesantías',NULL,N'Rate',130),
('71F00000-0000-0000-0000-00000000000E',N'payroll-rule-parameter',N'ServiceBonusRate',N'Provisión de prima',NULL,N'Rate',140),
('71F00000-0000-0000-0000-00000000000F',N'payroll-rule-parameter',N'VacationRate',N'Provisión de vacaciones',NULL,N'Rate',150),
('71F00000-0000-0000-0000-000000000010',N'payroll-rule-parameter',N'IntegralSalaryContributionBaseRate',N'Base de aportes del salario integral',NULL,N'Rate',160),
('71F00000-0000-0000-0000-000000000011',N'payroll-rule-parameter',N'MaximumContributionBaseMinimumWages',N'Tope de IBC en salarios mínimos',NULL,N'Multiple',170),
('71F00000-0000-0000-0000-000000000012',N'payroll-rule-parameter',N'NonSalaryExclusionThresholdRate',N'Límite no salarial excluido del IBC',NULL,N'Rate',180);

MERGE [payroll].[CatalogOptions] AS target
USING @Options AS source
ON target.[CatalogCode]=source.[CatalogCode] AND target.[Code]=source.[Code]
WHEN MATCHED THEN UPDATE SET
    target.[Label]=source.[Label],target.[Description]=source.[Description],
    target.[MetadataCode]=source.[MetadataCode],target.[SortOrder]=source.[SortOrder],
    target.[IsActive]=1,target.[UpdatedAt]=@Now
WHEN NOT MATCHED THEN INSERT
    ([OptionId],[CatalogCode],[Code],[Label],[Description],[MetadataCode],[IsActive],[SortOrder],[CreatedAt])
    VALUES(source.[OptionId],source.[CatalogCode],source.[Code],source.[Label],source.[Description],source.[MetadataCode],1,source.[SortOrder],@Now);

UPDATE payroll.CatalogOptions SET DianCode=N'2'
WHERE CatalogCode=N'payroll-contract-type' AND Code=N'Indefinite';
UPDATE payroll.CatalogOptions SET DianCode=N'1'
WHERE CatalogCode=N'payroll-contract-type' AND Code=N'FixedTerm';
UPDATE payroll.CatalogOptions SET DianCode=N'3'
WHERE CatalogCode=N'payroll-contract-type' AND Code=N'WorkOrLabor';
UPDATE payroll.CatalogOptions SET DianCode=N'5'
WHERE CatalogCode=N'payroll-pay-frequency' AND Code=N'Monthly';
UPDATE payroll.CatalogOptions SET DianCode=N'4'
WHERE CatalogCode=N'payroll-pay-frequency' AND Code=N'Semimonthly';
UPDATE payroll.CatalogOptions SET DianCode=N'42'
WHERE CatalogCode=N'payroll-payment-method' AND Code=N'BankTransfer';
UPDATE payroll.CatalogOptions SET DianCode=N'10'
WHERE CatalogCode=N'payroll-payment-method' AND Code=N'Cash';
UPDATE payroll.CatalogOptions SET DianCode=Code
WHERE CatalogCode IN(N'payroll-worker-type',N'payroll-worker-subtype');

MERGE payroll.CatalogOptions AS target
USING (VALUES
 ('71AA0000-0000-0000-0000-000000000001',N'payroll-identification-type',N'CC',N'Cédula de ciudadanía',N'13',10),
 ('71AA0000-0000-0000-0000-000000000002',N'payroll-identification-type',N'CE',N'Cédula de extranjería',N'22',20),
 ('71AA0000-0000-0000-0000-000000000003',N'payroll-identification-type',N'TI',N'Tarjeta de identidad',N'12',30),
 ('71AA0000-0000-0000-0000-000000000004',N'payroll-identification-type',N'PA',N'Pasaporte',N'41',40),
 ('71AA0000-0000-0000-0000-000000000005',N'payroll-identification-type',N'PPT',N'Permiso por protección temporal',N'48',50)
) AS source(OptionId,CatalogCode,Code,Label,DianCode,SortOrder)
ON target.CatalogCode=source.CatalogCode AND target.Code=source.Code
WHEN MATCHED THEN UPDATE SET Label=source.Label,DianCode=source.DianCode,
  IsActive=1,SortOrder=source.SortOrder,UpdatedAt=@Now
WHEN NOT MATCHED THEN INSERT
  (OptionId,CatalogCode,Code,Label,DianCode,IsActive,SortOrder,CreatedAt)
  VALUES(source.OptionId,source.CatalogCode,source.Code,source.Label,source.DianCode,1,source.SortOrder,@Now);
