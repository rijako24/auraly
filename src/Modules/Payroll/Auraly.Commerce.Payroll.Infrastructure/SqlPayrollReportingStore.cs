using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed class SqlPayrollReportingStore(PayrollSqlConnectionFactory connections)
    : IPayrollReportingStore
{
    public async Task<IReadOnlyList<PayrollReportDefinitionView>> ListDefinitionsAsync(
        PayrollUserIdentity user, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT Code,Name,Description,DatasetCode,ColumnsJson,SortOrder
            FROM reporting.PayrollReportDefinitions
            WHERE IsActive=1 ORDER BY SortOrder,Code;
            """, connection);
        var values = new List<PayrollReportDefinitionView>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) values.Add(ReadDefinition(reader));
        return values;
    }

    public async Task<PayrollReportResult> RunAsync(PayrollUserIdentity user,
        string code, DateOnly from, DateOnly to, Guid? partyId, CancellationToken ct,Guid? employeeResultId=null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        PayrollReportDefinitionView definition;
        await using (var definitionCommand = new SqlCommand("""
            SELECT Code,Name,Description,DatasetCode,ColumnsJson,SortOrder
            FROM reporting.PayrollReportDefinitions
            WHERE Code=@Code AND IsActive=1;
            """, connection))
        {
            definitionCommand.Parameters.AddWithValue("@Code", code);
            await using var reader = await definitionCommand.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new PayrollNotFoundException("El reporte de nómina no existe o está inactivo.");
            definition = ReadDefinition(reader);
        }

        if(employeeResultId.HasValue&&definition.Dataset!=PayrollReportDataset.PayrollReceipt)
            throw new PayrollValidationException("La consulta individual corresponde al desprendible laboral.");
        await using var command = new SqlCommand(Sql(definition.Dataset), connection);
        command.Parameters.AddWithValue("@EmployeeResultId",(object?)employeeResultId??DBNull.Value);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@PartyId", (object?)partyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@TaxWithholdingCategory",PayrollAccountingCategories.WithholdingPayable);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var data = await command.ExecuteReaderAsync(ct);
        while (await data.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < data.FieldCount; index++)
                row[data.GetName(index)] = Value(data, index);
            rows.Add(row);
        }
        return new(definition, from, to, rows);
    }

    private static PayrollReportDefinitionView ReadDefinition(SqlDataReader reader)
    {
        if (!Enum.TryParse<PayrollReportDataset>(reader.GetString(3), false, out var dataset))
            throw new PayrollConflictException(
                $"El dataset '{reader.GetString(3)}' del reporte no está soportado.");
        var columns = JsonSerializer.Deserialize<PayrollReportColumnView[]>(reader.GetString(4),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        if (columns.Length == 0 || columns.Any(column =>
            string.IsNullOrWhiteSpace(column.Key) || string.IsNullOrWhiteSpace(column.Label)))
            throw new PayrollConflictException("La definición de columnas del reporte no es válida.");
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), dataset,
            columns, reader.GetInt32(5));
    }

    private static object? Value(SqlDataReader reader, int index)
    {
        if (reader.IsDBNull(index)) return null;
        return reader.GetValue(index) switch
        {
            DateTime value => value.ToString("yyyy-MM-dd"),
            DateTimeOffset value => value.ToString("O"),
            Guid value => value.ToString("D"),
            var value => value
        };
    }

    private static string Sql(PayrollReportDataset dataset) => dataset switch
    {
        PayrollReportDataset.PayrollSummary => SummarySql,
        PayrollReportDataset.PayrollReceipt => ReceiptSql,
        PayrollReportDataset.ConceptDetail => ConceptSql + ConceptOrderSql,
        PayrollReportDataset.Deductions => ConceptSql + " AND l.NatureCode=N'Deduction'" + ConceptOrderSql,
        PayrollReportDataset.EmployerContributions => ConceptSql +
            " AND l.NatureCode=N'EmployerContribution' AND l.IsEmployerCost=1" + ConceptOrderSql,
        PayrollReportDataset.Provisions => ConceptSql + " AND l.NatureCode=N'Provision'" + ConceptOrderSql,
        PayrollReportDataset.LaborCost => LaborCostSql,
        PayrollReportDataset.Payments => PaymentsSql,
        PayrollReportDataset.ElectronicStatus => ElectronicSql,
        PayrollReportDataset.IncomeAndWithholding => IncomeWithholdingSql,
        _ => throw new PayrollConflictException("El dataset de reporte no está soportado.")
    };

    private const string SummarySql = """
        SELECT re.PayrollRunEmployeeId [id],re.PartyId [partyId],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.EmployeeName') [employeeName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Identification') [identification],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],
          re.WorkedDays [workedDays],re.Earnings [earnings],re.Deductions [deductions],
          re.NetPayable [netPayable],
          CASE WHEN EXISTS(SELECT 1 FROM payroll.PaymentLines pl JOIN payroll.PaymentBatchPostingStatus pb
            ON pb.PaymentBatchId=pl.PaymentBatchId AND pb.TenantId=r.TenantId AND pb.BusinessId=r.BusinessId WHERE pl.PayrollRunEmployeeId=re.PayrollRunEmployeeId
            AND pb.IsPaid=1) THEN N'Pagado' ELSE N'Pendiente' END [paymentStatus],
          COALESCE((SELECT TOP(1) d.Status FROM payroll.ElectronicDocuments d
            JOIN payroll.ElectronicPeriods ep ON ep.ElectronicPeriodId=d.ElectronicPeriodId
            WHERE d.PartyId=re.PartyId AND ep.TenantId=r.TenantId AND ep.BusinessId=r.BusinessId
              AND DATEFROMPARTS(ep.Year,ep.Month,1)<=r.PeriodEnd
              AND EOMONTH(DATEFROMPARTS(ep.Year,ep.Month,1))>=r.PeriodStart
            ORDER BY d.CreatedAt DESC),N'Pending') [electronicStatus]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY r.PeriodEnd DESC,[employeeName];
        """;

    private const string ConceptSql = """
        SELECT l.PayrollRunLineId [id],re.PartyId [partyId],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.EmployeeName') [employeeName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Identification') [identification],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],
          l.ConceptCode [conceptCode],l.ConceptName [conceptName],l.NatureCode [nature],
          l.Quantity [quantity],l.BaseAmount [baseAmount],l.Rate [rate],l.Amount [amount]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        """;

    private const string ReceiptSql = """
        SELECT l.PayrollRunLineId [id],re.PartyId [partyId],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.EmployeeName') [employeeName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Identification') [identification],JSON_VALUE(re.EmployeeSnapshotJson,'$.ContractNumber') [contractNumber],
          r.Status [runStatus],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],r.PaymentDate [paymentDate],CASE WHEN posting.IsPaid=1 THEN payment.PaymentDate END [paidDate],
          TRY_CONVERT(decimal(19,4),JSON_VALUE(re.EmployeeSnapshotJson,'$.MonthlySalary')) [monthlySalary],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Configuration.ProfileName') [profileName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Configuration.CostCenterName') [costCenterName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Configuration.HealthName') [healthName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Configuration.PensionName') [pensionName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Configuration.Values.Details.JobTitle') [jobTitle],
          re.PayrollRunEmployeeId [payrollRunEmployeeId],re.EmploymentId [employmentId],r.PayrollRunId [payrollRunId],
          re.WorkedDays [workedDays],l.ConceptCode [conceptCode],l.ConceptName [conceptName],
          l.NatureCode [nature],l.Quantity [quantity],
          CASE WHEN l.BaseAmount IS NOT NULL THEN l.Rate END [percentageRate],l.Amount [amount],
          l.CostCenterName [lineCostCenterName],l.Notes [notes],l.EvidenceUrl [evidenceUrl],
          re.Earnings [earnings],re.Deductions [deductions],re.NetPayable [netPayable]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
        LEFT JOIN payroll.PaymentLines paid ON paid.PayrollRunEmployeeId=re.PayrollRunEmployeeId
        LEFT JOIN payroll.PaymentBatches payment ON payment.PaymentBatchId=paid.PaymentBatchId AND payment.TenantId=r.TenantId AND payment.BusinessId=r.BusinessId
        LEFT JOIN payroll.PaymentBatchPostingStatus posting ON posting.PaymentBatchId=payment.PaymentBatchId AND posting.TenantId=payment.TenantId AND posting.BusinessId=payment.BusinessId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND (r.Status=N'Approved' OR @EmployeeResultId IS NOT NULL AND r.Status=N'Calculated')
          AND (@EmployeeResultId IS NULL OR re.PayrollRunEmployeeId=@EmployeeResultId)
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY r.PeriodEnd DESC,[employeeName],l.LineNumber;
        """;

    private const string ConceptOrderSql =
        " ORDER BY r.PeriodEnd DESC,[employeeName],l.LineNumber,l.PayrollRunLineId;";

    private const string LaborCostSql = """
        SELECT re.PayrollRunEmployeeId [id],re.PartyId [partyId],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.EmployeeName') [employeeName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Identification') [identification],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],
          re.Earnings [earnings],re.EmployerContributions [employerContributions],
          re.Provisions [provisions],re.Earnings+re.EmployerContributions+re.Provisions [totalLaborCost]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY r.PeriodEnd DESC,[employeeName];
        """;

    private const string PaymentsSql = """
        SELECT pl.PaymentLineId [id],re.PartyId [partyId],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.EmployeeName') [employeeName],
          JSON_VALUE(re.EmployeeSnapshotJson,'$.Identification') [identification],b.PaymentDate [paymentDate],COALESCE(b.PaymentMethodName,o.Label) [paymentMethod],
          b.ReferenceNumber [paymentReference],pl.Amount [amount],posting.AccountingStatus [status]
        FROM payroll.PaymentBatches b JOIN payroll.PaymentBatchPostingStatus posting
          ON posting.PaymentBatchId=b.PaymentBatchId AND posting.TenantId=b.TenantId AND posting.BusinessId=b.BusinessId AND posting.IsPaid=1
        JOIN payroll.PaymentLines pl ON pl.PaymentBatchId=b.PaymentBatchId
        JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
        JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
        WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
          AND b.PaymentDate BETWEEN @From AND @To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY b.PaymentDate DESC,[employeeName];
        """;

    private const string ElectronicSql = """
        SELECT d.ElectronicPayrollDocumentId [id],d.PartyId [partyId],
          JSON_VALUE(d.SourceSnapshotJson,'$.employeeName') [employeeName],
          JSON_VALUE(d.SourceSnapshotJson,'$.employeeIdentification') [identification],CONCAT(ep.Year,N'-',RIGHT(CONCAT(N'0',ep.Month),2)) [period],
          d.DocumentKind [documentKind],d.Status [payrollStatus],fd.FiscalStatus [fiscalStatus],
          process.TrackId [trackId],d.CreatedAt [createdAt]
        FROM payroll.ElectronicDocuments d JOIN payroll.ElectronicPeriods ep
          ON ep.ElectronicPeriodId=d.ElectronicPeriodId
        LEFT JOIN dbo.FiscalDocuments fd ON fd.DocumentId=d.FiscalDocumentId
        LEFT JOIN dbo.FiscalDocumentProcesses process ON process.DocumentId=d.FiscalDocumentId
        WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId
          AND EOMONTH(DATEFROMPARTS(ep.Year,ep.Month,1))>=@From
          AND DATEFROMPARTS(ep.Year,ep.Month,1)<=@To AND (@PartyId IS NULL OR d.PartyId=@PartyId)
        ORDER BY ep.Year DESC,ep.Month DESC,[employeeName];
        """;

    private const string IncomeWithholdingSql = """
        WITH PeriodLines AS(
          SELECT re.PartyId,r.PeriodStart,r.PeriodEnd,l.NatureCode,l.Amount,l.AccountingCategoryCode,
            JSON_VALUE(re.EmployeeSnapshotJson,'$.EmployeeName') EmployeeName,
            JSON_VALUE(re.EmployeeSnapshotJson,'$.Identification') Identification,
            DENSE_RANK() OVER(PARTITION BY re.PartyId ORDER BY r.PeriodEnd DESC,r.PeriodStart DESC,r.PayrollRunId DESC) SourceOrder
          FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
          JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
          WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
            AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        )
        SELECT PartyId [id],PartyId [partyId],MAX(CASE WHEN SourceOrder=1 THEN EmployeeName END) [employeeName],
          MAX(CASE WHEN SourceOrder=1 THEN Identification END) [identification],MIN(PeriodStart) [periodStart],MAX(PeriodEnd) [periodEnd],
          SUM(CASE WHEN NatureCode=N'Earning' THEN Amount ELSE 0 END) [employmentIncome],
          SUM(CASE WHEN AccountingCategoryCode=@TaxWithholdingCategory THEN Amount ELSE 0 END) [withholding],
          SUM(CASE WHEN NatureCode=N'Deduction' THEN Amount ELSE 0 END) [totalDeductions]
        FROM PeriodLines GROUP BY PartyId ORDER BY [employeeName];
        """;
}
