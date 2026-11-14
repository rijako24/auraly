-- Typed inputs for the existing payroll calculation command. These types store
-- no business state; results remain in RunEmployees/RunLines in its transaction.
CREATE TYPE [payroll].[CalculationEmployeeInput] AS TABLE
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [EmploymentId] UNIQUEIDENTIFIER NOT NULL UNIQUE,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [WorkedDays] DECIMAL(9,4) NOT NULL,
    [Earnings] DECIMAL(19,4) NOT NULL,
    [Deductions] DECIMAL(19,4) NOT NULL,
    [EmployerContributions] DECIMAL(19,4) NOT NULL,
    [Provisions] DECIMAL(19,4) NOT NULL,
    [NetPayable] DECIMAL(19,4) NOT NULL,
    [AppliesTransportAllowance] BIT NOT NULL,
    [EmploymentVersion] BINARY(8) NOT NULL,
    [Hash] BINARY(32) NOT NULL
);
GO
CREATE TYPE [payroll].[CalculationLineInput] AS TABLE
(
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL,
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Number] INT NOT NULL,
    [ConceptId] UNIQUEIDENTIFIER NOT NULL,
    [NoveltyId] UNIQUEIDENTIFIER NULL,
    [DeductionAgreementId] UNIQUEIDENTIFIER NULL,
    [Quantity] DECIMAL(19,6) NOT NULL,
    [Rate] DECIMAL(19,8) NULL,
    [BaseAmount] DECIMAL(19,4) NULL,
    [Amount] DECIMAL(19,4) NOT NULL,
    [IsEmployerCost] BIT NOT NULL,
    [IsSalaryBase] BIT NOT NULL,
    UNIQUE ([EmployeeId],[Number])
);
GO
-- V1 remains available during the additive schema/API cutover.
CREATE TYPE [payroll].[CalculationEmployeeInputV2] AS TABLE
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [EmploymentId] UNIQUEIDENTIFIER NOT NULL UNIQUE,
    [PartyId] UNIQUEIDENTIFIER NOT NULL,
    [WorkedDays] DECIMAL(9,4) NOT NULL,
    [Earnings] DECIMAL(19,4) NOT NULL,
    [Deductions] DECIMAL(19,4) NOT NULL,
    [EmployerContributions] DECIMAL(19,4) NOT NULL,
    [Provisions] DECIMAL(19,4) NOT NULL,
    [NetPayable] DECIMAL(19,4) NOT NULL,
    [AppliesTransportAllowance] BIT NOT NULL,
    [EmploymentVersion] BINARY(8) NOT NULL,
    [Hash] BINARY(32) NOT NULL,
    [BaseWorkedDays] DECIMAL(9,4) NOT NULL,
    [MaximumContractDays] DECIMAL(9,4) NOT NULL,
    [RiskRate] DECIMAL(19,8) NOT NULL,
    [NoveltyVersionHash] BINARY(32) NOT NULL
);
GO
