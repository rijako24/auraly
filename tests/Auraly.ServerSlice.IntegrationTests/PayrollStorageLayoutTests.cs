using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PayrollStorageLayoutCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Payroll storage layout upgrade";
}

[Collection(PayrollStorageLayoutCollection.Name)]
public sealed class PayrollStorageLayoutTests(ServerSliceFixture fixture)
{
    // Run separately from latency benchmarks: publishing the DACPAC launches SqlPackage.
    [Fact]
    public async Task Publishing_aggregate_clustered_keys_preserves_results_hashes_inputs_and_payment_references()
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        var runId=Guid.NewGuid();var employmentId=Guid.NewGuid();var employeeId=Guid.NewGuid();var conceptId=Guid.NewGuid();var batchId=Guid.NewGuid();var damageId=Guid.NewGuid();
        await using(var seed=new SqlCommand("""
            INSERT dbo.InventoryOperations(InventoryOperationId,BusinessId,DocumentType,WarehouseId,OccurredAt,
              ReasonCode,ReasonDescription,Status,CreatedAt,DamageDisposition)
            VALUES(@Damage,@Business,N'Damage',@Warehouse,SYSDATETIMEOFFSET(),N'DAMAGE',N'Avería irreversible de prueba',
              N'Draft',SYSDATETIMEOFFSET(),N'Irreversible');
            DECLARE @Frequency uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-pay-frequency' AND Code=N'Monthly');
            DECLARE @Cash uniqueidentifier=(SELECT OptionId FROM payroll.CatalogOptions WHERE CatalogCode=N'payroll-payment-method' AND Code=N'Cash');
            DECLARE @Rules uniqueidentifier=NEWID();
            INSERT dbo.Employees(EmployeeId,TenantId,PartyId,Name,IsActive,CreatedAt)
            VALUES(NEWID(),@Tenant,@Party,N'Migración de almacenamiento',1,SYSUTCDATETIME());
            INSERT payroll.RuleSets(RuleSetId,TenantId,CountryCode,Code,Name,EffectiveFrom,SourceReference,Status,CreatedAt)
            VALUES(@Rules,@Tenant,N'CO',CONVERT(nvarchar(36),@Run),N'Migración',N'2026-01-01',N'Fixture de migración',N'Approved',SYSUTCDATETIME());
            INSERT payroll.Concepts(ConceptId,TenantId,Code,Name,NatureOptionId,CalculationMethodOptionId,TreatmentOptionId,
                AccountingCategoryOptionId,IsSalaryBase,IsSocialSecurityBase,IsBenefitsBase,IsTaxWithholdingBase,
                RequiresDeductionAgreement,EffectiveFrom,IsActive,CreatedBy,CreatedAt)
            SELECT @Concept,@Tenant,REPLACE(CONVERT(nvarchar(36),@Concept),N'-',N''),N'Ingreso histórico',nature.OptionId,method.OptionId,treatment.OptionId,
                category.OptionId,1,1,1,0,0,N'2026-01-01',1,@User,SYSUTCDATETIME()
            FROM payroll.CatalogOptions nature CROSS JOIN payroll.CatalogOptions method CROSS JOIN payroll.CatalogOptions treatment CROSS JOIN payroll.CatalogOptions category
            WHERE nature.CatalogCode=N'payroll-concept-nature' AND nature.Code=N'Earning'
              AND method.CatalogCode=N'payroll-calculation-method' AND method.Code=N'FixedAmount'
              AND treatment.CatalogCode=N'payroll-concept-treatment' AND treatment.Code=N'Salary'
              AND category.CatalogCode=N'payroll-accounting-category' AND category.Code=N'SalaryExpense';
            INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,EmployeeId,ContractTypeOptionId,SalaryTypeOptionId,
                PayFrequencyOptionId,RiskClassOptionId,WorkerTypeOptionId,PaymentMethodOptionId,ContractNumber,StartDate,MonthlySalary,IsActive,CreatedBy,CreatedAt)
            SELECT @Employment,@Tenant,@Party,@Business,employee.EmployeeId,contract.OptionId,salary.OptionId,@Frequency,risk.OptionId,worker.OptionId,@Cash,
                CONVERT(nvarchar(36),@Employment),N'2026-01-01',1000,1,@User,SYSUTCDATETIME()
            FROM dbo.Employees employee CROSS JOIN payroll.CatalogOptions contract CROSS JOIN payroll.CatalogOptions salary
              CROSS JOIN payroll.CatalogOptions risk CROSS JOIN payroll.CatalogOptions worker
            WHERE employee.TenantId=@Tenant AND employee.PartyId=@Party
              AND contract.CatalogCode=N'payroll-contract-type' AND contract.Code=N'Indefinite'
              AND salary.CatalogCode=N'payroll-salary-type' AND salary.Code=N'Ordinary'
              AND risk.CatalogCode=N'payroll-risk-class' AND risk.Code=N'I'
              AND worker.CatalogCode=N'payroll-worker-type' AND worker.Code=N'01';
            INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,RunKind,PeriodStart,PeriodEnd,PaymentDate,Status,
                CalculationVersion,SelectionInitialized,InputHash,TotalEarnings,TotalDeductions,TotalEmployerContributions,TotalProvisions,NetPayable,CreatedBy,CreatedAt)
            VALUES(@Run,@Tenant,@Business,@Rules,@Frequency,N'Regular',N'2026-01-01',N'2026-01-31',N'2026-01-31',N'Approved',1,1,
                HASHBYTES('SHA2_256',CONVERT(nvarchar(36),@Run)),1200,0,0,0,1200,@User,SYSUTCDATETIME());
            INSERT payroll.RunEmploymentInputs(TenantId,PayrollRunId,EmploymentId,IsSelected,BaseWorkedDays)
            VALUES(@Tenant,@Run,@Employment,1,30);
            INSERT payroll.RunEmployees(PayrollRunEmployeeId,TenantId,PayrollRunId,EmploymentId,PartyId,EmployeeSnapshotJson,RuleSnapshotJson,
                WorkedDays,Earnings,Deductions,EmployerContributions,Provisions,NetPayable,CalculationHash)
            VALUES(@Employee,@Tenant,@Run,@Employment,@Party,N'{"SnapshotVersion":1,"EmployeeName":"Histórico","BaseWorkedDays":30}',N'{"monthlyDays":30}',
                30,1200,0,0,0,1200,HASHBYTES('SHA2_256',CONVERT(nvarchar(36),@Employee)));
            INSERT payroll.RunLines(PayrollRunLineId,PayrollRunEmployeeId,ConceptId,LineNumber,NatureCode,ConceptCode,ConceptName,
                AccountingCategoryCode,Quantity,Amount,IsEmployerCost,IsSalaryBase)
            VALUES(NEWID(),@Employee,@Concept,1,N'Earning',N'HIST',N'Salario histórico',N'SalaryExpense',30,1000,0,1),
                (NEWID(),@Employee,@Concept,2,N'Earning',N'HIST',N'Ingreso adicional',N'SalaryExpense',1,200,0,1);
            INSERT payroll.PaymentBatches(PaymentBatchId,TenantId,BusinessId,PaymentDate,PaymentMethodOptionId,ReferenceNumber,Status,TotalAmount,CreatedBy,CreatedAt)
            VALUES(@Batch,@Tenant,@Business,N'2026-01-31',@Cash,N'Pago histórico',N'Confirmed',1200,@User,SYSUTCDATETIME());
            INSERT payroll.PaymentLines(PaymentLineId,PaymentBatchId,PayrollRunEmployeeId,Amount)
            VALUES(NEWID(),@Batch,@Employee,1200);
            """,connection))
        {
            seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
            seed.Parameters.AddWithValue("@Damage",damageId);seed.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
            seed.Parameters.AddWithValue("@User",fixture.UserId);seed.Parameters.AddWithValue("@Party",fixture.SupplierPartyId);
            seed.Parameters.AddWithValue("@Run",runId);seed.Parameters.AddWithValue("@Employment",employmentId);
            seed.Parameters.AddWithValue("@Employee",employeeId);seed.Parameters.AddWithValue("@Concept",conceptId);seed.Parameters.AddWithValue("@Batch",batchId);
            await seed.ExecuteNonQueryAsync();
        }
        // Reproduce the previous published physical layout without altering a row.
        await using(var previous=new SqlCommand("""
            ALTER TABLE payroll.PaymentLines DROP CONSTRAINT FK_PayrollPaymentLines_RunEmployee;
            ALTER TABLE payroll.RunLines DROP CONSTRAINT FK_PayrollRunLines_RunEmployee;
            ALTER TABLE payroll.RunEmployees DROP CONSTRAINT PK_PayrollRunEmployees;
            ALTER TABLE payroll.RunEmployees DROP CONSTRAINT UQ_PayrollRunEmployees_Run_Employment;
            ALTER TABLE payroll.RunEmployees ADD CONSTRAINT PK_PayrollRunEmployees PRIMARY KEY CLUSTERED(PayrollRunEmployeeId);
            ALTER TABLE payroll.RunEmployees ADD CONSTRAINT UQ_PayrollRunEmployees_Run_Employment UNIQUE NONCLUSTERED(PayrollRunId,EmploymentId);
            ALTER TABLE payroll.RunLines DROP CONSTRAINT PK_PayrollRunLines;
            ALTER TABLE payroll.RunLines DROP CONSTRAINT UQ_PayrollRunLines_Employee_Number;
            ALTER TABLE payroll.RunLines ADD CONSTRAINT PK_PayrollRunLines PRIMARY KEY CLUSTERED(PayrollRunLineId);
            ALTER TABLE payroll.RunLines ADD CONSTRAINT UQ_PayrollRunLines_Employee_Number UNIQUE NONCLUSTERED(PayrollRunEmployeeId,LineNumber);
            ALTER TABLE payroll.RunLines WITH CHECK ADD CONSTRAINT FK_PayrollRunLines_RunEmployee FOREIGN KEY(PayrollRunEmployeeId) REFERENCES payroll.RunEmployees(PayrollRunEmployeeId);
            ALTER TABLE payroll.PaymentLines WITH CHECK ADD CONSTRAINT FK_PayrollPaymentLines_RunEmployee FOREIGN KEY(PayrollRunEmployeeId) REFERENCES payroll.RunEmployees(PayrollRunEmployeeId);
            """,connection)){await previous.ExecuteNonQueryAsync();}
        var before=await ReadRowsAsync(connection,runId,batchId,damageId);
        await connection.CloseAsync();
        await fixture.ReapplySchemaAsync();
        await connection.OpenAsync();
        Assert.Equal(before,await ReadRowsAsync(connection,runId,batchId,damageId));
        await using var indexes=new SqlCommand("""
            SELECT COUNT(*) FROM sys.indexes WHERE (object_id=OBJECT_ID(N'payroll.RunEmployees') AND
              ((name=N'PK_PayrollRunEmployees' AND type=2) OR (name=N'UQ_PayrollRunEmployees_Run_Employment' AND type=1)))
              OR (object_id=OBJECT_ID(N'payroll.RunLines') AND
              ((name=N'PK_PayrollRunLines' AND type=2) OR (name=N'UQ_PayrollRunLines_Employee_Number' AND type=1)));
            SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN(OBJECT_ID(N'payroll.RunLines'),OBJECT_ID(N'payroll.PaymentLines'))
              AND (is_disabled=1 OR is_not_trusted=1);
            SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN(N'FK_PayrollRunLines_RunEmployee',N'FK_PayrollPaymentLines_RunEmployee')
              AND is_disabled=0 AND is_not_trusted=0;
            """,connection);
        await using var reader=await indexes.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(4,reader.GetInt32(0));
        await reader.NextResultAsync();Assert.True(await reader.ReadAsync());Assert.Equal(0,reader.GetInt32(0));
        await reader.NextResultAsync();Assert.True(await reader.ReadAsync());Assert.Equal(2,reader.GetInt32(0));
    }

    private static async Task<string[]> ReadRowsAsync(SqlConnection connection,Guid runId,Guid batchId,Guid damageId)
    {
        await using var command=new SqlCommand("""
            SELECT (SELECT * FROM payroll.Runs WHERE PayrollRunId=@Run FOR JSON PATH),
              (SELECT * FROM payroll.RunEmploymentInputs WHERE PayrollRunId=@Run ORDER BY EmploymentId FOR JSON PATH),
              (SELECT * FROM payroll.RunEmployees WHERE PayrollRunId=@Run ORDER BY PayrollRunEmployeeId FOR JSON PATH),
              (SELECT line.* FROM payroll.RunLines line JOIN payroll.RunEmployees employee ON employee.PayrollRunEmployeeId=line.PayrollRunEmployeeId
                WHERE employee.PayrollRunId=@Run ORDER BY line.PayrollRunLineId FOR JSON PATH),
              (SELECT * FROM payroll.PaymentBatches WHERE PaymentBatchId=@Batch FOR JSON PATH),
              (SELECT * FROM payroll.PaymentLines WHERE PaymentBatchId=@Batch ORDER BY PaymentLineId FOR JSON PATH),
              (SELECT * FROM dbo.InventoryOperations WHERE InventoryOperationId=@Damage FOR JSON PATH);
            """,connection);
        command.Parameters.AddWithValue("@Run",runId);command.Parameters.AddWithValue("@Batch",batchId);
        command.Parameters.AddWithValue("@Damage",damageId);
        await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0,7).Select(reader.GetString).ToArray();
    }
}
