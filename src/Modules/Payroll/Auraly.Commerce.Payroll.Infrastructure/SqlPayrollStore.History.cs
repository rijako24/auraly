using System.Data;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore
{
    public async Task<PayrollHistoryPage<PayrollDeductionAgreementSummary>> PageAgreementsAsync(PayrollUserIdentity user,int page,int pageSize,Guid? employmentId,Guid? conceptId,bool? active,string? search,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("""
            DECLARE @Page TABLE(Id uniqueidentifier PRIMARY KEY);
            INSERT @Page SELECT a.DeductionAgreementId FROM payroll.DeductionAgreements a
            JOIN payroll.Employments e ON e.EmploymentId=a.EmploymentId AND e.TenantId=a.TenantId
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            WHERE a.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND (@Employment IS NULL OR a.EmploymentId=@Employment) AND (@Concept IS NULL OR a.ConceptId=@Concept)
              AND (@Active IS NULL OR a.IsActive=@Active)
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%' OR a.ReferenceNumber LIKE N'%'+@Search+N'%')
            ORDER BY a.IsActive DESC,a.Priority,p.DisplayName,a.DeductionAgreementId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            SELECT COUNT(*) FROM payroll.DeductionAgreements a
            JOIN payroll.Employments e ON e.EmploymentId=a.EmploymentId AND e.TenantId=a.TenantId
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            WHERE a.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND (@Employment IS NULL OR a.EmploymentId=@Employment) AND (@Concept IS NULL OR a.ConceptId=@Concept)
              AND (@Active IS NULL OR a.IsActive=@Active)
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%' OR a.ReferenceNumber LIKE N'%'+@Search+N'%');
            SELECT a.DeductionAgreementId,a.EmploymentId,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
                   a.ConceptId,c.Name,a.AuthorityOptionId,a.BeneficiaryPartyId,o.Label,a.ReferenceNumber,a.EvidenceUrl,
                   a.EffectiveFrom,a.EffectiveTo,a.AuthorizedTotal,a.InstallmentAmount,a.DeductedToDate,
                   a.Priority,a.MustProtectMinimumNetPay,a.IsActive,a.RowVersion
            FROM @Page selected JOIN payroll.DeductionAgreements a ON a.DeductionAgreementId=selected.Id
            JOIN payroll.Employments e ON e.EmploymentId=a.EmploymentId AND e.TenantId=a.TenantId
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            JOIN payroll.Concepts c ON c.ConceptId=a.ConceptId AND c.TenantId=a.TenantId
            JOIN payroll.CatalogOptions o ON o.OptionId=a.AuthorityOptionId
            WHERE a.TenantId=@TenantId AND e.BusinessId=@BusinessId
            ORDER BY a.IsActive DESC,a.Priority,p.DisplayName,a.DeductionAgreementId;

            """,connection);
        AddHistoryPageParameters(command,user,page,pageSize);
        command.Parameters.AddWithValue("@Employment",(object?)employmentId??DBNull.Value);command.Parameters.AddWithValue("@Concept",(object?)conceptId??DBNull.Value);
        command.Parameters.Add("@Active",SqlDbType.Bit).Value=(object?)active??DBNull.Value;command.Parameters.AddWithValue("@Search",(object?)search??DBNull.Value);
        return await ReadHistoryPageAsync(command,page,pageSize,reader=>new PayrollDeductionAgreementSummary(
            reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2).Trim(),reader.GetGuid(3),reader.GetString(4),reader.GetGuid(5),
            reader.IsDBNull(6)?null:reader.GetGuid(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),DateOnly.FromDateTime(reader.GetDateTime(10)),
            reader.IsDBNull(11)?null:DateOnly.FromDateTime(reader.GetDateTime(11)),reader.IsDBNull(12)?null:reader.GetDecimal(12),
            reader.IsDBNull(13)?null:reader.GetDecimal(13),reader.GetDecimal(14),reader.GetInt16(15),reader.GetBoolean(16),reader.GetBoolean(17),(byte[])reader[18]),ct);
    }

    public async Task<PayrollHistoryPage<PayrollPaymentBatchView>> PagePaymentsAsync(PayrollUserIdentity user,int page,int pageSize,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("""
            DECLARE @Page TABLE(Id uniqueidentifier PRIMARY KEY);
            INSERT @Page SELECT PaymentBatchId FROM payroll.PaymentBatches WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              ORDER BY PaymentDate DESC,CreatedAt DESC,PaymentBatchId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            SELECT COUNT(*) FROM payroll.PaymentBatches WHERE TenantId=@TenantId AND BusinessId=@BusinessId;
            SELECT b.PaymentBatchId,
                   (SELECT TOP(1) re.PayrollRunId FROM payroll.PaymentLines pl
                    JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
                    WHERE pl.PaymentBatchId=b.PaymentBatchId ORDER BY re.PayrollRunId),
                   b.PaymentDate,b.PaymentMethodOptionId,COALESCE(b.PaymentMethodName,o.Label),b.ReferenceNumber,b.Status,
                   COUNT(l.PaymentLineId),b.TotalAmount,b.RowVersion,b.PaymentMethodId,b.AccountId,posting.AccountingStatus,posting.IsPaid
            FROM @Page selected JOIN payroll.PaymentBatches b ON b.PaymentBatchId=selected.Id
            JOIN payroll.PaymentBatchPostingStatus posting ON posting.PaymentBatchId=b.PaymentBatchId AND posting.TenantId=b.TenantId AND posting.BusinessId=b.BusinessId
            JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
            LEFT JOIN payroll.PaymentLines l ON l.PaymentBatchId=b.PaymentBatchId
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
            GROUP BY b.PaymentBatchId,b.PaymentDate,b.PaymentMethodOptionId,o.Label,
                     b.ReferenceNumber,b.Status,b.TotalAmount,b.RowVersion,b.CreatedAt,b.PaymentMethodName,b.PaymentMethodId,b.AccountId,posting.AccountingStatus,posting.IsPaid
            ORDER BY b.PaymentDate DESC,b.CreatedAt DESC,b.PaymentBatchId;

            """,connection);
        AddHistoryPageParameters(command,user,page,pageSize);
        return await ReadHistoryPageAsync(command,page,pageSize,reader=>new PayrollPaymentBatchView(reader.GetGuid(0),reader.GetGuid(1),
            DateOnly.FromDateTime(reader.GetDateTime(2)),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetInt32(7),reader.GetDecimal(8),(byte[])reader[9])
            {PaymentMethodId=reader.IsDBNull(10)?null:reader.GetGuid(10),AccountId=reader.IsDBNull(11)?null:reader.GetGuid(11),AccountingStatus=NullableString(reader,12),IsPaid=reader.GetBoolean(13)},ct);
    }

    public async Task<PayrollHistoryPage<ElectronicPayrollPeriodView>> PageElectronicPeriodsAsync(PayrollUserIdentity user,int page,int pageSize,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("""
            SELECT COUNT(*) FROM payroll.ElectronicPeriods WHERE TenantId=@TenantId AND BusinessId=@BusinessId;
            WITH page AS(SELECT ElectronicPeriodId,Year,Month,Status,RowVersion FROM payroll.ElectronicPeriods
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId ORDER BY Year DESC,Month DESC,ElectronicPeriodId
              OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY)
            SELECT page.*, (SELECT COUNT(*) FROM payroll.ElectronicDocuments d WHERE d.ElectronicPeriodId=page.ElectronicPeriodId) DocumentCount
              FROM page ORDER BY Year DESC,Month DESC,ElectronicPeriodId;
            """,connection);
        AddHistoryPageParameters(command,user,page,pageSize);
        return await ReadHistoryPageAsync(command,page,pageSize,reader=>new ElectronicPayrollPeriodView(reader.GetGuid(0),reader.GetInt16(1),reader.GetByte(2),reader.GetString(3),[],(byte[])reader[4]){DocumentCount=reader.GetInt32(5)},ct);
    }

    public async Task<PayrollHistoryPage<ElectronicPayrollDocumentView>> PageElectronicDocumentsAsync(PayrollUserIdentity user,Guid periodId,int page,int pageSize,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("""
            SELECT COUNT(*) FROM payroll.ElectronicDocuments WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND ElectronicPeriodId=@Period;
            WITH page AS(SELECT ElectronicPayrollDocumentId,PartyId,DocumentKind,FiscalDocumentId,Status,SourceHash,SourceSnapshotJson,CreatedAt
              FROM payroll.ElectronicDocuments WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND ElectronicPeriodId=@Period
              ORDER BY CreatedAt DESC,ElectronicPayrollDocumentId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY)
            SELECT ElectronicPayrollDocumentId,PartyId,COALESCE(NULLIF(JSON_VALUE(SourceSnapshotJson,'$.employeeName'),N''),N'Sin nombre en el documento'),
              DocumentKind,FiscalDocumentId,Status,CONVERT(varchar(64),SourceHash,2) FROM page ORDER BY CreatedAt DESC,ElectronicPayrollDocumentId;
            """,connection);
        AddHistoryPageParameters(command,user,page,pageSize);command.Parameters.AddWithValue("@Period",periodId);
        return await ReadHistoryPageAsync(command,page,pageSize,reader=>new ElectronicPayrollDocumentView(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),
            reader.IsDBNull(4)?null:reader.GetGuid(4),reader.GetString(5),reader.GetString(6)),ct);
    }

    private static void AddHistoryPageParameters(SqlCommand command,PayrollUserIdentity user,int page,int pageSize)
    {
        command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@Offset",(page-1)*pageSize);command.Parameters.AddWithValue("@Size",pageSize);
    }

    private static async Task<PayrollHistoryPage<T>> ReadHistoryPageAsync<T>(SqlCommand command,int page,int pageSize,Func<SqlDataReader,T> read,CancellationToken ct)
    {
        await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var count=reader.GetInt32(0);
        await reader.NextResultAsync(ct);var rows=new List<T>();while(await reader.ReadAsync(ct))rows.Add(read(reader));
        return new(rows,page,pageSize,count,(int)Math.Ceiling(count/(double)pageSize));
    }
}
