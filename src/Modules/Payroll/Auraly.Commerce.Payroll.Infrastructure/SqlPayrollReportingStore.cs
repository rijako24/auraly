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
        string code, DateOnly from, DateOnly to, Guid? partyId, CancellationToken ct)
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

        await using var command = new SqlCommand(Sql(definition.Dataset), connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@PartyId", (object?)partyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@TaxWithholdingRole",
            PayrollSystemConceptRoleCodes.LaborWithholding);
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
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],
          re.WorkedDays [workedDays],re.Earnings [earnings],re.Deductions [deductions],
          re.NetPayable [netPayable],
          CASE WHEN EXISTS(SELECT 1 FROM payroll.PaymentLines pl JOIN payroll.PaymentBatches pb
            ON pb.PaymentBatchId=pl.PaymentBatchId WHERE pl.PayrollRunEmployeeId=re.PayrollRunEmployeeId
            AND pb.Status=N'Confirmed') THEN N'Pagado' ELSE N'Pendiente' END [paymentStatus],
          COALESCE((SELECT TOP(1) d.Status FROM payroll.ElectronicDocuments d
            JOIN payroll.ElectronicPeriods ep ON ep.ElectronicPeriodId=d.ElectronicPeriodId
            WHERE d.PartyId=re.PartyId AND ep.TenantId=r.TenantId AND ep.BusinessId=r.BusinessId
              AND DATEFROMPARTS(ep.Year,ep.Month,1)<=r.PeriodEnd
              AND EOMONTH(DATEFROMPARTS(ep.Year,ep.Month,1))>=r.PeriodStart
            ORDER BY d.CreatedAt DESC),N'Pending') [electronicStatus]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=r.TenantId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY r.PeriodEnd DESC,[employeeName];
        """;

    private const string ConceptSql = """
        SELECT l.PayrollRunLineId [id],re.PartyId [partyId],
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],
          l.ConceptCode [conceptCode],l.ConceptName [conceptName],l.NatureCode [nature],
          l.Quantity [quantity],l.BaseAmount [baseAmount],l.Rate [rate],l.Amount [amount]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
        JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=r.TenantId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        """;

    private const string ReceiptSql = """
        SELECT l.PayrollRunLineId [id],re.PartyId [partyId],
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],e.ContractNumber [contractNumber],
          r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],r.PaymentDate [paymentDate],
          re.WorkedDays [workedDays],l.ConceptCode [conceptCode],l.ConceptName [conceptName],
          l.NatureCode [nature],l.Quantity [quantity],l.Amount [amount],
          re.Earnings [earnings],re.Deductions [deductions],re.NetPayable [netPayable]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
        JOIN payroll.Employments e ON e.EmploymentId=re.EmploymentId AND e.TenantId=r.TenantId
        JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=r.TenantId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY r.PeriodEnd DESC,[employeeName],l.LineNumber;
        """;

    private const string ConceptOrderSql =
        " ORDER BY r.PeriodEnd DESC,[employeeName],l.LineNumber,l.PayrollRunLineId;";

    private const string LaborCostSql = """
        SELECT re.PayrollRunEmployeeId [id],re.PartyId [partyId],
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],r.PeriodStart [periodStart],r.PeriodEnd [periodEnd],
          re.Earnings [earnings],re.EmployerContributions [employerContributions],
          re.Provisions [provisions],re.Earnings+re.EmployerContributions+re.Provisions [totalLaborCost]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=r.TenantId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY r.PeriodEnd DESC,[employeeName];
        """;

    private const string PaymentsSql = """
        SELECT pl.PaymentLineId [id],re.PartyId [partyId],
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],b.PaymentDate [paymentDate],o.Label [paymentMethod],
          b.ReferenceNumber [paymentReference],pl.Amount [amount],b.Status [status]
        FROM payroll.PaymentBatches b JOIN payroll.PaymentLines pl ON pl.PaymentBatchId=b.PaymentBatchId
        JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
        JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=b.TenantId
        JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
        WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId AND b.Status=N'Confirmed'
          AND b.PaymentDate BETWEEN @From AND @To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        ORDER BY b.PaymentDate DESC,[employeeName];
        """;

    private const string ElectronicSql = """
        SELECT d.ElectronicPayrollDocumentId [id],d.PartyId [partyId],
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],CONCAT(ep.Year,N'-',RIGHT(CONCAT(N'0',ep.Month),2)) [period],
          d.DocumentKind [documentKind],d.Status [payrollStatus],fd.FiscalStatus [fiscalStatus],
          process.TrackId [trackId],d.CreatedAt [createdAt]
        FROM payroll.ElectronicDocuments d JOIN payroll.ElectronicPeriods ep
          ON ep.ElectronicPeriodId=d.ElectronicPeriodId
        JOIN dbo.Parties p ON p.PartyId=d.PartyId AND p.TenantId=d.TenantId
        LEFT JOIN dbo.FiscalDocuments fd ON fd.DocumentId=d.FiscalDocumentId
        LEFT JOIN dbo.FiscalDocumentProcesses process ON process.DocumentId=d.FiscalDocumentId
        WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId
          AND EOMONTH(DATEFROMPARTS(ep.Year,ep.Month,1))>=@From
          AND DATEFROMPARTS(ep.Year,ep.Month,1)<=@To AND (@PartyId IS NULL OR d.PartyId=@PartyId)
        ORDER BY ep.Year DESC,ep.Month DESC,[employeeName];
        """;

    private const string IncomeWithholdingSql = """
        SELECT re.PartyId [id],re.PartyId [partyId],
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona') [employeeName],
          p.Identification [identification],MIN(r.PeriodStart) [periodStart],MAX(r.PeriodEnd) [periodEnd],
          SUM(CASE WHEN l.NatureCode=N'Earning' THEN l.Amount ELSE 0 END) [employmentIncome],
          SUM(CASE WHEN role.Code=@TaxWithholdingRole THEN l.Amount ELSE 0 END) [withholding],
          SUM(CASE WHEN l.NatureCode=N'Deduction' THEN l.Amount ELSE 0 END) [totalDeductions]
        FROM payroll.Runs r JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
        JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
        JOIN payroll.Concepts c ON c.ConceptId=l.ConceptId
        LEFT JOIN payroll.CatalogOptions role ON role.OptionId=c.SystemRoleOptionId
        JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=r.TenantId
        WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId AND r.Status=N'Approved'
          AND r.PeriodEnd>=@From AND r.PeriodStart<=@To AND (@PartyId IS NULL OR re.PartyId=@PartyId)
        GROUP BY re.PartyId,p.DisplayName,p.LegalName,p.FirstName,p.LastName,p.Identification
        ORDER BY [employeeName];
        """;
}
