using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Domain.Time;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore(
    PayrollSqlConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    ILogger<SqlPayrollStore> logger) : IPayrollStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string EmploymentConfigurationProjection = """
        ,JSON_QUERY((SELECT e.AccountingProfileId AS [Values.AccountingProfileId],e.HealthPartyId AS [Values.HealthPartyId],
          e.PensionPartyId AS [Values.PensionPartyId],e.SeverancePartyId AS [Values.SeverancePartyId],
          e.RiskPartyId AS [Values.RiskPartyId],e.CompensationPartyId AS [Values.CompensationPartyId],
          e.CostCenterId AS [Values.CostCenterId],profile.Name AS ProfileName,center.Name AS CostCenterName,
          health.DisplayName AS HealthName,pension.DisplayName AS PensionName,severance.DisplayName AS SeveranceName,
          riskEntity.DisplayName AS RiskName,compensation.DisplayName AS CompensationName
          FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER))
        """;
    private const string EmploymentConfigurationJoins = """
        LEFT JOIN payroll.AccountingProfiles profile ON profile.ProfileId=e.AccountingProfileId AND profile.TenantId=e.TenantId
        LEFT JOIN dbo.AccountingCostCenters center ON center.CostCenterId=e.CostCenterId AND center.TenantId=e.TenantId
        LEFT JOIN dbo.Parties health ON health.PartyId=e.HealthPartyId AND health.TenantId=e.TenantId
        LEFT JOIN dbo.Parties pension ON pension.PartyId=e.PensionPartyId AND pension.TenantId=e.TenantId
        LEFT JOIN dbo.Parties severance ON severance.PartyId=e.SeverancePartyId AND severance.TenantId=e.TenantId
        LEFT JOIN dbo.Parties riskEntity ON riskEntity.PartyId=e.RiskPartyId AND riskEntity.TenantId=e.TenantId
        LEFT JOIN dbo.Parties compensation ON compensation.PartyId=e.CompensationPartyId AND compensation.TenantId=e.TenantId
        """;
    public async Task<PayrollEmploymentPage> PageEmploymentsAsync(
        PayrollUserIdentity user, int page, int pageSize, string? search,
        bool? isActive, Guid? employmentId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"""
            SELECT COUNT_BIG(1)
            FROM payroll.Employments e
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND (@IsActive IS NULL OR e.IsActive=@IsActive)
              AND (@EmploymentId IS NULL OR e.EmploymentId=@EmploymentId)
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%'
                   OR p.Identification LIKE N'%'+@Search+N'%'
                   OR e.ContractNumber LIKE N'%'+@Search+N'%');

            WITH EmploymentPage AS (
                SELECT e.* FROM payroll.Employments e
                JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
                WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
                  AND (@IsActive IS NULL OR e.IsActive=@IsActive)
                  AND (@EmploymentId IS NULL OR e.EmploymentId=@EmploymentId)
                  AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%'
                       OR p.Identification LIKE N'%'+@Search+N'%'
                       OR e.ContractNumber LIKE N'%'+@Search+N'%')
                ORDER BY e.IsActive DESC,COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),e.EmploymentId
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY)
            SELECT e.EmploymentId,e.PartyId,e.BusinessId,e.ContractNumber,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
                   e.MonthlySalary,e.IsActive,e.EmployeeId,e.ContractTypeOptionId,
                   e.SalaryTypeOptionId,e.PayFrequencyOptionId,e.RiskClassOptionId,
                   e.WorkerTypeOptionId,e.WorkerSubtypeOptionId,e.PaymentMethodOptionId,
                   e.StartDate,e.EndDate,e.IntegralSalaryPercentage,e.BankAccountReference,
                   e.BankOptionId,e.BankAccountTypeOptionId,e.BankAccountNumber,e.RowVersion,e.AppliesTransportAllowance
                   {EmploymentConfigurationProjection}
            FROM EmploymentPage e
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            {EmploymentConfigurationJoins}
            ORDER BY e.IsActive DESC,5,e.EmploymentId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsActive", (object?)isActive ?? DBNull.Value);
        command.Parameters.AddWithValue("@EmploymentId", (object?)employmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var total = checked((int)reader.GetInt64(0));
        await reader.NextResultAsync(ct);
        var items = new List<PayrollEmploymentOption>();
        while (await reader.ReadAsync(ct)) items.Add(ReadEmployment(reader));
        return new(items, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<PayrollWorkspaceOptions> GetOptionsAsync(PayrollUserIdentity user, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"""
            SELECT OptionId,CatalogCode,Code,Label,Description,MetadataCode,DianCode,IsActive,SortOrder
            FROM payroll.CatalogOptions WHERE IsActive=1 ORDER BY CatalogCode,SortOrder,Code;

            SELECT c.ConceptId,c.Code,c.Name,n.Code,m.Code,t.Code,d.Code,a.Code,s.Code,
                   c.IsSalaryBase,c.IsSocialSecurityBase,c.IsBenefitsBase,c.IsTaxWithholdingBase,
                   c.RequiresDeductionAgreement,c.EffectiveFrom,c.EffectiveTo,c.IsActive,c.RowVersion
            FROM payroll.Concepts c
            JOIN payroll.CatalogOptions n ON n.OptionId=c.NatureOptionId
            JOIN payroll.CatalogOptions m ON m.OptionId=c.CalculationMethodOptionId
            JOIN payroll.CatalogOptions t ON t.OptionId=c.TreatmentOptionId
            LEFT JOIN payroll.CatalogOptions d ON d.OptionId=c.DianConceptOptionId
            JOIN payroll.CatalogOptions a ON a.OptionId=c.AccountingCategoryOptionId
            LEFT JOIN payroll.CatalogOptions s ON s.OptionId=c.SystemRoleOptionId
            WHERE c.TenantId=@TenantId ORDER BY c.IsActive DESC,c.Name,c.Code;

            SELECT e.EmploymentId,e.PartyId,e.BusinessId,e.ContractNumber,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
                   e.MonthlySalary,e.IsActive,e.EmployeeId,e.ContractTypeOptionId,
                   e.SalaryTypeOptionId,e.PayFrequencyOptionId,e.RiskClassOptionId,
                   e.WorkerTypeOptionId,e.WorkerSubtypeOptionId,e.PaymentMethodOptionId,
                   e.StartDate,e.EndDate,e.IntegralSalaryPercentage,e.BankAccountReference,
                   e.BankOptionId,e.BankAccountTypeOptionId,e.BankAccountNumber,e.RowVersion,e.AppliesTransportAllowance
                   {EmploymentConfigurationProjection}
            FROM payroll.Employments e JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            {EmploymentConfigurationJoins}
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId AND 1=0 ORDER BY e.IsActive DESC,5;

            SELECT p.PartyId,e.EmployeeId,p.Identification,
                   COALESCE(p.DisplayName,CONCAT(p.FirstName,N' ',p.LastName))
            FROM dbo.Parties p
            JOIN dbo.Employees e ON e.PartyId=p.PartyId AND e.TenantId=@TenantId AND e.IsActive=1
            JOIN payroll.CatalogOptions idtype
              ON idtype.CatalogCode=N'payroll-identification-type'
             AND idtype.Code=p.IdentificationTypeCode AND idtype.IsActive=1
             AND NULLIF(idtype.DianCode,N'') IS NOT NULL
            WHERE p.TenantId=@TenantId AND p.PartyType=N'NaturalPerson' AND p.IsActive=1
              AND 1=0
              AND NULLIF(p.Identification,N'') IS NOT NULL
              AND NULLIF(p.FirstName,N'') IS NOT NULL
              AND NULLIF(p.LastName,N'') IS NOT NULL
            ORDER BY 4,p.Identification;

            SELECT r.RuleSetId,r.CountryCode,r.Code,r.Name,r.EffectiveFrom,r.EffectiveTo,
                   r.SourceReference,r.Status,r.RowVersion,p.Code,p.NumericValue,p.UnitCode,p.Description
            FROM payroll.RuleSets r LEFT JOIN payroll.RuleParameters p ON p.RuleSetId=r.RuleSetId
            WHERE (r.TenantId=@TenantId OR r.TenantId IS NULL)
            ORDER BY r.EffectiveFrom DESC,r.Code,p.Code;

            SELECT IsEmployerExemptFromHealthSenaIcbf,ElectronicPayrollEnabled,RowVersion
            FROM payroll.Settings WHERE TenantId=@TenantId;

            SELECT BusinessId,FiscalIssuerConfigurationId,SoftwareIdentificationCode,
                   SoftwarePinSecretReference,TestSetId,Prefix,NextConsecutive,
                   QrValidationUrl,IsActive,RowVersion
            FROM payroll.ElectronicConfigurations
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId;

            SELECT FiscalIssuerConfigurationId,Version,LegalName,SoftwareIdentificationCode,
                   SoftwarePinSecretReference,Environment,TestSetId,IsActive
            FROM dbo.FiscalIssuerConfigurations
            WHERE BusinessId=@BusinessId
            ORDER BY IsActive DESC,Version DESC;

            SELECT a.DeductionAgreementId,a.EmploymentId,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
                   a.ConceptId,c.Name,a.AuthorityOptionId,a.BeneficiaryPartyId,o.Label,a.ReferenceNumber,a.EvidenceUrl,
                   a.EffectiveFrom,a.EffectiveTo,a.AuthorizedTotal,a.InstallmentAmount,a.DeductedToDate,
                   a.Priority,a.MustProtectMinimumNetPay,a.IsActive,a.RowVersion
            FROM payroll.DeductionAgreements a
            JOIN payroll.Employments e ON e.EmploymentId=a.EmploymentId AND e.TenantId=a.TenantId
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            JOIN payroll.Concepts c ON c.ConceptId=a.ConceptId AND c.TenantId=a.TenantId
            JOIN payroll.CatalogOptions o ON o.OptionId=a.AuthorityOptionId
            WHERE a.TenantId=@TenantId AND e.BusinessId=@BusinessId
            ORDER BY a.IsActive DESC,a.Priority,p.DisplayName;

            SELECT b.PaymentBatchId,
                   (SELECT TOP(1) re.PayrollRunId FROM payroll.PaymentLines pl
                    JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
                    WHERE pl.PaymentBatchId=b.PaymentBatchId ORDER BY re.PayrollRunId),
                   b.PaymentDate,b.PaymentMethodOptionId,COALESCE(b.PaymentMethodName,o.Label),b.ReferenceNumber,b.Status,
                   COUNT(l.PaymentLineId),b.TotalAmount,b.RowVersion,b.PaymentMethodId,b.AccountId,posting.AccountingStatus,posting.IsPaid
            FROM payroll.PaymentBatches b
            JOIN payroll.PaymentBatchPostingStatus posting ON posting.PaymentBatchId=b.PaymentBatchId AND posting.TenantId=b.TenantId AND posting.BusinessId=b.BusinessId
            JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
            LEFT JOIN payroll.PaymentLines l ON l.PaymentBatchId=b.PaymentBatchId
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
            GROUP BY b.PaymentBatchId,b.PaymentDate,b.PaymentMethodOptionId,o.Label,
                     b.ReferenceNumber,b.Status,b.TotalAmount,b.RowVersion,b.CreatedAt,b.PaymentMethodName,b.PaymentMethodId,b.AccountId,posting.AccountingStatus,posting.IsPaid
            ORDER BY b.PaymentDate DESC,b.CreatedAt DESC;

            SELECT ep.ElectronicPeriodId,ep.Year,ep.Month,ep.Status,ep.RowVersion
            FROM payroll.ElectronicPeriods ep
            WHERE ep.TenantId=@TenantId AND ep.BusinessId=@BusinessId
            ORDER BY ep.Year DESC,ep.Month DESC;

            SELECT d.ElectronicPeriodId,d.ElectronicPayrollDocumentId,d.PartyId,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona'),
                   d.DocumentKind,d.FiscalDocumentId,d.Status,CONVERT(varchar(64),d.SourceHash,2)
            FROM payroll.ElectronicDocuments d
            JOIN dbo.Parties p ON p.PartyId=d.PartyId AND p.TenantId=d.TenantId
            WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId
            ORDER BY d.CreatedAt DESC;

            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var catalogs = new Dictionary<string, List<PayrollCatalogOption>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            var option = new PayrollCatalogOption(reader.GetGuid(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), NullableString(reader, 4),
                NullableString(reader, 5), NullableString(reader, 6),
                reader.GetBoolean(7), reader.GetInt32(8));
            if (!catalogs.TryGetValue(option.CatalogCode, out var values))
                catalogs.Add(option.CatalogCode, values = []);
            values.Add(option);
        }

        await reader.NextResultAsync(ct);
        var concepts = new List<PayrollConceptView>();
        while (await reader.ReadAsync(ct)) concepts.Add(ReadConcept(reader));

        await reader.NextResultAsync(ct);
        var employments = new List<PayrollEmploymentOption>();
        while (await reader.ReadAsync(ct)) employments.Add(ReadEmployment(reader));

        await reader.NextResultAsync(ct);
        var parties = new List<PayrollPartyOption>();
        while (await reader.ReadAsync(ct))
            parties.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3).Trim()));

        await reader.NextResultAsync(ct);
        var rules = new List<PayrollRuleSetView>();
        Guid? currentId = null;
        string country = "", code = "", name = "", source = "", status = "";
        DateOnly effectiveFrom = default; DateOnly? effectiveTo = null; byte[] version = [];
        var parameters = new List<PayrollRuleParameterView>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (currentId is not null && currentId != id)
            {
                rules.Add(new(currentId.Value, country, code, name, effectiveFrom, effectiveTo,
                    source, status, parameters.ToArray(), version));
                parameters.Clear();
            }
            currentId = id; country = reader.GetString(1); code = reader.GetString(2);
            name = reader.GetString(3); effectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(4));
            effectiveTo = reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5));
            source = reader.GetString(6); status = reader.GetString(7); version = (byte[])reader[8];
            if (!reader.IsDBNull(9))
                parameters.Add(new(reader.GetString(9), reader.GetDecimal(10), reader.GetString(11), NullableString(reader, 12)));
        }
        if (currentId is not null)
            rules.Add(new(currentId.Value, country, code, name, effectiveFrom, effectiveTo,
                source, status, parameters.ToArray(), version));

        await reader.NextResultAsync(ct);
        PayrollSettingsView? settings = null;
        if (await reader.ReadAsync(ct))
            settings = new(reader.GetBoolean(0), reader.GetBoolean(1), (byte[])reader[2]);

        await reader.NextResultAsync(ct);
        ElectronicPayrollConfigurationView? electronicConfiguration = null;
        if (await reader.ReadAsync(ct))
            electronicConfiguration = new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5), reader.GetInt64(6), reader.GetString(7),
                reader.GetBoolean(8), (byte[])reader[9]);

        await reader.NextResultAsync(ct);
        var fiscalIssuers = new List<FiscalIssuerOption>();
        while (await reader.ReadAsync(ct))
            fiscalIssuers.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetByte(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetBoolean(7)));

        await reader.NextResultAsync(ct);
        var agreements = new List<PayrollDeductionAgreementSummary>();
        while (await reader.ReadAsync(ct))
            agreements.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2).Trim(),
                reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), DateOnly.FromDateTime(reader.GetDateTime(10)),
                reader.IsDBNull(11) ? null : DateOnly.FromDateTime(reader.GetDateTime(11)),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13), reader.GetDecimal(14),
                reader.GetInt16(15), reader.GetBoolean(16), reader.GetBoolean(17), (byte[])reader[18]));

        await reader.NextResultAsync(ct);
        var paymentBatches = new List<PayrollPaymentBatchView>();
        while (await reader.ReadAsync(ct))
            paymentBatches.Add(new(reader.GetGuid(0), reader.GetGuid(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetGuid(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetDecimal(8),
                (byte[])reader[9]) {PaymentMethodId=reader.IsDBNull(10)?null:reader.GetGuid(10),AccountId=reader.IsDBNull(11)?null:reader.GetGuid(11),AccountingStatus=NullableString(reader,12),IsPaid=reader.GetBoolean(13)});

        await reader.NextResultAsync(ct);
        var periodHeaders = new List<(Guid Id, short Year, byte Month, string Status, byte[] Version)>();
        while (await reader.ReadAsync(ct)) periodHeaders.Add((reader.GetGuid(0), reader.GetInt16(1),
            reader.GetByte(2), reader.GetString(3), (byte[])reader[4]));
        await reader.NextResultAsync(ct);
        var periodDocuments = periodHeaders.ToDictionary(x => x.Id,
            _ => new List<ElectronicPayrollDocumentView>());
        while (await reader.ReadAsync(ct))
            if (periodDocuments.TryGetValue(reader.GetGuid(0), out var documents))
                documents.Add(new(reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3).Trim(),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.GetString(6), reader.GetString(7)));
        var electronicPeriods = periodHeaders.Select(x => new ElectronicPayrollPeriodView(
            x.Id, x.Year, x.Month, x.Status, periodDocuments[x.Id], x.Version)).ToArray();

        return new(catalogs.ToDictionary(x => x.Key,
            x => (IReadOnlyList<PayrollCatalogOption>)x.Value, StringComparer.Ordinal),
            concepts, employments, parties, rules, settings,
            electronicConfiguration, fiscalIssuers, agreements, [],
            paymentBatches, electronicPeriods);
    }

    public async Task<ElectronicPayrollConfigurationView> SaveElectronicConfigurationAsync(
        PayrollUserIdentity user, SaveElectronicPayrollConfigurationRequest request,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            DECLARE @Environment tinyint=(SELECT Environment
              FROM dbo.FiscalIssuerConfigurations
              WHERE FiscalIssuerConfigurationId=@IssuerId AND BusinessId=@BusinessId AND IsActive=1);
            IF @Environment IS NULL
              THROW 51774,N'La configuración fiscal no pertenece a la empresa.',1;
            IF (@Environment=2 AND @TestSetId IS NULL) OR
               (@Environment=1 AND @TestSetId IS NOT NULL)
              THROW 51776,N'El TestSet ID debe existir solo para un emisor en habilitación.',1;
            IF EXISTS(SELECT 1 FROM payroll.ElectronicConfigurations
                      WHERE BusinessId=@BusinessId)
            BEGIN
              UPDATE payroll.ElectronicConfigurations
              SET FiscalIssuerConfigurationId=@IssuerId,
                  SoftwareIdentificationCode=@SoftwareId,
                  SoftwarePinSecretReference=@PinReference,TestSetId=@TestSetId,Prefix=@Prefix,
                  NextConsecutive=@NextConsecutive,QrValidationUrl=@QrUrl,
                  IsActive=@IsActive,UpdatedBy=@UserId,UpdatedAt=@Now
              WHERE BusinessId=@BusinessId AND TenantId=@TenantId
                AND (@RowVersion IS NULL OR RowVersion=@RowVersion);
              IF @@ROWCOUNT<>1 THROW 51775,N'La configuración cambió; recarga antes de guardar.',1;
            END
            ELSE
              INSERT payroll.ElectronicConfigurations(
                BusinessId,TenantId,FiscalIssuerConfigurationId,SoftwareIdentificationCode,
                SoftwarePinSecretReference,TestSetId,Prefix,NextConsecutive,
                QrValidationUrl,IsActive,UpdatedBy,UpdatedAt)
              VALUES(@BusinessId,@TenantId,@IssuerId,@SoftwareId,@PinReference,@TestSetId,
                @Prefix,@NextConsecutive,
                @QrUrl,@IsActive,@UserId,@Now);
            SELECT BusinessId,FiscalIssuerConfigurationId,SoftwareIdentificationCode,
                   SoftwarePinSecretReference,TestSetId,Prefix,NextConsecutive,
                   QrValidationUrl,IsActive,RowVersion
            FROM payroll.ElectronicConfigurations WHERE BusinessId=@BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@IssuerId", request.FiscalIssuerConfigurationId);
        command.Parameters.AddWithValue("@SoftwareId", request.SoftwareIdentificationCode);
        command.Parameters.AddWithValue("@PinReference", request.SoftwarePinSecretReference);
        command.Parameters.AddWithValue("@TestSetId", (object?)request.TestSetId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Prefix", request.Prefix);
        command.Parameters.AddWithValue("@NextConsecutive", request.NextConsecutive);
        command.Parameters.AddWithValue("@QrUrl", request.QrValidationUrl);
        command.Parameters.AddWithValue("@IsActive", request.IsActive);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value =
            (object?)request.RowVersion ?? DBNull.Value;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), reader.GetInt64(6),
                reader.GetString(7), reader.GetBoolean(8), (byte[])reader[9]);
        }
        catch (SqlException error) when (error.Number is 51774 or 51775 or 51776)
        {
            throw new PayrollConflictException(error.Message);
        }
    }

    public async Task<PayrollEmploymentView> SaveEmploymentAsync(PayrollUserIdentity user,
        SavePayrollEmploymentRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                    THROW 51700,N'La empresa está fuera del tenant.',1;
                IF EXISTS(SELECT 1 FROM payroll.Employments WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId<>@BusinessId)
                    THROW 51700,N'El contrato está fuera de la sede seleccionada.',1;
                IF @Active=0 AND EXISTS(
                    SELECT 1 FROM payroll.Employments
                    WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId)
                BEGIN
                    UPDATE payroll.Employments SET IsActive=0,EndDate=@EndDate,
                      AppliesTransportAllowance=COALESCE(@ApplyTransport,AppliesTransportAllowance),
                      UpdatedBy=@UserId,UpdatedAt=@Now
                    WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                      AND RowVersion=@RowVersion;
                    IF @@ROWCOUNT<>1 THROW 51705,N'La relación laboral cambió; recargue antes de guardar.',1;
                END
                ELSE
                BEGIN
                  DECLARE @PayrollLimit int=(
                    SELECT PayrollEmployeeLimit FROM billing.TenantSubscriptions WITH(UPDLOCK,HOLDLOCK)
                    WHERE TenantId=@TenantId AND Status IN(N'Active',N'PastDue'));
                  IF @Active=1
                     AND NOT EXISTS(SELECT 1 FROM payroll.Employments WHERE EmploymentId=@Id AND TenantId=@TenantId AND IsActive=1)
                     AND @PayrollLimit IS NOT NULL
                     AND (SELECT COUNT(*) FROM payroll.Employments WITH(UPDLOCK,HOLDLOCK)
                          WHERE TenantId=@TenantId AND IsActive=1)>=@PayrollLimit
                    THROW 51706,N'Se alcanzó el número de empleados de nómina contratado. Amplía la suscripción antes de activar otro empleado.',1;
                  IF NOT EXISTS(
                    SELECT 1 FROM dbo.Parties p
                    JOIN payroll.CatalogOptions idtype
                      ON idtype.CatalogCode=N'payroll-identification-type'
                     AND idtype.Code=p.IdentificationTypeCode AND idtype.IsActive=1
                     AND NULLIF(idtype.DianCode,N'') IS NOT NULL
                    WHERE p.PartyId=@PartyId AND p.TenantId=@TenantId
                      AND p.PartyType=N'NaturalPerson' AND p.IsActive=1
                      AND NULLIF(p.Identification,N'') IS NOT NULL
                      AND NULLIF(p.FirstName,N'') IS NOT NULL
                      AND NULLIF(p.LastName,N'') IS NOT NULL)
                    THROW 51701,N'El trabajador debe ser una persona natural activa con identificación y nombres completos.',1;
                DECLARE @ResolvedEmployeeId uniqueidentifier=(
                    SELECT EmployeeId FROM dbo.Employees
                    WHERE TenantId=@TenantId AND PartyId=@PartyId AND IsActive=1);
                IF @ResolvedEmployeeId IS NULL OR
                   (@EmployeeId IS NOT NULL AND @EmployeeId<>@ResolvedEmployeeId)
                    THROW 51702,N'El tercero no tiene un rol de empleado activo en la empresa.',1;
                IF EXISTS(SELECT 1 FROM (VALUES
                    (@ContractType,N'payroll-contract-type'),(@SalaryType,N'payroll-salary-type'),
                    (@Frequency,N'payroll-pay-frequency'),(@RiskClass,N'payroll-risk-class'),
                    (@WorkerType,N'payroll-worker-type'),(@PaymentMethod,N'payroll-payment-method')) x(Id,Catalog)
                    WHERE NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions o WHERE o.OptionId=x.Id AND o.CatalogCode=x.Catalog AND o.IsActive=1))
                    THROW 51703,N'Una opción del contrato no pertenece al catálogo esperado.',1;
                IF @WorkerSubtype IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@WorkerSubtype AND CatalogCode=N'payroll-worker-subtype' AND IsActive=1)
                    THROW 51704,N'El subtipo de trabajador no es válido.',1;
                IF @Configuration IS NOT NULL
                BEGIN
                  IF JSON_VALUE(@Configuration,'$.accountingProfileId') IS NOT NULL AND
                    (JSON_VALUE(@Configuration,'$.healthPartyId') IS NULL OR JSON_VALUE(@Configuration,'$.pensionPartyId') IS NULL
                     OR JSON_VALUE(@Configuration,'$.severancePartyId') IS NULL OR JSON_VALUE(@Configuration,'$.riskPartyId') IS NULL
                     OR JSON_VALUE(@Configuration,'$.compensationPartyId') IS NULL)
                    THROW 51704,N'Para usar un perfil completa EPS, pensión, cesantías, ARL y caja de compensación del contrato.',1;
                  IF EXISTS(SELECT 1 FROM OPENJSON(@Configuration) value
                    WHERE value.[key] IN(N'healthPartyId',N'pensionPartyId',N'severancePartyId',N'riskPartyId',N'compensationPartyId')
                      AND value.[type]<>0 AND NOT EXISTS(SELECT 1 FROM dbo.Parties party
                        WHERE party.PartyId=TRY_CONVERT(uniqueidentifier,value.value) AND party.TenantId=@TenantId
                          AND party.PartyType=N'Organization' AND party.IsActive=1))
                    THROW 51704,N'Las entidades de afiliación deben ser terceros jurídicos activos de esta empresa.',1;
                  IF JSON_VALUE(@Configuration,'$.accountingProfileId') IS NOT NULL AND NOT EXISTS(
                    SELECT 1 FROM payroll.AccountingProfiles WHERE ProfileId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.accountingProfileId'))
                      AND TenantId=@TenantId AND IsActive=1)
                    THROW 51704,N'El perfil de nómina no está activo en esta empresa.',1;
                  IF JSON_VALUE(@Configuration,'$.costCenterId') IS NOT NULL AND NOT EXISTS(
                    SELECT 1 FROM dbo.AccountingCostCenters WHERE CostCenterId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.costCenterId'))
                      AND TenantId=@TenantId AND IsActive=1)
                    THROW 51704,N'El centro de costo no está activo en esta empresa.',1;
                END;
                IF @Bank IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@Bank AND CatalogCode=N'payroll-bank' AND IsActive=1)
                    THROW 51704,N'El banco no pertenece al catálogo de nómina.',1;
                IF @BankAccountType IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@BankAccountType AND CatalogCode=N'payroll-bank-account-type' AND IsActive=1)
                    THROW 51704,N'El tipo de cuenta no pertenece al catálogo de nómina.',1;
                IF EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@PaymentMethod AND Code=N'Transfer')
                   AND (@Bank IS NULL OR @BankAccountType IS NULL OR NULLIF(LTRIM(RTRIM(@BankAccountNumber)),N'') IS NULL)
                    THROW 51704,N'Banco, tipo y número de cuenta son obligatorios para transferencia.',1;

                IF EXISTS(SELECT 1 FROM payroll.Employments WHERE EmploymentId=@Id AND TenantId=@TenantId)
                BEGIN
                    UPDATE payroll.Employments SET PartyId=@PartyId,BusinessId=@BusinessId,EmployeeId=@ResolvedEmployeeId,
                      ContractTypeOptionId=@ContractType,SalaryTypeOptionId=@SalaryType,PayFrequencyOptionId=@Frequency,
                      RiskClassOptionId=@RiskClass,WorkerTypeOptionId=@WorkerType,WorkerSubtypeOptionId=@WorkerSubtype,
                      PaymentMethodOptionId=@PaymentMethod,ContractNumber=@ContractNumber,StartDate=@StartDate,
                      EndDate=@EndDate,MonthlySalary=@Salary,IntegralSalaryPercentage=@IntegralPercentage,
                      BankAccountReference=@BankReference,BankOptionId=@Bank,
                      BankAccountTypeOptionId=@BankAccountType,BankAccountNumber=@BankAccountNumber,
                      AppliesTransportAllowance=COALESCE(@ApplyTransport,AppliesTransportAllowance),
                      IsActive=@Active,UpdatedBy=@UserId,UpdatedAt=@Now
                    WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId AND RowVersion=@RowVersion;
                    IF @@ROWCOUNT<>1 THROW 51705,N'La relación laboral cambió; recargue antes de guardar.',1;
                END
                ELSE
                    INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,EmployeeId,
                      ContractTypeOptionId,SalaryTypeOptionId,PayFrequencyOptionId,RiskClassOptionId,
                      WorkerTypeOptionId,WorkerSubtypeOptionId,PaymentMethodOptionId,ContractNumber,
                      StartDate,EndDate,MonthlySalary,IntegralSalaryPercentage,BankAccountReference,
                      BankOptionId,BankAccountTypeOptionId,BankAccountNumber,AppliesTransportAllowance,
                      IsActive,CreatedBy,CreatedAt)
                    VALUES(@Id,@TenantId,@PartyId,@BusinessId,@ResolvedEmployeeId,@ContractType,@SalaryType,@Frequency,
                      @RiskClass,@WorkerType,@WorkerSubtype,@PaymentMethod,@ContractNumber,@StartDate,@EndDate,
                      @Salary,@IntegralPercentage,@BankReference,@Bank,@BankAccountType,
                      @BankAccountNumber,COALESCE(@ApplyTransport,CAST(1 AS bit)),@Active,@UserId,@Now);
                END
                IF @Configuration IS NOT NULL
                  UPDATE payroll.Employments SET
                    AccountingProfileId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.accountingProfileId')),
                    HealthPartyId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.healthPartyId')),
                    PensionPartyId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.pensionPartyId')),
                    SeverancePartyId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.severancePartyId')),
                    RiskPartyId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.riskPartyId')),
                    CompensationPartyId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.compensationPartyId')),
                    CostCenterId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@Configuration,'$.costCenterId'))
                  WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
                """, connection, tx);
            AddEmploymentParameters(command, user, request);
            await command.ExecuteNonQueryAsync(ct);
            var saved=await ReadEmploymentAsync(connection,tx,user,request.EmploymentId,ct);
            await tx.CommitAsync(ct);
            return saved;
        }
        catch (SqlException error) when (error.Number is >= 51700 and <= 51706)
        { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw new PayrollValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException("Ya existe un contrato o una relación activa para esa persona."); }
        catch { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<PayrollConceptView> SaveConceptAsync(PayrollUserIdentity user,
        SavePayrollConceptRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM (VALUES
                  (@Nature,N'payroll-concept-nature'),(@Method,N'payroll-calculation-method'),
                  (@Treatment,N'payroll-concept-treatment'),(@Accounting,N'payroll-accounting-category')) x(Id,Catalog)
                  WHERE NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions o WHERE o.OptionId=x.Id AND o.CatalogCode=x.Catalog AND o.IsActive=1))
                  THROW 51710,N'Una opción del concepto no pertenece al catálogo esperado.',1;
                IF @Dian IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@Dian AND CatalogCode=N'payroll-dian-concept' AND IsActive=1)
                  THROW 51711,N'El concepto DIAN no es válido.',1;
                IF @SystemRole IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@SystemRole AND CatalogCode=N'payroll-system-concept-role' AND IsActive=1)
                  THROW 51712,N'El rol técnico del concepto no es válido.',1;
                IF EXISTS(SELECT 1 FROM payroll.Concepts WHERE ConceptId=@Id AND TenantId=@TenantId)
                BEGIN
                  UPDATE payroll.Concepts SET Code=@Code,Name=@Name,NatureOptionId=@Nature,
                    CalculationMethodOptionId=@Method,TreatmentOptionId=@Treatment,DianConceptOptionId=@Dian,
                    AccountingCategoryOptionId=@Accounting,SystemRoleOptionId=@SystemRole,
                    IsSalaryBase=@SalaryBase,IsSocialSecurityBase=@SecurityBase,IsBenefitsBase=@BenefitsBase,
                    IsTaxWithholdingBase=@WithholdingBase,RequiresDeductionAgreement=@RequiresAgreement,
                    EffectiveFrom=@From,EffectiveTo=@To,IsActive=@Active,UpdatedBy=@UserId,UpdatedAt=@Now
                  WHERE ConceptId=@Id AND TenantId=@TenantId AND RowVersion=@RowVersion;
                  IF @@ROWCOUNT<>1 THROW 51713,N'El concepto cambió; recargue antes de guardar.',1;
                END
                ELSE INSERT payroll.Concepts(ConceptId,TenantId,Code,Name,NatureOptionId,
                    CalculationMethodOptionId,TreatmentOptionId,DianConceptOptionId,AccountingCategoryOptionId,
                    SystemRoleOptionId,IsSalaryBase,IsSocialSecurityBase,IsBenefitsBase,IsTaxWithholdingBase,
                    RequiresDeductionAgreement,EffectiveFrom,EffectiveTo,IsActive,CreatedBy,CreatedAt)
                  VALUES(@Id,@TenantId,@Code,@Name,@Nature,@Method,@Treatment,@Dian,@Accounting,@SystemRole,
                    @SalaryBase,@SecurityBase,@BenefitsBase,@WithholdingBase,@RequiresAgreement,@From,@To,
                    @Active,@UserId,@Now);
                """, connection, tx);
            AddConceptParameters(command, user, request);
            await command.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number is >= 51710 and <= 51713)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException("Ya existe un concepto con ese código o rol técnico."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return (await GetOptionsAsync(user, ct)).Concepts.Single(x => x.ConceptId == request.ConceptId);
    }

    public async Task<PayrollRuleSetView> SaveRuleSetAsync(PayrollUserIdentity user,
        SavePayrollRuleSetRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var command = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM payroll.RuleSets WHERE RuleSetId=@Id AND TenantId=@TenantId)
                BEGIN
                  UPDATE payroll.RuleSets SET CountryCode=@Country,Code=@Code,Name=@Name,EffectiveFrom=@From,
                    EffectiveTo=@To,SourceReference=@Source
                  WHERE RuleSetId=@Id AND TenantId=@TenantId AND Status=N'Draft' AND RowVersion=@RowVersion;
                  IF @@ROWCOUNT<>1 THROW 51720,N'La regla cambió o ya fue aprobada.',1;
                  DELETE payroll.RuleParameters WHERE RuleSetId=@Id;
                END
                ELSE INSERT payroll.RuleSets(RuleSetId,TenantId,CountryCode,Code,Name,EffectiveFrom,EffectiveTo,
                    SourceReference,Status,CreatedBy,CreatedAt)
                  VALUES(@Id,@TenantId,@Country,@Code,@Name,@From,@To,@Source,N'Draft',@UserId,@Now);
                INSERT payroll.RuleParameters(RuleParameterId,RuleSetId,Code,NumericValue,UnitCode,Description)
                SELECT ParameterId,@Id,Code,NumericValue,UnitCode,Description FROM OPENJSON(@Parameters)
                  WITH(ParameterId uniqueidentifier,Code nvarchar(64),NumericValue decimal(19,8),UnitCode nvarchar(32),Description nvarchar(300));
                """, connection, tx))
            {
                command.Parameters.AddWithValue("@Id", request.RuleSetId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@Country", request.CountryCode);
                command.Parameters.AddWithValue("@Code", request.Code);
                command.Parameters.AddWithValue("@Name", request.Name);
                command.Parameters.AddWithValue("@From", request.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@To", DbDate(request.EffectiveTo));
                command.Parameters.AddWithValue("@Source", request.SourceReference);
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
                command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = (object?)request.RowVersion ?? DBNull.Value;
                command.Parameters.Add("@Parameters",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Parameters.Select(parameter=>new {
                    ParameterId=ids.NewId(),parameter.Code,parameter.NumericValue,parameter.UnitCode,parameter.Description
                }));
                await command.ExecuteNonQueryAsync(ct);
            }
            var result=await ReadRuleSetAsync(connection,tx,user.TenantId,request.RuleSetId,ct);
            await tx.CommitAsync(ct);return result;
        }
        catch (SqlException error) when (error.Number == 51720)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException("Ya existe un conjunto o parámetro con ese código y vigencia."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<PayrollRuleSetView> ApproveRuleSetAsync(PayrollUserIdentity user,
        Guid ruleSetId, byte[] rowVersion, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal);
            await using (var read = new SqlCommand("""
                SELECT p.Code,p.NumericValue FROM payroll.RuleSets r WITH(UPDLOCK,HOLDLOCK)
                JOIN payroll.RuleParameters p ON p.RuleSetId=r.RuleSetId
                WHERE r.RuleSetId=@Id AND r.TenantId=@TenantId AND r.Status=N'Draft' AND r.RowVersion=@Version;
                """, connection, tx))
            {
                read.Parameters.AddWithValue("@Id", ruleSetId); read.Parameters.AddWithValue("@TenantId", user.TenantId);
                read.Parameters.Add("@Version", SqlDbType.Timestamp).Value = rowVersion;
                await using var reader = await read.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) parameters.Add(reader.GetString(0), reader.GetDecimal(1));
            }
            if (parameters.Count == 0) throw new PayrollConflictException("La regla cambió o no está en borrador.");
            try { PayrollCalculator.ValidateRuleParameters(parameters); }
            catch (PayrollCalculationException error) { throw new PayrollValidationException(error.Message); }
            await using var update = new SqlCommand("""
                UPDATE payroll.RuleSets SET Status=N'Approved',ApprovedBy=@UserId,ApprovedAt=@Now
                WHERE RuleSetId=@Id AND TenantId=@TenantId AND Status=N'Draft' AND RowVersion=@Version;
                IF @@ROWCOUNT<>1 THROW 51721,N'La regla cambió antes de aprobar.',1;
                """, connection, tx);
            update.Parameters.AddWithValue("@Id", ruleSetId); update.Parameters.AddWithValue("@TenantId", user.TenantId);
            update.Parameters.AddWithValue("@UserId", user.UserId); update.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            update.Parameters.Add("@Version", SqlDbType.Timestamp).Value = rowVersion;
            await update.ExecuteNonQueryAsync(ct);
            var result=await ReadRuleSetAsync(connection,tx,user.TenantId,ruleSetId,ct);
            await tx.CommitAsync(ct);return result;
        }
        catch (Exception error) when (error is PayrollConflictException or PayrollValidationException)
        { await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number == 51721)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<PayrollRuleSetView> RetireRuleSetAsync(PayrollUserIdentity user,
        Guid ruleSetId, byte[] rowVersion, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE payroll.RuleSets SET Status=N'Retired'
            WHERE RuleSetId=@Id AND TenantId=@TenantId AND Status<>N'Retired'
              AND RowVersion=@Version;
            IF @@ROWCOUNT<>1 THROW 51721,N'La regla cambió o ya estaba retirada.',1;
            """, connection);
        command.Parameters.AddWithValue("@Id", ruleSetId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = rowVersion;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51721)
        { throw new PayrollConflictException(error.Message); }
        return await ReadRuleSetAsync(connection,null,user.TenantId,ruleSetId,ct);
    }

    public async Task<PayrollDeductionAgreementView> SaveDeductionAgreementAsync(
        PayrollUserIdentity user, SavePayrollDeductionAgreementRequest request,
        CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM payroll.Employments WHERE EmploymentId=@EmploymentId
                  AND TenantId=@TenantId AND BusinessId=@BusinessId)
                  THROW 51722,N'La relación laboral está fuera de la empresa.',1;
                IF NOT EXISTS(SELECT 1 FROM payroll.Concepts WHERE ConceptId=@ConceptId AND TenantId=@TenantId
                  AND RequiresDeductionAgreement=1 AND IsActive=1)
                  THROW 51723,N'El concepto no es una deducción administrada activa.',1;
                IF NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@AuthorityId
                  AND CatalogCode=N'payroll-deduction-authority' AND IsActive=1)
                  THROW 51724,N'La autoridad de la deducción no es válida.',1;
                IF @BeneficiaryId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Parties WHERE PartyId=@BeneficiaryId AND TenantId=@TenantId AND IsActive=1)
                  THROW 51725,N'El beneficiario está fuera del tenant.',1;
                IF EXISTS(SELECT 1 FROM payroll.DeductionAgreements WHERE DeductionAgreementId=@Id AND TenantId=@TenantId)
                BEGIN
                  UPDATE payroll.DeductionAgreements SET AuthorityOptionId=@AuthorityId,
                    BeneficiaryPartyId=@BeneficiaryId,ReferenceNumber=@Reference,EvidenceUrl=@Evidence,
                    EffectiveFrom=@From,EffectiveTo=@To,AuthorizedTotal=@AuthorizedTotal,
                    InstallmentAmount=@Installment,Priority=@Priority,
                    MustProtectMinimumNetPay=@ProtectMinimum,IsActive=@Active,UpdatedAt=@Now
                  WHERE DeductionAgreementId=@Id AND TenantId=@TenantId AND EmploymentId=@EmploymentId
                    AND ConceptId=@ConceptId AND RowVersion=@Version;
                  IF @@ROWCOUNT<>1 THROW 51726,N'El acuerdo cambió; recargue antes de guardar.',1;
                END
                ELSE INSERT payroll.DeductionAgreements(DeductionAgreementId,TenantId,EmploymentId,ConceptId,
                  AuthorityOptionId,BeneficiaryPartyId,ReferenceNumber,EvidenceUrl,EffectiveFrom,EffectiveTo,
                  AuthorizedTotal,InstallmentAmount,DeductedToDate,Priority,MustProtectMinimumNetPay,IsActive,
                  CreatedBy,CreatedAt)
                VALUES(@Id,@TenantId,@EmploymentId,@ConceptId,@AuthorityId,@BeneficiaryId,@Reference,@Evidence,
                  @From,@To,@AuthorizedTotal,@Installment,0,@Priority,@ProtectMinimum,@Active,@UserId,@Now);
                """, connection, tx);
            command.Parameters.AddWithValue("@Id", request.DeductionAgreementId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@EmploymentId", request.EmploymentId);
            command.Parameters.AddWithValue("@ConceptId", request.ConceptId);
            command.Parameters.AddWithValue("@AuthorityId", request.AuthorityOptionId);
            command.Parameters.AddWithValue("@BeneficiaryId", (object?)request.BeneficiaryPartyId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Reference", request.ReferenceNumber);
            command.Parameters.AddWithValue("@Evidence", request.EvidenceUrl);
            command.Parameters.AddWithValue("@From", request.EffectiveFrom.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@To", DbDate(request.EffectiveTo));
            DecimalNullable(command, "@AuthorizedTotal", request.AuthorizedTotal, 4);
            DecimalNullable(command, "@Installment", request.InstallmentAmount, 4);
            command.Parameters.AddWithValue("@Priority", request.Priority);
            command.Parameters.AddWithValue("@ProtectMinimum", request.MustProtectMinimumNetPay);
            command.Parameters.AddWithValue("@Active", request.IsActive);
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = (object?)request.RowVersion ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number is >= 51722 and <= 51726)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollValidationException(error.Message); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return await ReadAgreementAsync(user, request.DeductionAgreementId, ct);
    }

    public async Task<PayrollSettingsView> SaveSettingsAsync(PayrollUserIdentity user,
        SavePayrollSettingsRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM payroll.Settings WHERE TenantId=@TenantId)
            BEGIN
              UPDATE payroll.Settings SET IsEmployerExemptFromHealthSenaIcbf=@Exempt,
                ElectronicPayrollEnabled=@Electronic,UpdatedBy=@UserId,UpdatedAt=@Now
              WHERE TenantId=@TenantId AND RowVersion=@Version;
              IF @@ROWCOUNT<>1 THROW 51727,N'La configuración cambió; recargue antes de guardar.',1;
            END
            ELSE INSERT payroll.Settings(TenantId,IsEmployerExemptFromHealthSenaIcbf,
              ElectronicPayrollEnabled,UpdatedBy,UpdatedAt)
            VALUES(@TenantId,@Exempt,@Electronic,@UserId,@Now);
            SELECT IsEmployerExemptFromHealthSenaIcbf,ElectronicPayrollEnabled,RowVersion
            FROM payroll.Settings WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@Exempt", request.IsEmployerExemptFromHealthSenaIcbf);
        command.Parameters.AddWithValue("@Electronic", request.ElectronicPayrollEnabled);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.Add("@Version", SqlDbType.Timestamp).Value = (object?)request.RowVersion ?? DBNull.Value;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
            return new(reader.GetBoolean(0), reader.GetBoolean(1), (byte[])reader[2]);
        }
        catch (SqlException error) when (error.Number == 51727)
        { throw new PayrollConflictException(error.Message); }
    }

    private const string NoveltyProjectionSql = """
        SELECT n.NoveltyId,n.EmploymentId,
          COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
          n.ConceptId,c.Name,n.NoveltyTypeOptionId,o.Label,n.DeductionAgreementId,
          n.StartDate,n.EndDate,n.Quantity,n.UnitAmount,n.TotalAmount,n.Notes,n.EvidenceUrl,n.Status,n.ReasonId,n.RowVersion
        FROM payroll.Novelties n
        JOIN payroll.Employments e ON e.EmploymentId=n.EmploymentId AND e.TenantId=n.TenantId
        JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
        JOIN payroll.Concepts c ON c.ConceptId=n.ConceptId AND c.TenantId=n.TenantId
        JOIN payroll.CatalogOptions o ON o.OptionId=n.NoveltyTypeOptionId
        """;

    private const string NoveltyResultSql = NoveltyProjectionSql + """

        WHERE n.NoveltyId=@Id AND n.TenantId=@TenantId AND n.BusinessId=@BusinessId;
        """;

    private static PayrollNoveltyView ReadNovelty(SqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2).Trim(),
            reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            DateOnly.FromDateTime(reader.GetDateTime(8)), DateOnly.FromDateTime(reader.GetDateTime(9)),
            reader.GetDecimal(10), reader.IsDBNull(11) ? null : reader.GetDecimal(11),
            reader.GetDecimal(12), NullableString(reader, 13), NullableString(reader, 14), reader.GetString(15))
        {
            ReasonId = reader.IsDBNull(16) ? null : reader.GetGuid(16),
            RowVersion = (byte[])reader[17]
        };

    public async Task<PayrollNoveltyView> SaveNoveltyAsync(PayrollUserIdentity user, SavePayrollNoveltyRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            IF @Version IS NOT NULL
            BEGIN
              DECLARE @Status nvarchar(24),@CurrentVersion binary(8),@Employment uniqueidentifier;
              SELECT @Status=Status,@CurrentVersion=RowVersion,@Employment=EmploymentId
              FROM payroll.Novelties WITH(UPDLOCK,HOLDLOCK)
              WHERE NoveltyId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
              IF @Status IS NULL THROW 51735,N'La novedad no existe en esta sede.',1;
              IF @Status NOT IN(N'Draft',N'Approved') OR @CurrentVersion<>@Version
                THROW 51736,N'La novedad cambió o ya fue consumida. Actualiza antes de continuar.',1;
              IF @Employment<>@EmploymentId
                THROW 51734,N'No se puede cambiar el trabajador de una novedad existente.',1;
            END;
            IF NOT EXISTS(SELECT 1 FROM payroll.Employments WHERE EmploymentId=@EmploymentId AND TenantId=@TenantId AND BusinessId=@BusinessId)
              THROW 51730,N'La relación laboral está fuera de la empresa.',1;
            IF NOT EXISTS(SELECT 1 FROM payroll.Concepts WHERE ConceptId=@ConceptId AND TenantId=@TenantId AND IsActive=1)
              THROW 51731,N'El concepto no está activo.',1;
            IF NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@TypeId AND CatalogCode=N'payroll-novelty-type' AND IsActive=1)
              THROW 51732,N'El tipo de novedad no es válido.',1;
            IF @AgreementId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.DeductionAgreements
              WHERE DeductionAgreementId=@AgreementId AND TenantId=@TenantId AND EmploymentId=@EmploymentId
                AND ConceptId=@ConceptId AND IsActive=1 AND EffectiveFrom<=@EndDate AND (EffectiveTo IS NULL OR EffectiveTo>=@StartDate))
              THROW 51733,N'El acuerdo de deducción no está vigente para esta novedad.',1;
            IF @ReasonId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons WHERE ReasonId=@ReasonId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51734,N'El motivo no pertenece a la empresa.',1;
            SELECT method.Code,kind.Code FROM payroll.Concepts concept
            JOIN payroll.CatalogOptions method ON method.OptionId=concept.CalculationMethodOptionId
              AND method.CatalogCode=N'payroll-calculation-method' AND method.IsActive=1
            JOIN payroll.CatalogOptions kind ON kind.OptionId=@TypeId
              AND kind.CatalogCode=N'payroll-novelty-type' AND kind.IsActive=1
            WHERE concept.ConceptId=@ConceptId AND concept.TenantId=@TenantId AND concept.IsActive=1;
            """, connection,transaction);
        command.Parameters.AddWithValue("@Id", request.NoveltyId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@EmploymentId", request.EmploymentId);
        command.Parameters.AddWithValue("@ConceptId", request.ConceptId); command.Parameters.AddWithValue("@TypeId", request.NoveltyTypeOptionId);
        command.Parameters.AddWithValue("@ReasonId", (object?)request.ReasonId ?? DBNull.Value); command.Parameters.AddWithValue("@AgreementId", (object?)request.DeductionAgreementId ?? DBNull.Value);
        command.Parameters.AddWithValue("@StartDate", request.StartDate.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EndDate", request.EndDate.ToDateTime(TimeOnly.MinValue));
        Decimal(command, "@Quantity", request.Quantity, 6); DecimalNullable(command, "@UnitAmount", request.UnitAmount, 4); Decimal(command, "@TotalAmount", request.TotalAmount, 4);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value); command.Parameters.AddWithValue("@Evidence", (object?)request.EvidenceUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@UserId", user.UserId); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        command.Parameters.Add("@Version",SqlDbType.Binary,8).Value=(object?)request.RowVersion??DBNull.Value;
        try
        {
            await using(var reader=await command.ExecuteReaderAsync(ct))
            {
                if(!await reader.ReadAsync(ct))throw new PayrollValidationException("El concepto o tipo de novedad no está disponible.");
                try {PayrollNoveltyInput.ValidateAmounts(reader.GetString(0),reader.GetString(1),request.Quantity,request.UnitAmount,request.TotalAmount);}
                catch(PayrollCalculationException error){throw new PayrollValidationException(error.Message);}
            }
            command.CommandText="""
                SET NOCOUNT ON;
                IF @Version IS NULL
                INSERT payroll.Novelties(NoveltyId,TenantId,BusinessId,EmploymentId,ConceptId,NoveltyTypeOptionId,
                  ReasonId,DeductionAgreementId,StartDate,EndDate,Quantity,UnitAmount,TotalAmount,Notes,EvidenceUrl,
                  Status,CreatedBy,CreatedAt,ApprovedBy,ApprovedAt)
                VALUES(@Id,@TenantId,@BusinessId,@EmploymentId,@ConceptId,@TypeId,@ReasonId,@AgreementId,
                  @StartDate,@EndDate,@Quantity,@UnitAmount,@TotalAmount,@Notes,@Evidence,N'Approved',@UserId,@Now,@UserId,@Now);
                ELSE
                  UPDATE payroll.Novelties SET ConceptId=@ConceptId,NoveltyTypeOptionId=@TypeId,
                    ReasonId=@ReasonId,DeductionAgreementId=@AgreementId,StartDate=@StartDate,EndDate=@EndDate,
                    Quantity=@Quantity,UnitAmount=@UnitAmount,TotalAmount=@TotalAmount,Notes=@Notes,EvidenceUrl=@Evidence,
                    Status=N'Approved',ApprovedBy=@UserId,ApprovedAt=@Now
                  WHERE NoveltyId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                    AND RowVersion=@Version AND Status IN(N'Draft',N'Approved');
                IF @@ROWCOUNT<>1 THROW 51736,N'La novedad cambió antes de guardarse.',1;
                """ + NoveltyResultSql;
            PayrollNoveltyView result;
            await using(var reader=await command.ExecuteReaderAsync(ct))
            {
                if(!await reader.ReadAsync(ct))throw new PayrollConflictException("La novedad no pudo guardarse completa.");
                result=ReadNovelty(reader);
            }
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException error) when (error.Number is >= 51730 and <= 51734) { throw new PayrollValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627) { throw new PayrollConflictException("La novedad ya existe."); }
        catch (SqlException error) when (error.Number == 51735) { throw new PayrollNotFoundException(error.Message); }
        catch (SqlException error) when (error.Number == 51736) { throw new PayrollConflictException(error.Message); }
    }

    public async Task<PayrollNoveltyView> VoidNoveltyAsync(PayrollUserIdentity user,Guid noveltyId,byte[] rowVersion,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await using var command=new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Status nvarchar(24),@CurrentVersion binary(8);
            SELECT @Status=Status,@CurrentVersion=RowVersion FROM payroll.Novelties WITH(UPDLOCK,HOLDLOCK)
            WHERE NoveltyId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
            IF @Status IS NULL THROW 51735,N'La novedad no existe en esta sede.',1;
            IF @Status<>N'Voided'
            BEGIN
              IF @Status NOT IN(N'Draft',N'Approved') OR @CurrentVersion<>@Version
                THROW 51736,N'La novedad cambió o ya fue consumida. Actualiza antes de continuar.',1;
              UPDATE payroll.Novelties SET Status=N'Voided',ApprovedBy=@UserId,ApprovedAt=@Now
              WHERE NoveltyId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId AND RowVersion=@Version;
              IF @@ROWCOUNT<>1 THROW 51736,N'La novedad cambió antes de retirarse.',1;
            END;
            """+NoveltyResultSql,connection,transaction);
        command.Parameters.AddWithValue("@Id",noveltyId);command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());command.Parameters.Add("@Version",SqlDbType.Binary,8).Value=rowVersion;
        try
        {
            PayrollNoveltyView result;
            await using(var reader=await command.ExecuteReaderAsync(ct))
            {
                if(!await reader.ReadAsync(ct))throw new PayrollConflictException("La novedad no pudo retirarse completa.");
                result=ReadNovelty(reader);
            }
            await transaction.CommitAsync(ct);return result;
        }
        catch(SqlException error)when(error.Number==51735){throw new PayrollNotFoundException(error.Message);}
        catch(SqlException error)when(error.Number==51736){throw new PayrollConflictException(error.Message);}
    }

    public async Task<PayrollPaymentBatchView> CreatePaymentBatchAsync(
        PayrollUserIdentity user, CreatePayrollPaymentBatchRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            DateOnly periodStart; DateOnly periodEnd; decimal total; string? settlementCategory=null; TimeZoneInfo paymentZone;
            var lines = new List<(Guid RunEmployeeId, Guid PartyId, decimal Amount, string Name)>();
            var pendingBefore=new List<PayrollPayableEmployee>();
            string? formCode=null,formName=null,formType=null; int? formVersion=null;
            await using (var existing = new SqlCommand("""
                DECLARE @LockedRun uniqueidentifier;
                SELECT @LockedRun=PayrollRunId FROM payroll.Runs WITH(UPDLOCK,HOLDLOCK)
                  WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId;
                SELECT b.PaymentDate,b.PaymentMethodOptionId,COALESCE(b.PaymentMethodName,o.Label),b.ReferenceNumber,b.Status,
                       COUNT(l.PaymentLineId),b.TotalAmount,b.RowVersion,
                       (SELECT TOP(1) re.PayrollRunId FROM payroll.PaymentLines pl
                        JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
                        WHERE pl.PaymentBatchId=b.PaymentBatchId ORDER BY re.PayrollRunId),
                       CAST(CASE WHEN EXISTS(SELECT 1 FROM payroll.OutboxMessages signal
                         WHERE signal.AggregateId=b.PaymentBatchId AND signal.TenantId=b.TenantId
                           AND signal.BusinessId=b.BusinessId AND signal.MessageType=N'AccountingPostingRequested'
                           AND signal.PublishedAt IS NOT NULL) THEN 1 ELSE 0 END AS bit),
                       COUNT(DISTINCT member.PayrollRunId),b.PaymentMethodId,b.AccountId,posting.AccountingStatus,posting.IsPaid
                FROM payroll.PaymentBatches b
                JOIN payroll.PaymentBatchPostingStatus posting ON posting.PaymentBatchId=b.PaymentBatchId AND posting.TenantId=b.TenantId AND posting.BusinessId=b.BusinessId
                JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
                LEFT JOIN payroll.PaymentLines l ON l.PaymentBatchId=b.PaymentBatchId
                LEFT JOIN payroll.RunEmployees member ON member.PayrollRunEmployeeId=l.PayrollRunEmployeeId
                WHERE b.PaymentBatchId=@BatchId AND b.TenantId=@TenantId AND b.BusinessId=@BusinessId
                GROUP BY b.PaymentDate,b.PaymentMethodOptionId,o.Label,b.ReferenceNumber,b.Status,
                         b.TotalAmount,b.RowVersion,b.PaymentBatchId,b.TenantId,b.BusinessId,b.PaymentMethodId,b.AccountId,b.PaymentMethodName,posting.AccountingStatus,posting.IsPaid;
                IF EXISTS(SELECT 1 FROM payroll.PaymentBatches WHERE PaymentBatchId=@BatchId AND TenantId=@TenantId AND BusinessId=@BusinessId)
                SELECT TOP(@EmployeeReadLimit) PayrollRunEmployeeId,PaymentMethodOptionId,PaymentMethodName,NetPayable,
                  BankDestinationComplete,EmployeeName
                FROM payroll.PayableRunEmployees(@TenantId,@BusinessId,@RunId);
                ELSE BEGIN
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings WITH(HOLDLOCK)
                  WHERE TenantId=@TenantId AND Status=N'Ready' AND EffectiveFrom<=@PaymentDate)
                  THROW 51735,N'La contabilidad debe estar habilitada para la fecha del pago.',1;
                DECLARE @SelectedForm TABLE(Code nvarchar(64),Name nvarchar(160),Version int,MethodCode nvarchar(32));
                IF @FormId IS NOT NULL BEGIN
                  INSERT @SelectedForm SELECT form.Code,form.Name,form.Version,form.MethodCode
                  FROM reference.PaymentMethodDetails form WITH(HOLDLOCK)
                  JOIN payroll.CatalogOptions method ON method.OptionId=@MethodOption AND method.CatalogCode=N'payroll-payment-method'
                    AND method.Code=form.MethodCode AND method.IsActive=1
                  JOIN dbo.AccountingAccounts account ON account.AccountId=form.AccountId AND account.TenantId=@TenantId
                  WHERE form.TenantId=@TenantId AND form.PaymentMethodId=@FormId AND form.UseForPayroll=1
                    AND form.IsActive=1 AND form.IsReady=1;
                  IF @@ROWCOUNT<>1 THROW 51735,N'La forma elegida no está habilitada para este grupo de nómina.',1;
                END;
                SELECT TOP(@EmployeeReadLimit) r.PeriodStart,r.PeriodEnd,eligible.SettlementCategory,eligible.PayrollRunEmployeeId,eligible.PartyId,
                       eligible.NetPayable,eligible.EmployeeName,eligible.BankDestinationComplete,business.TimeZone,
                       eligible.PaymentMethodOptionId,eligible.PaymentMethodName
                FROM payroll.Runs r
                JOIN dbo.Businesses business ON business.BusinessId=r.BusinessId AND business.TenantId=r.TenantId
                CROSS JOIN payroll.PayableRunEmployees(@TenantId,@BusinessId,@RunId) eligible
                WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                ORDER BY eligible.PartyId;
                SELECT Code,Name,Version,MethodCode FROM @SelectedForm;
                END;
                """, connection, tx))
            {
                existing.Parameters.AddWithValue("@EmployeeReadLimit",PayrollPaymentLimits.MaximumEmployees+1);
                existing.Parameters.AddWithValue("@RunId", request.PayrollRunId);
                existing.Parameters.Add("@PaymentDate",SqlDbType.Date).Value=request.PaymentDate.ToDateTime(TimeOnly.MinValue);
                existing.Parameters.AddWithValue("@MethodOption",request.PaymentMethodOptionId);
                existing.Parameters.Add("@FormId",SqlDbType.UniqueIdentifier).Value=(object?)request.PaymentMethodId??DBNull.Value;
                existing.Parameters.AddWithValue("@BatchId", request.PaymentBatchId);
                existing.Parameters.AddWithValue("@TenantId", user.TenantId);
                existing.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await existing.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    if(reader.GetInt32(10)!=1) throw new PayrollConflictException("El lote original no corresponde a una sola liquidación.");
                    var existingRunId = reader.GetGuid(8);
                    if (existingRunId != request.PayrollRunId ||
                        DateOnly.FromDateTime(reader.GetDateTime(0)) != request.PaymentDate ||
                        reader.GetGuid(1) != request.PaymentMethodOptionId ||
                        (reader.IsDBNull(11)?(Guid?)null:reader.GetGuid(11))!=request.PaymentMethodId ||
                        !string.Equals(reader.GetString(3), request.ReferenceNumber, StringComparison.Ordinal))
                        throw new PayrollConflictException(
                            "El identificador del lote ya fue usado con otros datos.");
                    var replay = new PayrollPaymentBatchView(request.PaymentBatchId,
                        existingRunId, request.PaymentDate, request.PaymentMethodOptionId,
                        reader.GetString(2), reader.GetString(3), reader.GetString(4),
                        reader.GetInt32(5), reader.GetDecimal(6), (byte[])reader[7]);
                    replay=replay with {PaymentMethodId=reader.IsDBNull(11)?null:reader.GetGuid(11),AccountId=reader.IsDBNull(12)?null:reader.GetGuid(12),AccountingStatus=NullableString(reader,13),IsPaid=reader.GetBoolean(14)};
                    var signalPublished=reader.GetBoolean(9);
                    await reader.NextResultAsync(ct);
                    replay=replay with {RemainingEmployees=await ReadPayableEmployeesAsync(reader,ct),AccountingSignalPublished=signalPublished};
                    await reader.DisposeAsync();
                    await tx.CommitAsync(ct);
                    return replay;
                }
                await reader.NextResultAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new PayrollConflictException("La liquidación debe estar contabilizada y tener valores pendientes por pagar con ese medio.");
                periodStart = DateOnly.FromDateTime(reader.GetDateTime(0));
                periodEnd = DateOnly.FromDateTime(reader.GetDateTime(1));
                paymentZone=TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(8));
                do
                {
                    if(pendingBefore.Count==PayrollPaymentLimits.MaximumEmployees)
                        throw new PayrollValidationException("La consulta de pagos admite hasta 500 trabajadores pendientes por liquidación.");
                    var pending=new PayrollPayableEmployee(reader.GetGuid(3),reader.GetGuid(9),reader.GetString(10),
                        reader.GetDecimal(5),reader.GetBoolean(7),reader.GetString(6).Trim());
                    pendingBefore.Add(pending);
                    if(pending.PaymentMethodOptionId!=request.PaymentMethodOptionId)continue;
                    settlementCategory=reader.IsDBNull(2)
                        ? throw new PayrollConflictException("El medio de pago no tiene categoría contable configurada.")
                        : reader.GetString(2);
                    if(!reader.GetBoolean(7)) throw new PayrollValidationException("Un trabajador del grupo no tiene destino bancario completo en la liquidación.");
                    lines.Add((reader.GetGuid(3), reader.GetGuid(4), reader.GetDecimal(5),
                        reader.GetString(6).Trim()));
                } while (await reader.ReadAsync(ct));
                await reader.NextResultAsync(ct);
                if(request.PaymentMethodId is not null){
                    if(!await reader.ReadAsync(ct))throw new PayrollConflictException("Falta la identidad de la forma elegida.");
                    formCode=reader.GetString(0);formName=reader.GetString(1);formVersion=reader.GetInt32(2);formType=reader.GetString(3);
                }
            }
            if(lines.Count==0||settlementCategory is null) throw new PayrollConflictException("La liquidación debe estar contabilizada y tener valores pendientes por pagar con ese medio.");
            total = lines.Sum(x => x.Amount);
            if (request.PaymentDate < periodStart)
                throw new PayrollValidationException(
                    "La fecha de pago no puede ser anterior al período liquidado.");
            var accountingLines = lines.Select(x => new PayrollAccountingLine(
                PayrollAccountingCategories.NetPayable, x.Amount, 0, x.PartyId,
                $"Pago de nómina · {x.Name}"))
                .Append(new(settlementCategory, 0, total, null,
                    $"Salida de fondos · {request.ReferenceNumber}"))
                .ToArray();
            var payload = new PayrollAccountingPayload(user.TenantId, user.BusinessId,
                request.PaymentBatchId, "Payment", periodStart, periodEnd, request.PaymentDate,
                $"Pago de nómina {periodStart:yyyy-MM-dd} a {periodEnd:yyyy-MM-dd}", accountingLines,request.PaymentMethodId,formType);
            var json = PayrollContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            var jobId = ids.NewId(); var now = timeProvider.GetUtcNow();
            await using var save = new SqlCommand("""
                INSERT payroll.PaymentBatches(PaymentBatchId,TenantId,BusinessId,PaymentDate,
                  PaymentMethodOptionId,PaymentMethodId,PaymentMethodTypeCode,PaymentMethodCode,PaymentMethodName,PaymentMethodVersion,ReferenceNumber,Status,TotalAmount,CreatedBy,CreatedAt,
                  ConfirmedBy,ConfirmedAt)
                VALUES(@BatchId,@TenantId,@BusinessId,@PaymentDate,@PaymentMethod,@FormId,@FormType,@FormCode,@FormName,@FormVersion,@Reference,
                  N'Confirmed',@Total,@UserId,@Now,@UserId,@Now);
                INSERT payroll.PaymentLines(PaymentLineId,PaymentBatchId,PayrollRunEmployeeId,Amount)
                SELECT NEWID(),@BatchId,input.Id,input.Amount
                FROM OPENJSON(@Employees) WITH(Id uniqueidentifier,Amount decimal(19,4)) input;
                IF @@ROWCOUNT<>@EmployeeCount THROW 51735,N'Los empleados por pagar cambiaron durante la confirmación.',1;
                DECLARE @Sources nvarchar(max)=(SELECT @BatchId DocumentId,N'PayrollPayment' DocumentType,
                  @BusinessId BusinessId,@Payload Payload,CONVERT(varchar(64),@PayloadHash,2) Hash,
                  @OccurredAt OccurredAt,@JobId JobId FOR JSON PATH);
                EXEC dbo.AccountingSourcesAccept @TenantId,@Sources,@Now,1;
                INSERT payroll.OutboxMessages(OutboxMessageId,TenantId,BusinessId,AggregateId,MessageType,
                  PayloadJson,OccurredAt,AttemptCount)
                VALUES(@OutboxId,@TenantId,@BusinessId,@BatchId,N'AccountingPostingRequested',@Notification,@Now,0);
                SELECT batch.RowVersion,COALESCE(batch.PaymentMethodName,method.Label),posting.AccountingStatus,posting.IsPaid FROM payroll.PaymentBatches batch
                  JOIN payroll.PaymentBatchPostingStatus posting ON posting.PaymentBatchId=batch.PaymentBatchId AND posting.TenantId=batch.TenantId AND posting.BusinessId=batch.BusinessId
                  JOIN payroll.CatalogOptions method ON method.OptionId=batch.PaymentMethodOptionId
                  WHERE batch.PaymentBatchId=@BatchId AND batch.TenantId=@TenantId AND batch.BusinessId=@BusinessId;
                """, connection, tx);
            save.Parameters.AddWithValue("@BatchId", request.PaymentBatchId);
            save.Parameters.Add("@FormId",SqlDbType.UniqueIdentifier).Value=(object?)request.PaymentMethodId??DBNull.Value;
            save.Parameters.Add("@FormCode",SqlDbType.NVarChar,64).Value=(object?)formCode??DBNull.Value;
            save.Parameters.Add("@FormName",SqlDbType.NVarChar,160).Value=(object?)formName??DBNull.Value;
            save.Parameters.Add("@FormType",SqlDbType.NVarChar,32).Value=(object?)formType??DBNull.Value;
            save.Parameters.Add("@FormVersion",SqlDbType.Int).Value=(object?)formVersion??DBNull.Value;
            save.Parameters.AddWithValue("@TenantId", user.TenantId);
            save.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            save.Parameters.AddWithValue("@PaymentDate", request.PaymentDate.ToDateTime(TimeOnly.MinValue));
            save.Parameters.AddWithValue("@PaymentMethod", request.PaymentMethodOptionId);
            save.Parameters.AddWithValue("@Reference", request.ReferenceNumber);
            save.Parameters.AddWithValue("@Total", total);
            save.Parameters.AddWithValue("@EmployeeCount", lines.Count);
            save.Parameters.Add("@Employees",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(lines.Select(line=>new {Id=line.RunEmployeeId,line.Amount}));
            save.Parameters.AddWithValue("@UserId", user.UserId);
            save.Parameters.AddWithValue("@Now", now);
            save.Parameters.AddWithValue("@Payload", json);
            save.Parameters.Add("@Notification",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(new{
                DocumentId=request.PaymentBatchId,DocumentType=PayrollAccountingDocumentTypes.Payment,BusinessId=user.BusinessId},Json);
            save.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
            save.Parameters.AddWithValue("@OccurredAt", new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                request.PaymentDate.ToDateTime(new TimeOnly(12,0)),paymentZone)));
            save.Parameters.AddWithValue("@JobId", jobId);
            save.Parameters.AddWithValue("@OutboxId", ids.NewId());
            await using var saved=await save.ExecuteReaderAsync(ct);
            await saved.ReadAsync(ct);
            var version=(byte[])saved[0];var methodName=saved.GetString(1);
            var accountingStatus=NullableString(saved,2);var isPaid=saved.GetBoolean(3);
            // The run lock protects this captured set through insertion and commit.
            var remaining=pendingBefore.Where(employee=>employee.PaymentMethodOptionId!=request.PaymentMethodOptionId).ToArray();
            await saved.DisposeAsync();
            await tx.CommitAsync(ct);
            return new(request.PaymentBatchId, request.PayrollRunId, request.PaymentDate,
                request.PaymentMethodOptionId,
                methodName, request.ReferenceNumber, "Confirmed", lines.Count, total, version) {RemainingEmployees=remaining,PaymentMethodId=request.PaymentMethodId,AccountingStatus=accountingStatus,IsPaid=isPaid};
        }
        catch (SqlException error) when (error.Number == 51735)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch (Exception error) when (error is PayrollConflictException)
        { await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException("El lote de pago ya existe o la liquidación ya fue pagada."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<PayrollRunView> CreateRunAsync(PayrollUserIdentity user, CreatePayrollRunRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        if (request.RunKind == nameof(PayrollRunKind.Regular))
        {
            await using var frequency = new SqlCommand("""
                SELECT TRY_CONVERT(int,MetadataCode) FROM payroll.CatalogOptions
                WHERE OptionId=@Frequency AND CatalogCode=N'payroll-pay-frequency' AND IsActive=1;
                """, connection);
            frequency.Parameters.AddWithValue("@Frequency", request.PayFrequencyOptionId);
            var days = await frequency.ExecuteScalarAsync(ct);
            try { PayrollWorkPeriod.CreateRegularDraft(request.PeriodStart, request.PeriodEnd, days is int value ? value : null); }
            catch (PayrollCalculationException error) { throw new PayrollValidationException(error.Message); }
        }
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51740,N'La empresa está fuera del tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@Frequency AND CatalogCode=N'payroll-pay-frequency' AND IsActive=1)
              THROW 51742,N'La periodicidad no es válida.',1;
            IF NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE Code=@Kind AND CatalogCode=N'payroll-run-kind' AND IsActive=1)
              THROW 51742,N'El tipo de liquidación no está activo.',1;
            IF @OriginalId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.Runs WHERE PayrollRunId=@OriginalId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Approved')
              THROW 51743,N'La liquidación original no está aprobada.',1;
            IF @OriginalId IS NOT NULL AND EXISTS(SELECT 1 FROM payroll.Runs WHERE PayrollRunId=@OriginalId
              AND (PeriodStart<>@PeriodStart OR PeriodEnd<>@PeriodEnd OR PayFrequencyOptionId<>@Frequency))
              THROW 51744,N'El ajuste debe conservar período y periodicidad de la liquidación original.',1;
            INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,RunKind,
              OriginalPayrollRunId,PeriodStart,PeriodEnd,PaymentDate,Status,CalculationVersion,TotalEarnings,
              TotalDeductions,TotalEmployerContributions,TotalProvisions,NetPayable,CreatedBy,CreatedAt)
            VALUES(@Id,@TenantId,@BusinessId,NULL,@Frequency,@Kind,@OriginalId,@PeriodStart,@PeriodEnd,
              @PaymentDate,N'Draft',0,0,0,0,0,0,@UserId,@Now);
            """, connection);
        command.Parameters.AddWithValue("@Id", request.PayrollRunId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Frequency", request.PayFrequencyOptionId); command.Parameters.AddWithValue("@Kind", request.RunKind);
        command.Parameters.AddWithValue("@OriginalId", (object?)request.OriginalPayrollRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PeriodStart", request.PeriodStart.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@PeriodEnd", request.PeriodEnd.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@PaymentDate", request.PaymentDate.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number is >= 51740 and <= 51744) { throw new PayrollValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627) { throw new PayrollConflictException("Ya existe una liquidación regular para ese período y periodicidad."); }
        return await GetRunAsync(user, request.PayrollRunId, ct);
    }

    public async Task<PayrollRunCalculationData> LoadCalculationDataAsync(PayrollUserIdentity user, Guid runId, CancellationToken ct,
        CalculatePayrollRunRequest? request = null)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        bool exempt; byte[] runVersion; byte[] settingsVersion;
        PayrollWorkPeriod period; bool partial;
        await using (var run = new SqlCommand("""
            SELECT r.PayrollRunId,r.PeriodStart,r.PeriodEnd,TRY_CONVERT(int,f.MetadataCode),r.RunKind,
                COALESCE(s.IsEmployerExemptFromHealthSenaIcbf,CAST(0 AS bit)),r.RowVersion,
                COALESCE(CONVERT(binary(8),s.RowVersion),CONVERT(binary(8),0x0)),r.Status,r.SelectionInitialized
            FROM payroll.Runs r JOIN payroll.CatalogOptions f ON f.OptionId=r.PayFrequencyOptionId
            LEFT JOIN payroll.Settings s ON s.TenantId=r.TenantId
            WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId
              AND r.Status IN(N'Draft',N'Calculated');
            """, connection))
        {
            run.Parameters.AddWithValue("@RunId",runId); run.Parameters.AddWithValue("@TenantId",user.TenantId);
            run.Parameters.AddWithValue("@BusinessId",user.BusinessId);
            await using var reader=await run.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))throw new PayrollConflictException("La liquidación no existe o ya no admite cálculo.");
            exempt=reader.GetBoolean(5); runVersion=(byte[])reader[6]; settingsVersion=(byte[])reader[7];
            if(request?.RowVersion is not null && !runVersion.SequenceEqual(request.RowVersion))
                throw new PayrollConflictException("La liquidación cambió. Actualízala antes de continuar.");
            try
            {
                period=PayrollWorkPeriod.Create(DateOnly.FromDateTime(reader.GetDateTime(1)),DateOnly.FromDateTime(reader.GetDateTime(2)),
                    reader.IsDBNull(3)?null:reader.GetInt32(3),reader.GetString(4)==PayrollRunKind.Adjustment.ToString());
            }
            catch(PayrollCalculationException error){throw new PayrollValidationException(error.Message);}
            partial=(request?.WorkedDaysChanges is {Count:>0} || request?.RecalculateEmploymentIds is {Count:>0})
                && request.SelectAll is null && request.SelectionChanges is not {Count:>0};
            if(partial && (reader.GetString(8)!=PayrollRunStatus.Calculated.ToString() || !reader.GetBoolean(9)))
                throw new PayrollValidationException("Liquida primero los trabajadores antes de recalcularlos en Novedades.");
        }
        var employees=new List<EmployeeSeed>();
        await using(var command=new SqlCommand("""
            SELECT COUNT(*) FROM OPENJSON(@ChangesJson) WITH(EmploymentId uniqueidentifier,IsSelected bit) change
            WHERE NOT EXISTS(SELECT 1 FROM payroll.EligibleRunEmployments(@TenantId,@BusinessId,@RunId) eligible
                WHERE eligible.EmploymentId=change.EmploymentId);
            SELECT TOP(@EmployeeLimit) e.EmploymentId,e.PartyId,e.MonthlySalary,e.StartDate,e.EndDate,st.Code,
                TRY_CONVERT(decimal(19,8),risk.MetadataCode),e.AppliesTransportAllowance,e.RowVersion,e.BaseWorkedDays,e.TransportBaseWorkedDays
            FROM payroll.EligibleRunEmployments(@TenantId,@BusinessId,@RunId) e
            JOIN payroll.CatalogOptions st ON st.OptionId=e.SalaryTypeOptionId
            JOIN payroll.CatalogOptions risk ON risk.OptionId=e.RiskClassOptionId
            LEFT JOIN OPENJSON(@ChangesJson) WITH(EmploymentId uniqueidentifier,IsSelected bit) change ON change.EmploymentId=e.EmploymentId
            WHERE COALESCE(change.IsSelected,@SelectAll,e.IsSelected)=1
              AND (@Partial=0 OR EXISTS(SELECT 1 FROM OPENJSON(@AffectedEmployees) WITH(EmploymentId uniqueidentifier) affected WHERE affected.EmploymentId=e.EmploymentId))
            ORDER BY e.EmploymentId;
            """,connection))
        {
            command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
            command.Parameters.AddWithValue("@RunId",runId);command.Parameters.AddWithValue("@EmployeeLimit",PayrollPaymentLimits.MaximumEmployees+1);
            command.Parameters.Add("@SelectAll",SqlDbType.Bit).Value=(object?)request?.SelectAll??DBNull.Value;
            command.Parameters.AddWithValue("@Partial",partial);
            command.Parameters.Add("@ChangesJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request?.SelectionChanges??[]);
            command.Parameters.Add("@AffectedEmployees",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(
                (request?.WorkedDaysChanges?.Select(change=>change.EmploymentId)??[])
                .Concat(request?.RecalculateEmploymentIds??[]).Distinct().Select(id=>new{EmploymentId=id}));
            await using var reader=await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            if(reader.GetInt32(0)!=0)throw new PayrollValidationException("La selección contiene un contrato que no es elegible para esta liquidación.");
            await reader.NextResultAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                if(partial && reader.IsDBNull(9))
                    throw new PayrollValidationException("Recalcula la liquidación completa para habilitar la edición de días.");
                var baseDays=reader.IsDBNull(9)?period.NominalDays:reader.GetDecimal(9);
                if(baseDays>period.NominalDays)throw new PayrollValidationException("Los días guardados superan los de la periodicidad. Revisa las entradas.");
                var start=DateOnly.FromDateTime(reader.GetDateTime(3));
                var end=reader.IsDBNull(4)?(DateOnly?)null:DateOnly.FromDateTime(reader.GetDateTime(4));
                employees.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetDecimal(2),baseDays,reader.GetString(5)=="Integral",
                    reader.IsDBNull(6)?throw new PayrollValidationException("La clase de riesgo no tiene tarifa configurada."):reader.GetDecimal(6),
                    reader.GetBoolean(7),(byte[])reader[8],period.MaximumContractDays(start,end),reader.IsDBNull(10)?baseDays:reader.GetDecimal(10)));
            }
        }
        if(employees.Count>PayrollPaymentLimits.MaximumEmployees)
            throw new PayrollValidationException("El cálculo admite hasta 500 trabajadores por comando.");
        var profileRules=await ReadProfileRulesAsync(connection,user,employees,period,ct);
        var employeeIds=JsonSerializer.Serialize(employees.Select(employee=>new{employee.EmploymentId}));
        var concepts=new Dictionary<Guid,PayrollConceptDefinition>();
        var conceptVersions=new Dictionary<Guid,byte[]>();
        var systemConcepts=new Dictionary<string,PayrollConceptDefinition>(StringComparer.Ordinal);
        var contextBytes=new List<byte>(settingsVersion);
        await using(var command=new SqlCommand("""
            WITH NeededConcepts AS(
                SELECT ConceptId FROM payroll.ApplicableCalculationConcepts(@TenantId,@BusinessId,@RunId) WHERE SystemRoleOptionId IS NOT NULL
                UNION
                SELECT ConceptId FROM payroll.CalculationNoveltyVersions(@TenantId,@BusinessId,@RunId,@EmployeeIds)
            )
            SELECT c.ConceptId,c.Code,c.Name,c.NatureCode,c.CalculationMethodCode,c.AccountingCategoryCode,c.DianConceptCode,c.SystemRoleCode,
                c.IsSalaryBase,c.IsSocialSecurityBase,c.IsBenefitsBase,c.IsTaxWithholdingBase,c.RequiresDeductionAgreement,c.RowVersion
            FROM NeededConcepts needed JOIN payroll.ApplicableCalculationConcepts(@TenantId,@BusinessId,@RunId) c ON c.ConceptId=needed.ConceptId
            ORDER BY c.ConceptId;
            """,connection))
        {
            command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
            command.Parameters.AddWithValue("@RunId",runId);command.Parameters.Add("@EmployeeIds",SqlDbType.NVarChar,-1).Value=employeeIds;
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                if(!Enum.TryParse<PayrollLineNature>(reader.GetString(3),false,out var nature))
                    throw new PayrollValidationException($"La naturaleza '{reader.GetString(3)}' no es compatible con el calculador.");
                var concept=new PayrollConceptDefinition(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),nature,reader.GetString(4),reader.GetString(5),
                    NullableString(reader,6),NullableString(reader,7),reader.GetBoolean(8),reader.GetBoolean(9),reader.GetBoolean(10),reader.GetBoolean(11),reader.GetBoolean(12));
                concepts.Add(concept.ConceptId,concept);conceptVersions.Add(concept.ConceptId,(byte[])reader[13]);
                if(concept.SystemRoleCode is not null)
                {
                    if(!systemConcepts.TryAdd(concept.SystemRoleCode,concept))
                        throw new PayrollValidationException($"Existe más de un concepto activo para '{concept.SystemRoleCode}'.");
                    contextBytes.AddRange((byte[])reader[13]);
                }
            }
        }
        var noveltyLookup=employees.ToDictionary(employee=>employee.EmploymentId,_=>new List<PayrollNoveltyInput>());
        var noveltyVersions=employees.ToDictionary(employee=>employee.EmploymentId,_=>new List<byte>());
        await using(var command=new SqlCommand("""
            SELECT n.NoveltyId,n.EmploymentId,n.ConceptId,t.Code,n.Quantity,n.UnitAmount,n.TotalAmount,n.DeductionAgreementId,
                CASE WHEN a.DeductionAgreementId IS NOT NULL AND a.IsActive=1 AND LEN(a.EvidenceUrl)>0
                    AND a.EffectiveFrom<=n.EndDate AND (a.EffectiveTo IS NULL OR a.EffectiveTo>=n.StartDate)
                    AND (a.AuthorizedTotal IS NULL OR a.DeductedToDate+n.TotalAmount<=a.AuthorizedTotal) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                auth.Code,a.MustProtectMinimumNetPay,source.NoveltyVersion,source.AgreementVersion,source.ConceptVersion
            FROM payroll.CalculationNoveltyVersions(@TenantId,@BusinessId,@RunId,@EmployeeIds) source
            JOIN payroll.Novelties n ON n.NoveltyId=source.NoveltyId AND n.RowVersion=source.NoveltyVersion
            JOIN payroll.CatalogOptions t ON t.OptionId=n.NoveltyTypeOptionId
            LEFT JOIN payroll.DeductionAgreements a ON a.DeductionAgreementId=n.DeductionAgreementId AND a.TenantId=n.TenantId AND a.EmploymentId=n.EmploymentId
            LEFT JOIN payroll.CatalogOptions auth ON auth.OptionId=a.AuthorityOptionId
            WHERE COALESCE(CONVERT(binary(8),a.RowVersion),CONVERT(binary(8),0x0))=source.AgreementVersion
            ORDER BY n.EmploymentId,n.NoveltyId;
            """,connection))
        {
            command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
            command.Parameters.AddWithValue("@RunId",runId);command.Parameters.Add("@EmployeeIds",SqlDbType.NVarChar,-1).Value=employeeIds;
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                var employmentId=reader.GetGuid(1);
                if(!concepts.TryGetValue(reader.GetGuid(2),out var concept))
                    throw new PayrollValidationException("Una novedad usa un concepto inactivo o fuera de vigencia.");
                if(!conceptVersions[concept.ConceptId].SequenceEqual((byte[])reader[13]))
                    throw new PayrollConflictException("Un concepto cambió durante el cálculo. Recalcula con su configuración actual.");
                var authority=NullableString(reader,9);
                noveltyLookup[employmentId].Add(new(reader.GetGuid(0),concept,reader.GetString(3),reader.GetDecimal(4),
                    reader.IsDBNull(5)?null:reader.GetDecimal(5),reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetGuid(7),reader.GetBoolean(8),
                    reader.IsDBNull(10)?authority is "WrittenAuthorization":reader.GetBoolean(10)));
                noveltyVersions[employmentId].AddRange((byte[])reader[11]);
                noveltyVersions[employmentId].AddRange((byte[])reader[12]);
                noveltyVersions[employmentId].AddRange((byte[])reader[13]);
            }
        }
        var inputs=employees.Select(employee=>new PayrollEmployeeCalculationInput(employee.EmploymentId,employee.PartyId,employee.MonthlySalary,employee.WorkedDays,
            employee.IsIntegral,exempt,employee.RiskRate,profileRules[employee.EmploymentId].Rules,systemConcepts,noveltyLookup[employee.EmploymentId],employee.AppliesTransportAllowance,employee.EmploymentVersion,
            employee.TransportBaseWorkedDays) {PeriodDays=period.NominalDays}).ToArray();
        return new(runId,inputs,runVersion)
        {
            PeriodDays=period.NominalDays,IsPartial=partial,ContextVersionHash=SHA256.HashData(contextBytes.ToArray()),
            Sources=employees.ToDictionary(employee=>employee.EmploymentId,employee=>new PayrollEmployeeCalculationSource(employee.MaximumContractDays,
                SHA256.HashData(noveltyVersions[employee.EmploymentId].ToArray()),profileRules[employee.EmploymentId].RuleSetId))
        };
    }

    public async Task<PayrollRunView> SaveCalculationAsync(PayrollUserIdentity user, PayrollRunCalculationData data,
        IReadOnlyList<PayrollEmployeeCalculation> calculations, CancellationToken ct)
    {
        var runId = data.PayrollRunId;
        var sourceInputs=data.Employees.ToDictionary(value=>value.EmploymentId);
        if(calculations.Count is 0 or > PayrollPaymentLimits.MaximumEmployees || data.ContextVersionHash.Length!=32 ||
            calculations.Any(value=>value.EmploymentVersion is not {Length:8} || !sourceInputs.ContainsKey(value.EmploymentId) ||
                !data.Sources.TryGetValue(value.EmploymentId,out var source) || source.NoveltyVersionHash.Length!=32))
            throw new PayrollValidationException("El cálculo debe incluir de 1 a 500 contratos con su versión de origen.");
        var targetIds=JsonSerializer.Serialize(calculations.Select(value=>new{value.EmploymentId}));
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var lockRun = new SqlCommand("""
                SELECT PayrollRunId FROM payroll.Runs WITH(UPDLOCK,HOLDLOCK)
                WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND Status IN(N'Draft',N'Calculated') AND RowVersion=@RowVersion;
                """, connection, tx))
            {
                lockRun.Parameters.AddWithValue("@RunId", runId); lockRun.Parameters.AddWithValue("@TenantId", user.TenantId); lockRun.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                lockRun.Parameters.Add("@RowVersion",SqlDbType.Binary,8).Value=data.RowVersion;
                if(await lockRun.ExecuteScalarAsync(ct) is not Guid)throw new PayrollConflictException("La liquidación ya no admite cálculo.");
            }
            var calculatedRows=calculations.Select(calculation=>new {
                Id=ids.NewId(),calculation.EmploymentId,calculation.PartyId,calculation.WorkedDays,
                calculation.Earnings,calculation.Deductions,calculation.EmployerContributions,
                calculation.Provisions,calculation.NetPayable,calculation.AppliesTransportAllowance,
                EmploymentVersion=calculation.EmploymentVersion!,
                BaseWorkedDays=sourceInputs[calculation.EmploymentId].WorkedDays,
                calculation.TransportBaseWorkedDays,calculation.ConceptWorkedDays,
                EffectiveRuleSetId=data.Sources[calculation.EmploymentId].RuleSetId,
                RuleSnapshot=JsonSerializer.Serialize(new SortedDictionary<string,decimal>(sourceInputs[calculation.EmploymentId].RuleParameters.ToDictionary(item=>item.Key,item=>item.Value),StringComparer.Ordinal)),
                data.Sources[calculation.EmploymentId].MaximumContractDays,
                sourceInputs[calculation.EmploymentId].RiskRate,
                data.Sources[calculation.EmploymentId].NoveltyVersionHash,
                Hash=calculation.CalculationHash,
                Lines=calculation.Lines.Select((line,index)=>new {
                    Id=ids.NewId(),Number=index+1,line.ConceptId,line.NoveltyId,line.DeductionAgreementId,
                    line.ConceptCode,line.ConceptName,Nature=line.Nature.ToString(),line.AccountingCategoryCode,
                    line.DianConceptCode,line.Quantity,line.Rate,line.BaseAmount,line.Amount,line.IsEmployerCost,line.IsSalaryBase
                }).ToArray()
            }).ToArray();
            using var employeeRows=new DataTable();
            employeeRows.Columns.Add("Id",typeof(Guid));employeeRows.Columns.Add("EmploymentId",typeof(Guid));
            employeeRows.Columns.Add("PartyId",typeof(Guid));employeeRows.Columns.Add("WorkedDays",typeof(decimal));
            employeeRows.Columns.Add("Earnings",typeof(decimal));employeeRows.Columns.Add("Deductions",typeof(decimal));
            employeeRows.Columns.Add("EmployerContributions",typeof(decimal));employeeRows.Columns.Add("Provisions",typeof(decimal));
            employeeRows.Columns.Add("NetPayable",typeof(decimal));employeeRows.Columns.Add("AppliesTransportAllowance",typeof(bool));
            employeeRows.Columns.Add("EmploymentVersion",typeof(byte[]));employeeRows.Columns.Add("Hash",typeof(byte[]));
            employeeRows.Columns.Add("BaseWorkedDays",typeof(decimal));employeeRows.Columns.Add("MaximumContractDays",typeof(decimal));
            employeeRows.Columns.Add("RiskRate",typeof(decimal));employeeRows.Columns.Add("NoveltyVersionHash",typeof(byte[]));
            using var lineRows=new DataTable();
            lineRows.Columns.Add("EmployeeId",typeof(Guid));lineRows.Columns.Add("Id",typeof(Guid));
            lineRows.Columns.Add("Number",typeof(int));lineRows.Columns.Add("ConceptId",typeof(Guid));
            lineRows.Columns.Add("NoveltyId",typeof(Guid));lineRows.Columns.Add("DeductionAgreementId",typeof(Guid));
            lineRows.Columns.Add("Quantity",typeof(decimal));lineRows.Columns.Add("Rate",typeof(decimal));
            lineRows.Columns.Add("BaseAmount",typeof(decimal));lineRows.Columns.Add("Amount",typeof(decimal));
            lineRows.Columns.Add("IsEmployerCost",typeof(bool));lineRows.Columns.Add("IsSalaryBase",typeof(bool));
            foreach(var row in calculatedRows)
            {
                employeeRows.Rows.Add(row.Id,row.EmploymentId,row.PartyId,row.WorkedDays,row.Earnings,row.Deductions,
                    row.EmployerContributions,row.Provisions,row.NetPayable,row.AppliesTransportAllowance,row.EmploymentVersion,row.Hash,
                    row.BaseWorkedDays,row.MaximumContractDays,row.RiskRate,row.NoveltyVersionHash);
                foreach(var line in row.Lines)
                    lineRows.Rows.Add(row.Id,line.Id,line.Number,line.ConceptId,(object?)line.NoveltyId??DBNull.Value,
                        (object?)line.DeductionAgreementId??DBNull.Value,line.Quantity,(object?)line.Rate??DBNull.Value,
                        (object?)line.BaseAmount??DBNull.Value,line.Amount,line.IsEmployerCost,line.IsSalaryBase);
            }
            var conceptRows=calculatedRows.SelectMany(row=>row.Lines).Select(line=>new {
                line.ConceptId,line.ConceptCode,line.ConceptName,line.Nature,line.AccountingCategoryCode,line.DianConceptCode
            }).Distinct();
            await using(var batch=new SqlCommand($"""
                DECLARE @DayInputs TABLE(EmploymentId uniqueidentifier PRIMARY KEY,TransportBaseWorkedDays decimal(9,4),ConceptWorkedDays nvarchar(max),EffectiveRuleSetId uniqueidentifier,RuleSnapshot nvarchar(max));
                INSERT @DayInputs SELECT EmploymentId,TransportBaseWorkedDays,ConceptWorkedDays,EffectiveRuleSetId,RuleSnapshot
                FROM OPENJSON(@DayInputsJson) WITH(EmploymentId uniqueidentifier,TransportBaseWorkedDays decimal(9,4),ConceptWorkedDays nvarchar(max) AS JSON,EffectiveRuleSetId uniqueidentifier,RuleSnapshot nvarchar(max));
                IF EXISTS(SELECT 1 FROM @EmployeeRows input JOIN @DayInputs days ON days.EmploymentId=input.EmploymentId
                  JOIN payroll.Employments e ON e.EmploymentId=input.EmploymentId AND e.TenantId=@TenantId AND e.BusinessId=@BusinessId
                  JOIN payroll.Runs run ON run.PayrollRunId=@RunId AND run.TenantId=@TenantId AND run.BusinessId=@BusinessId
                  CROSS APPLY payroll.ProfileRuleConfiguration(@TenantId,e.AccountingProfileId,run.PeriodStart,run.PeriodEnd) configuration
                  WHERE configuration.Issue IS NOT NULL OR configuration.RuleSetId<>days.EffectiveRuleSetId)
                  THROW 51750,N'El perfil de un contrato cambió durante el cálculo; recalcula con sus parámetros vigentes.',1;
                IF EXISTS(SELECT 1 FROM @EmployeeRows employee LEFT JOIN @DayInputs days ON days.EmploymentId=employee.EmploymentId WHERE days.EmploymentId IS NULL)
                    THROW 51750,N'Faltan las entradas de días de un trabajador calculado.',1;
                IF @Partial=1
                BEGIN
                    DELETE line FROM @EmployeeRows target
                    JOIN payroll.RunEmployees employee ON employee.PayrollRunId=@RunId AND employee.EmploymentId=target.EmploymentId
                    JOIN payroll.RunLines line ON line.PayrollRunEmployeeId=employee.PayrollRunEmployeeId
                    OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1,RECOMPILE);
                    DELETE employee FROM @EmployeeRows target JOIN payroll.RunEmployees employee
                      ON employee.PayrollRunId=@RunId AND employee.EmploymentId=target.EmploymentId
                    OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1,RECOMPILE);
                END
                ELSE
                BEGIN
                    DELETE line FROM payroll.RunEmployees employee JOIN payroll.RunLines line ON line.PayrollRunEmployeeId=employee.PayrollRunEmployeeId
                    WHERE employee.PayrollRunId=@RunId;
                    DELETE payroll.RunEmployees WHERE PayrollRunId=@RunId;
                END;
                DECLARE @CalculatedConcepts TABLE(ConceptId uniqueidentifier PRIMARY KEY,ConceptCode nvarchar(32),
                  ConceptName nvarchar(160),Nature nvarchar(32),AccountingCategoryCode nvarchar(64),DianConceptCode nvarchar(64));
                INSERT @CalculatedConcepts
                SELECT ConceptId,ConceptCode,ConceptName,Nature,AccountingCategoryCode,DianConceptCode
                FROM OPENJSON(@ConceptsJson) WITH(ConceptId uniqueidentifier,ConceptCode nvarchar(32),ConceptName nvarchar(160),
                  Nature nvarchar(32),AccountingCategoryCode nvarchar(64),DianConceptCode nvarchar(64));
                IF @Partial=0 UPDATE payroll.RunEmploymentInputs SET IsSelected=0 WHERE PayrollRunId=@RunId AND TenantId=@TenantId;
                UPDATE target SET IsSelected=1,BaseWorkedDays=input.BaseWorkedDays,TransportBaseWorkedDays=days.TransportBaseWorkedDays
                FROM payroll.RunEmploymentInputs target JOIN @EmployeeRows input ON input.EmploymentId=target.EmploymentId
                JOIN @DayInputs days ON days.EmploymentId=input.EmploymentId
                WHERE target.PayrollRunId=@RunId AND target.TenantId=@TenantId;
                INSERT payroll.RunEmploymentInputs(TenantId,PayrollRunId,EmploymentId,IsSelected,BaseWorkedDays,TransportBaseWorkedDays)
                SELECT @TenantId,@RunId,input.EmploymentId,1,input.BaseWorkedDays,days.TransportBaseWorkedDays FROM @EmployeeRows input
                JOIN @DayInputs days ON days.EmploymentId=input.EmploymentId
                WHERE NOT EXISTS(SELECT 1 FROM payroll.RunEmploymentInputs target WHERE target.PayrollRunId=@RunId AND target.EmploymentId=input.EmploymentId);
                INSERT payroll.RunEmployees(PayrollRunEmployeeId,TenantId,PayrollRunId,EmploymentId,PartyId,
                  EmployeeSnapshotJson,RuleSnapshotJson,WorkedDays,Earnings,Deductions,EmployerContributions,Provisions,NetPayable,CalculationHash)
                SELECT input.Id,@TenantId,@RunId,input.EmploymentId,input.PartyId,
                  (SELECT 1 AS SnapshotVersion,e.EmploymentId,e.PartyId,
                        COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador') AS EmployeeName,
                        COALESCE(p.Identification,N'') AS Identification,
                        COALESCE(idtype.DianCode,N'') AS IdentificationTypeCode,
                        COALESCE(p.FirstName,N'') AS FirstName,COALESCE(p.LastName,N'') AS FirstSurname,
                        e.ContractNumber,e.StartDate,e.EndDate,e.MonthlySalary,
                        input.AppliesTransportAllowance,CONVERT(varchar(16),CONVERT(varbinary(8),e.RowVersion),2) AS EmploymentVersion,
                        input.BaseWorkedDays,input.MaximumContractDays,@PeriodDays AS PeriodDays,input.RiskRate,
                        JSON_QUERY(days.ConceptWorkedDays) AS ConceptWorkedDays,
                        CONVERT(varchar(64),@ContextHash,2) AS ContextVersionHash,
                        CONVERT(varchar(64),input.NoveltyVersionHash,2) AS NoveltyVersionHash,
                        CONVERT(bit,CASE WHEN salary.Code=N'Integral' THEN 1 ELSE 0 END) AS IntegralSalary,
                        COALESCE(contract.DianCode,N'') AS ContractTypeCode,
                        COALESCE(worker.DianCode,N'') AS WorkerTypeCode,
                        COALESCE(subtype.DianCode,N'00') AS WorkerSubtypeCode,
                        COALESCE(payment.DianCode,N'') AS PaymentMethodCode,
                        COALESCE(frequency.DianCode,N'') AS PayrollPeriodCode,
                        bank.Label AS Bank,accountType.MetadataCode AS BankAccountType,e.BankAccountNumber,days.EffectiveRuleSetId
                        {EmploymentConfigurationProjection} AS Configuration
                    FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER),
                  days.RuleSnapshot,input.WorkedDays,input.Earnings,input.Deductions,input.EmployerContributions,input.Provisions,input.NetPayable,input.Hash
                FROM @EmployeeRows input
                JOIN @DayInputs days ON days.EmploymentId=input.EmploymentId
                JOIN payroll.Employments e ON e.EmploymentId=input.EmploymentId AND e.PartyId=input.PartyId
                  AND e.TenantId=@TenantId AND e.BusinessId=@BusinessId AND e.RowVersion=input.EmploymentVersion
                JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
                {EmploymentConfigurationJoins}
                  JOIN payroll.CatalogOptions salary ON salary.OptionId=e.SalaryTypeOptionId
                  JOIN payroll.CatalogOptions contract ON contract.OptionId=e.ContractTypeOptionId
                  JOIN payroll.CatalogOptions worker ON worker.OptionId=e.WorkerTypeOptionId
                  LEFT JOIN payroll.CatalogOptions subtype ON subtype.OptionId=e.WorkerSubtypeOptionId
                  JOIN payroll.CatalogOptions payment ON payment.OptionId=e.PaymentMethodOptionId
                  JOIN payroll.CatalogOptions frequency ON frequency.OptionId=e.PayFrequencyOptionId
                  LEFT JOIN payroll.CatalogOptions bank ON bank.OptionId=e.BankOptionId
                    AND bank.CatalogCode=N'payroll-bank'
                  LEFT JOIN payroll.CatalogOptions accountType ON accountType.OptionId=e.BankAccountTypeOptionId
                    AND accountType.CatalogCode=N'payroll-bank-account-type'
                  LEFT JOIN payroll.CatalogOptions idtype
                    ON idtype.CatalogCode=N'payroll-identification-type'
                   AND idtype.Code=p.IdentificationTypeCode AND idtype.IsActive=1
                OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1,RECOMPILE);
                IF @@ROWCOUNT<>@EmployeeCount THROW 51750,N'Un contrato cambió durante el cálculo. Recalcula antes de guardar.',1;
                INSERT payroll.RunLines(PayrollRunLineId,PayrollRunEmployeeId,ConceptId,NoveltyId,DeductionAgreementId,LineNumber,
                  NatureCode,ConceptCode,ConceptName,AccountingCategoryCode,DianConceptCode,Quantity,Rate,BaseAmount,Amount,IsEmployerCost,IsSalaryBase)
                SELECT line.Id,input.Id,line.ConceptId,line.NoveltyId,line.DeductionAgreementId,line.Number,concept.Nature,concept.ConceptCode,
                  concept.ConceptName,concept.AccountingCategoryCode,concept.DianConceptCode,line.Quantity,line.Rate,line.BaseAmount,line.Amount,line.IsEmployerCost,line.IsSalaryBase
                FROM @LineRows line JOIN @EmployeeRows input ON input.Id=line.EmployeeId
                JOIN @CalculatedConcepts concept ON concept.ConceptId=line.ConceptId OPTION(RECOMPILE);
                IF @@ROWCOUNT<>@LineCount THROW 51750,N'No se guardaron todas las partidas del cálculo.',1;
                """,connection,tx))
            {
                batch.Parameters.AddWithValue("@TenantId",user.TenantId);batch.Parameters.AddWithValue("@BusinessId",user.BusinessId);
                batch.Parameters.AddWithValue("@RunId",runId);
                batch.Parameters.AddWithValue("@Partial",data.IsPartial);batch.Parameters.AddWithValue("@PeriodDays",data.PeriodDays);
                batch.Parameters.Add("@ContextHash",SqlDbType.Binary,32).Value=data.ContextVersionHash;
                batch.Parameters.AddWithValue("@EmployeeCount",calculatedRows.Length);batch.Parameters.AddWithValue("@LineCount",calculations.Sum(value=>value.Lines.Count));
                var employees=batch.Parameters.Add("@EmployeeRows",SqlDbType.Structured);
                employees.TypeName="payroll.CalculationEmployeeInputV2";employees.Value=employeeRows;
                var lines=batch.Parameters.Add("@LineRows",SqlDbType.Structured);
                lines.TypeName="payroll.CalculationLineInput";lines.Value=lineRows;
                batch.Parameters.Add("@ConceptsJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(conceptRows);
                batch.Parameters.Add("@DayInputsJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(calculatedRows.Select(row=>new {
                    row.EmploymentId,row.TransportBaseWorkedDays,row.ConceptWorkedDays,row.EffectiveRuleSetId,row.RuleSnapshot
                }));
                await batch.ExecuteNonQueryAsync(ct);
            }
            await using var update = new SqlCommand("""
                UPDATE run SET Status=N'Calculated',CalculationVersion=CalculationVersion+1,SelectionInitialized=1,
                  InputHash=totals.InputHash,TotalEarnings=totals.Earnings,TotalDeductions=totals.Deductions,
                  TotalEmployerContributions=totals.Employer,TotalProvisions=totals.Provisions,NetPayable=totals.Net,CalculatedAt=@Now
                FROM payroll.Runs run CROSS APPLY(
                    SELECT SUM(Earnings) Earnings,SUM(Deductions) Deductions,SUM(EmployerContributions) Employer,
                      SUM(Provisions) Provisions,SUM(NetPayable) Net,
                      HASHBYTES('SHA2_256',CONVERT(varbinary(max),STRING_AGG(CONVERT(varchar(max),CalculationHash,2),'')
                        WITHIN GROUP(ORDER BY EmploymentId),2)) InputHash
                    FROM payroll.RunEmployees WHERE PayrollRunId=run.PayrollRunId AND TenantId=run.TenantId
                ) totals
                WHERE run.PayrollRunId=@RunId AND run.TenantId=@TenantId AND run.BusinessId=@BusinessId AND run.Status IN(N'Draft',N'Calculated');
                IF @@ROWCOUNT<>1 THROW 51750,N'La liquidación cambió antes de guardar el cálculo.',1;
                """, connection, tx);
            update.Parameters.AddWithValue("@RunId", runId); update.Parameters.AddWithValue("@TenantId", user.TenantId); update.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            update.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
            await update.ExecuteNonQueryAsync(ct);
            var result=await ReadRunAsync(connection,tx,user,runId,ct,data.IsPartial?targetIds:null);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (SqlException error) when (error.Number == 51750) { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<PayrollRunAcceptance> ApproveRunAsync(PayrollUserIdentity user, Guid runId,
        string idempotencyKey, byte[] rowVersion, CancellationToken ct)
    {
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:D}|{idempotencyKey}"));
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            string kind; string status; string? existingKey; byte[]? existingHash; DateOnly start; DateOnly end; DateOnly payment; bool signalPublished; TimeZoneInfo zone;
            await using (var run = new SqlCommand("""
                -- The tenant is the legal employer, including its other sites
                -- and contracts. Serialize acceptance before locking a run so
                -- competing drafts cannot each approve the same worked period.
                -- This adds no round trip and takes no lock during calculation.
                DECLARE @Employer uniqueidentifier=(SELECT TenantId FROM dbo.Tenants WITH(UPDLOCK,HOLDLOCK)
                  WHERE TenantId=@TenantId);
                SELECT RunKind,Status,ApprovalIdempotencyKey,ApprovalRequestHash,PeriodStart,PeriodEnd,PaymentDate,
                  CAST(CASE WHEN EXISTS(SELECT 1 FROM payroll.OutboxMessages signal
                    WHERE signal.AggregateId=@RunId AND signal.TenantId=@TenantId AND signal.BusinessId=@BusinessId
                      AND signal.MessageType=N'AccountingPostingRequested' AND signal.PublishedAt IS NOT NULL)
                    THEN 1 ELSE 0 END AS bit),
                  (SELECT TimeZone FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                FROM payroll.Runs WITH(UPDLOCK,HOLDLOCK)
                WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId;
                """, connection, tx))
            {
                run.Parameters.AddWithValue("@RunId", runId); run.Parameters.AddWithValue("@TenantId", user.TenantId); run.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await run.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La liquidación no existe.");
                kind = reader.GetString(0); status = reader.GetString(1); existingKey = NullableString(reader, 2); existingHash = reader.IsDBNull(3) ? null : (byte[])reader[3];
                start = DateOnly.FromDateTime(reader.GetDateTime(4)); end = DateOnly.FromDateTime(reader.GetDateTime(5)); payment = DateOnly.FromDateTime(reader.GetDateTime(6));
                signalPublished=reader.GetBoolean(7);
                zone=TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(8));
            }
            var documentType = kind == PayrollRunKind.Adjustment.ToString() ? PayrollAccountingDocumentTypes.Adjustment : PayrollAccountingDocumentTypes.Accrual;
            if (status == PayrollRunStatus.Approved.ToString())
            {
                if (existingKey != idempotencyKey || existingHash is null || !existingHash.AsSpan().SequenceEqual(requestHash))
                    throw new PayrollConflictException("La liquidación ya fue aprobada con otra operación.");
                var existingJob = await FindAccountingJobAsync(connection, tx, runId, documentType, ct);
                await tx.CommitAsync(ct); return new(runId, status, kind, existingJob, true) {AccountingSignalPublished=signalPublished};
            }
            if (status != PayrollRunStatus.Calculated.ToString()) throw new PayrollConflictException("La liquidación debe estar calculada antes de aprobar.");

            var accountingLines = await BuildAccountingLinesAsync(connection, tx, runId, ct);
            var description = $"Nómina {start:yyyy-MM-dd} a {end:yyyy-MM-dd}";
            var payload = new PayrollAccountingPayload(user.TenantId, user.BusinessId, runId, kind,
                start, end, payment, description, accountingLines.Lines){Employees=accountingLines.Employees,Concepts=accountingLines.Concepts};
            var json = PayrollContractSerializer.Serialize(payload); var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(json)); var jobId = ids.NewId(); var now = timeProvider.GetUtcNow();
            await using var approve = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM payroll.RunEmployees result
                  JOIN payroll.Employments employment WITH(UPDLOCK,HOLDLOCK) ON employment.EmploymentId=result.EmploymentId
                    AND employment.TenantId=@TenantId AND employment.BusinessId=@BusinessId
                  WHERE result.PayrollRunId=@RunId AND (
                    JSON_VALUE(result.EmployeeSnapshotJson,'$.EmploymentVersion') IS NULL OR
                    TRY_CONVERT(binary(8),JSON_VALUE(result.EmployeeSnapshotJson,'$.EmploymentVersion'),2) IS NULL OR
                    employment.RowVersion<>TRY_CONVERT(binary(8),JSON_VALUE(result.EmployeeSnapshotJson,'$.EmploymentVersion'),2)))
                  THROW 51764,N'Un contrato cambió o el cálculo no conserva su versión. Recalcula antes de contabilizar.',1;
                -- The version guard above also rejects a non-object snapshot.
                -- Parse its remaining scalar evidence once, preserving JSON_VALUE's
                -- lax 4,000-character limit before the existing TRY_CONVERT guards.
                DECLARE @Snapshots TABLE(EmploymentId uniqueidentifier PRIMARY KEY,EffectiveRuleSetId uniqueidentifier,
                  SnapshotVersion int,ContextHash binary(32),NoveltyHash binary(32),BaseDays decimal(9,4),
                  MaximumDays decimal(9,4),RiskRate decimal(19,8),PeriodDays int,EmployeeSnapshotJson nvarchar(max));
                INSERT @Snapshots
                SELECT result.EmploymentId,
                  TRY_CONVERT(uniqueidentifier,CASE WHEN DATALENGTH(snapshot.EffectiveRuleSetId)<=8000 THEN snapshot.EffectiveRuleSetId END),
                  TRY_CONVERT(int,CASE WHEN DATALENGTH(snapshot.SnapshotVersion)<=8000 THEN snapshot.SnapshotVersion END),
                  TRY_CONVERT(binary(32),CASE WHEN DATALENGTH(snapshot.ContextHash)<=8000 THEN snapshot.ContextHash END,2),
                  TRY_CONVERT(binary(32),CASE WHEN DATALENGTH(snapshot.NoveltyHash)<=8000 THEN snapshot.NoveltyHash END,2),
                  TRY_CONVERT(decimal(9,4),CASE WHEN DATALENGTH(snapshot.BaseDays)<=8000 THEN snapshot.BaseDays END),
                  TRY_CONVERT(decimal(9,4),CASE WHEN DATALENGTH(snapshot.MaximumDays)<=8000 THEN snapshot.MaximumDays END),
                  TRY_CONVERT(decimal(19,8),CASE WHEN DATALENGTH(snapshot.RiskRate)<=8000 THEN snapshot.RiskRate END),
                  TRY_CONVERT(int,CASE WHEN DATALENGTH(snapshot.PeriodDays)<=8000 THEN snapshot.PeriodDays END),result.EmployeeSnapshotJson
                FROM payroll.RunEmployees result
                CROSS APPLY OPENJSON(result.EmployeeSnapshotJson) WITH(
                  EffectiveRuleSetId nvarchar(max),SnapshotVersion nvarchar(max),ContextHash nvarchar(max) '$.ContextVersionHash',
                  NoveltyHash nvarchar(max) '$.NoveltyVersionHash',BaseDays nvarchar(max) '$.BaseWorkedDays',
                  MaximumDays nvarchar(max) '$.MaximumContractDays',RiskRate nvarchar(max),PeriodDays nvarchar(max)) snapshot
                WHERE result.PayrollRunId=@RunId;
                -- A calculated draft must not consume a novelty already approved in
                -- another run, or a deduction whose authority changed after calculation.
                IF EXISTS(
                  SELECT 1 FROM payroll.RunLines l
                  JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
                  JOIN payroll.Novelties n WITH(UPDLOCK,HOLDLOCK) ON n.NoveltyId=l.NoveltyId
                  WHERE e.PayrollRunId=@RunId AND n.Status<>N'Approved')
                  THROW 51761,N'Una novedad ya fue liquidada. Recalcula antes de aprobar.',1;
                IF @Kind=N'Regular' AND (
                  EXISTS(SELECT PartyId FROM payroll.RunEmployees WHERE PayrollRunId=@RunId
                    GROUP BY PartyId HAVING COUNT(*)>1)
                  OR EXISTS(SELECT 1 FROM payroll.RunEmployees currentEmployee
                    JOIN payroll.RunEmployees previousEmployee
                      ON previousEmployee.TenantId=@TenantId AND previousEmployee.PartyId=currentEmployee.PartyId
                    JOIN payroll.Runs previousRun ON previousRun.PayrollRunId=previousEmployee.PayrollRunId
                      AND previousRun.TenantId=@TenantId AND previousRun.RunKind=N'Regular' AND previousRun.Status=N'Approved'
                    WHERE currentEmployee.PayrollRunId=@RunId AND previousRun.PayrollRunId<>@RunId
                      AND previousRun.PeriodStart<=@PeriodEnd AND previousRun.PeriodEnd>=@PeriodStart))
                  THROW 51767,N'El trabajador ya tiene una liquidación regular para fechas superpuestas. Revisa los contratos y el período.',1;
                IF EXISTS(
                  SELECT 1 FROM payroll.RunLines l
                  JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
                  JOIN payroll.Novelties n ON n.NoveltyId=l.NoveltyId
                  JOIN payroll.DeductionAgreements a WITH(UPDLOCK,HOLDLOCK)
                    ON a.DeductionAgreementId=l.DeductionAgreementId
                  WHERE e.PayrollRunId=@RunId AND
                    (a.TenantId<>@TenantId OR a.EmploymentId<>e.EmploymentId
                     OR a.ConceptId<>l.ConceptId OR a.IsActive=0
                     OR NULLIF(LTRIM(RTRIM(a.EvidenceUrl)),N'') IS NULL
                     OR a.EffectiveFrom>n.StartDate OR a.EffectiveTo<n.EndDate))
                  THROW 51762,N'La autorización de un descuento cambió. Revisa el acuerdo y recalcula.',1;
                IF EXISTS(
                  SELECT 1 FROM payroll.DeductionAgreements a WITH(UPDLOCK,HOLDLOCK)
                  JOIN (SELECT l.DeductionAgreementId,SUM(l.Amount) Amount
                    FROM payroll.RunLines l
                    JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
                    WHERE e.PayrollRunId=@RunId AND l.DeductionAgreementId IS NOT NULL
                    GROUP BY l.DeductionAgreementId) deductions
                    ON deductions.DeductionAgreementId=a.DeductionAgreementId
                  WHERE a.AuthorizedTotal IS NOT NULL
                    AND a.DeductedToDate+deductions.Amount>a.AuthorizedTotal)
                  THROW 51763,N'Los descuentos superan el saldo autorizado. Revisa el acuerdo y recalcula.',1;

                IF (SELECT COUNT(*) FROM payroll.RunEmployees WHERE PayrollRunId=@RunId)>500
                  THROW 51765,N'Recalcula y selecciona hasta 500 trabajadores antes de contabilizar.',1;
                DECLARE @ProfileRules TABLE(ProfileId uniqueidentifier PRIMARY KEY,RuleSetId uniqueidentifier,Issue nvarchar(40));
                INSERT @ProfileRules(ProfileId)
                SELECT DISTINCT employment.AccountingProfileId FROM @Snapshots result
                  JOIN payroll.Employments employment ON employment.EmploymentId=result.EmploymentId
                    AND employment.TenantId=@TenantId AND employment.BusinessId=@BusinessId
                  WHERE employment.AccountingProfileId IS NOT NULL;
                UPDATE profiles SET RuleSetId=configuration.RuleSetId,Issue=configuration.Issue
                FROM @ProfileRules profiles
                CROSS APPLY payroll.ProfileRuleConfiguration(@TenantId,profiles.ProfileId,@PeriodStart,@PeriodEnd) configuration;
                IF EXISTS(SELECT 1 FROM @Snapshots result
                  JOIN payroll.Employments employment ON employment.EmploymentId=result.EmploymentId
                    AND employment.TenantId=@TenantId AND employment.BusinessId=@BusinessId
                  LEFT JOIN @ProfileRules configuration ON configuration.ProfileId=employment.AccountingProfileId
                  WHERE configuration.ProfileId IS NULL OR configuration.Issue IS NOT NULL
                    OR result.EffectiveRuleSetId IS NULL OR result.EffectiveRuleSetId<>configuration.RuleSetId)
                  THROW 51765,N'La configuración de un contrato cambió o perdió vigencia; recalcula antes de aprobar.',1;
                DECLARE @ContextHash binary(32)=(
                    SELECT HASHBYTES('SHA2_256',CONVERT(varbinary(max),STRING_AGG(CONVERT(varchar(max),SourceVersion,2),'')
                        WITHIN GROUP(ORDER BY SourceKind,SourceId),2))
                    FROM payroll.CalculationContextVersions(@TenantId,@BusinessId,@RunId));
                DECLARE @EmployeeIds nvarchar(max)=(SELECT EmploymentId FROM payroll.RunEmployees WHERE PayrollRunId=@RunId FOR JSON PATH);
                DECLARE @NoveltyVersions TABLE(EmploymentId uniqueidentifier PRIMARY KEY,VersionHash binary(32));
                INSERT @NoveltyVersions
                SELECT EmploymentId,HASHBYTES('SHA2_256',CONVERT(varbinary(max),
                    STRING_AGG(CONCAT(CONVERT(varchar(max),NoveltyVersion,2),CONVERT(varchar(16),AgreementVersion,2),CONVERT(varchar(16),ConceptVersion,2)),'')
                    WITHIN GROUP(ORDER BY NoveltyId),2))
                FROM payroll.CalculationNoveltyVersions(@TenantId,@BusinessId,@RunId,@EmployeeIds)
                GROUP BY EmploymentId;
                IF EXISTS(
                    SELECT 1 FROM @Snapshots result LEFT JOIN @NoveltyVersions currentNovelty ON currentNovelty.EmploymentId=result.EmploymentId
                    WHERE result.SnapshotVersion IS NULL OR result.SnapshotVersion<>1 OR result.ContextHash IS NULL OR result.ContextHash<>@ContextHash OR
                        result.NoveltyHash IS NULL OR result.NoveltyHash<>
                            COALESCE(currentNovelty.VersionHash,HASHBYTES('SHA2_256',CONVERT(varbinary(max),0x))) OR
                        result.BaseDays IS NULL OR result.MaximumDays IS NULL)
                    THROW 51765,N'Las novedades o la configuración cambiaron, o falta su huella. Recalcula antes de contabilizar.',1;
                IF EXISTS(SELECT 1 FROM @Snapshots result WHERE result.BaseDays>result.MaximumDays OR
                    EXISTS(SELECT 1 FROM OPENJSON(result.EmployeeSnapshotJson,'$.ConceptWorkedDays') WITH(BaseDays decimal(9,4)) days
                      WHERE days.BaseDays>result.MaximumDays))
                    THROW 51766,N'Hay días fuera de la vigencia contractual. Corrígelos en Novedades antes de contabilizar.',1;
                IF EXISTS(
                    SELECT 1 FROM @Snapshots result
                    JOIN payroll.Employments employment ON employment.EmploymentId=result.EmploymentId AND employment.TenantId=@TenantId
                    JOIN payroll.CatalogOptions risk ON risk.OptionId=employment.RiskClassOptionId
                    JOIN payroll.Runs run ON run.PayrollRunId=@RunId
                    JOIN payroll.CatalogOptions frequency ON frequency.OptionId=run.PayFrequencyOptionId
                    WHERE (
                        result.RiskRate IS NULL OR
                        TRY_CONVERT(decimal(19,8),risk.MetadataCode) IS NULL OR
                        result.RiskRate<>TRY_CONVERT(decimal(19,8),risk.MetadataCode) OR
                        result.PeriodDays IS NULL OR
                        TRY_CONVERT(int,frequency.MetadataCode) IS NULL OR
                        result.PeriodDays<>CASE WHEN run.RunKind=N'Adjustment' THEN 0 ELSE TRY_CONVERT(int,frequency.MetadataCode) END))
                    THROW 51765,N'La periodicidad o tarifa de riesgo cambió. Recalcula antes de contabilizar.',1;

                UPDATE payroll.Runs SET Status=N'Approved',ApprovedBy=@UserId,ApprovedAt=@Now,
                  ApprovalIdempotencyKey=@Key,ApprovalRequestHash=@RequestHash
                WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND Status=N'Calculated' AND RowVersion=@Version;
                IF @@ROWCOUNT<>1 THROW 51760,N'La liquidación cambió antes de aprobar.',1;

                UPDATE n SET Status=N'Consumed'
                FROM payroll.Novelties n JOIN payroll.RunLines l ON l.NoveltyId=n.NoveltyId
                JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
                WHERE e.PayrollRunId=@RunId AND n.Status=N'Approved';

                UPDATE a SET DeductedToDate=a.DeductedToDate+l.Amount,UpdatedAt=@Now
                FROM payroll.DeductionAgreements a JOIN (
                  SELECT l.DeductionAgreementId,SUM(l.Amount) Amount FROM payroll.RunLines l
                  JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
                  WHERE e.PayrollRunId=@RunId AND l.DeductionAgreementId IS NOT NULL GROUP BY l.DeductionAgreementId
                ) l ON l.DeductionAgreementId=a.DeductionAgreementId;

                DECLARE @Sources nvarchar(max)=(SELECT @RunId DocumentId,@DocumentType DocumentType,
                  @BusinessId BusinessId,@Payload Payload,CONVERT(varchar(64),@PayloadHash,2) Hash,
                  @OccurredAt OccurredAt,@JobId JobId FOR JSON PATH);
                EXEC dbo.AccountingSourcesAccept @TenantId,@Sources,@Now,1;
                INSERT payroll.OutboxMessages(OutboxMessageId,TenantId,BusinessId,AggregateId,MessageType,
                  PayloadJson,OccurredAt,AttemptCount)
                VALUES(@OutboxId,@TenantId,@BusinessId,@RunId,N'AccountingPostingRequested',@Notification,@Now,0);
                """, connection, tx);
            approve.Parameters.AddWithValue("@RunId", runId); approve.Parameters.AddWithValue("@TenantId", user.TenantId); approve.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            approve.Parameters.AddWithValue("@Kind",kind);
            approve.Parameters.AddWithValue("@PeriodStart",start.ToDateTime(TimeOnly.MinValue));
            approve.Parameters.AddWithValue("@PeriodEnd",end.ToDateTime(TimeOnly.MinValue));
            approve.Parameters.AddWithValue("@UserId", user.UserId); approve.Parameters.AddWithValue("@Now", now); approve.Parameters.AddWithValue("@Key", idempotencyKey);
            approve.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash; approve.Parameters.Add("@Version", SqlDbType.Timestamp).Value = rowVersion;
            // The outbox requests existing work; the immutable financial body has
            // one owner in AccountingSourceDocuments, not a second outbox copy.
            approve.Parameters.Add("@Notification",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(new{
                DocumentId=runId,DocumentType=documentType,BusinessId=user.BusinessId},Json);
            approve.Parameters.AddWithValue("@DocumentType", documentType); approve.Parameters.AddWithValue("@Payload", json); approve.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
            // Accrual recognizes the worked period, independently of the planned
            // payment. Preserve its civil accounting date in the site's zone.
            var accruedOn=kind==PayrollRunKind.Regular.ToString()?end:payment;
            approve.Parameters.AddWithValue("@OccurredAt", BusinessCalendar.AtNoon(accruedOn,zone)); approve.Parameters.AddWithValue("@JobId", jobId); approve.Parameters.AddWithValue("@OutboxId", ids.NewId());
            await approve.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return new(runId, PayrollRunStatus.Approved.ToString(), kind, jobId, false);
        }
        catch (Exception error) when (error is PayrollConflictException or PayrollNotFoundException)
        { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number is >= 51760 and <= 51767) { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch { if(tx.Connection is not null)await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<ElectronicPayrollPeriodView> GenerateElectronicPeriodAsync(
        PayrollUserIdentity user,
        GenerateElectronicPayrollPeriodRequest request,
        CancellationToken ct)
    {
        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            Guid? preparedPeriodId = null;
            await using (var prepared = new SqlCommand("""
                SELECT TOP(1) period.ElectronicPeriodId
                FROM payroll.ElectronicPeriods period WITH(UPDLOCK,HOLDLOCK)
                WHERE period.TenantId=@TenantId AND period.BusinessId=@BusinessId
                  AND period.[Year]=@Year AND period.[Month]=@Month
                  AND EXISTS(SELECT 1 FROM payroll.ElectronicDocuments document
                    WHERE document.ElectronicPeriodId=period.ElectronicPeriodId
                      AND document.FiscalDocumentId IS NOT NULL);
                """, connection, tx))
            {
                prepared.Parameters.AddWithValue("@TenantId", user.TenantId);
                prepared.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                prepared.Parameters.AddWithValue("@Year", request.Year);
                prepared.Parameters.AddWithValue("@Month", request.Month);
                var value = await prepared.ExecuteScalarAsync(ct);
                preparedPeriodId = value is null or DBNull ? null : (Guid)value;
            }
            if (preparedPeriodId is not null)
            {
                await tx.CommitAsync(ct);
                return await ReadElectronicPeriodAsync(user, preparedPeriodId.Value, ct);
            }

            await using (var guard = new SqlCommand("""
                IF NOT EXISTS(
                  SELECT 1 FROM dbo.Businesses b
                  JOIN payroll.Settings s ON s.TenantId=b.TenantId
                  WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId
                    AND s.ElectronicPayrollEnabled=1)
                  THROW 51770,N'La nómina electrónica no está habilitada para esta entidad.',1;

                DECLARE @ExistingId uniqueidentifier,@ExistingStatus nvarchar(24);
                SELECT @ExistingId=ElectronicPeriodId,@ExistingStatus=Status
                FROM payroll.ElectronicPeriods WITH(UPDLOCK,HOLDLOCK)
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND [Year]=@Year AND [Month]=@Month;
                IF @ExistingId IS NOT NULL AND @ExistingId<>@PeriodId
                  THROW 51771,N'El período electrónico ya existe con otra identidad.',1;
                IF @ExistingStatus IN(N'Submitted',N'Closed')
                  THROW 51772,N'El período electrónico ya fue enviado o cerrado.',1;
                IF EXISTS(
                  SELECT 1 FROM payroll.ElectronicDocuments
                  WHERE ElectronicPeriodId=@ExistingId
                    AND (Status<>N'Draft' OR FiscalDocumentId IS NOT NULL))
                  THROW 51772,N'El período electrónico ya inició su procesamiento fiscal.',1;
                """, connection, tx))
            {
                guard.Parameters.AddWithValue("@TenantId", user.TenantId);
                guard.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                guard.Parameters.AddWithValue("@Year", request.Year);
                guard.Parameters.AddWithValue("@Month", request.Month);
                guard.Parameters.AddWithValue("@PeriodId", request.ElectronicPeriodId);
                await guard.ExecuteNonQueryAsync(ct);
            }

            var employees = new Dictionary<Guid, ElectronicEmployeeSnapshotSeed>();
            var lines = new Dictionary<Guid, List<ElectronicPayrollSnapshotLine>>();
            await using (var source = new SqlCommand("""
                DECLARE @FiscalEmployees TABLE(PayrollRunEmployeeId uniqueidentifier PRIMARY KEY);
                INSERT @FiscalEmployees
                SELECT re.PayrollRunEmployeeId FROM payroll.Runs r
                JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId AND re.TenantId=r.TenantId
                WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                  AND r.Status=N'Approved' AND r.PeriodStart>=@Start AND r.PeriodEnd<=@End
                  AND (re.Earnings>0 OR re.Deductions>0 OR re.EmployerContributions>0 OR re.Provisions>0);

                SELECT re.PartyId,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.EmployeeName ELSE COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador') END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.Identification ELSE COALESCE(p.Identification,N'') END,r.PayrollRunId,re.EmploymentId,
                       r.PaymentDate,re.WorkedDays,re.Earnings,re.Deductions,
                       re.EmployerContributions,re.Provisions,re.NetPayable,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.IdentificationTypeCode ELSE COALESCE(idtype.DianCode,N'') END,CASE WHEN snap.SnapshotVersion=1 THEN snap.FirstName ELSE COALESCE(p.FirstName,N'') END,CASE WHEN snap.SnapshotVersion=1 THEN snap.FirstSurname ELSE COALESCE(p.LastName,N'') END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.ContractNumber ELSE e.ContractNumber END,CASE WHEN snap.SnapshotVersion=1 THEN snap.StartDate ELSE e.StartDate END,CASE WHEN snap.SnapshotVersion=1 THEN snap.EndDate ELSE e.EndDate END,CASE WHEN snap.SnapshotVersion=1 THEN snap.MonthlySalary ELSE e.MonthlySalary END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.IntegralSalary ELSE CONVERT(bit,CASE WHEN salary.Code=N'Integral' THEN 1 ELSE 0 END) END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.ContractTypeCode ELSE COALESCE(contract.DianCode,N'') END,CASE WHEN snap.SnapshotVersion=1 THEN snap.WorkerTypeCode ELSE COALESCE(worker.DianCode,N'') END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.WorkerSubtypeCode ELSE COALESCE(subtype.DianCode,N'00') END,CASE WHEN snap.SnapshotVersion=1 THEN snap.PaymentMethodCode ELSE COALESCE(payment.DianCode,N'') END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.PayrollPeriodCode ELSE COALESCE(frequency.DianCode,N'') END,CASE WHEN snap.SnapshotVersion=1 THEN snap.Bank ELSE bank.Label END,CASE WHEN snap.SnapshotVersion=1 THEN snap.BankAccountType ELSE accountType.MetadataCode END,
                       CASE WHEN snap.SnapshotVersion=1 THEN snap.BankAccountNumber ELSE e.BankAccountNumber END,snap.SnapshotVersion
                FROM payroll.Runs r
                JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
                JOIN @FiscalEmployees selected ON selected.PayrollRunEmployeeId=re.PayrollRunEmployeeId
                JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=re.TenantId
                JOIN payroll.Employments e ON e.EmploymentId=re.EmploymentId AND e.TenantId=re.TenantId
                JOIN payroll.CatalogOptions salary ON salary.OptionId=e.SalaryTypeOptionId
                JOIN payroll.CatalogOptions contract ON contract.OptionId=e.ContractTypeOptionId
                JOIN payroll.CatalogOptions worker ON worker.OptionId=e.WorkerTypeOptionId
                LEFT JOIN payroll.CatalogOptions subtype ON subtype.OptionId=e.WorkerSubtypeOptionId
                JOIN payroll.CatalogOptions payment ON payment.OptionId=e.PaymentMethodOptionId
                JOIN payroll.CatalogOptions frequency ON frequency.OptionId=e.PayFrequencyOptionId
                LEFT JOIN payroll.CatalogOptions bank ON bank.OptionId=e.BankOptionId
                  AND bank.CatalogCode=N'payroll-bank'
                LEFT JOIN payroll.CatalogOptions accountType ON accountType.OptionId=e.BankAccountTypeOptionId
                  AND accountType.CatalogCode=N'payroll-bank-account-type'
                LEFT JOIN payroll.CatalogOptions idtype
                  ON idtype.CatalogCode=N'payroll-identification-type'
                 AND idtype.Code=p.IdentificationTypeCode AND idtype.IsActive=1
                OUTER APPLY OPENJSON(re.EmployeeSnapshotJson) WITH(
                  SnapshotVersion int,EmployeeName nvarchar(500),Identification nvarchar(100),
                  IdentificationTypeCode nvarchar(20),FirstName nvarchar(200),FirstSurname nvarchar(200),
                  ContractNumber nvarchar(100),StartDate date,EndDate date,MonthlySalary decimal(19,4),
                  IntegralSalary bit,ContractTypeCode nvarchar(20),WorkerTypeCode nvarchar(20),
                  WorkerSubtypeCode nvarchar(20),PaymentMethodCode nvarchar(20),PayrollPeriodCode nvarchar(20),
                  Bank nvarchar(300),BankAccountType nvarchar(100),BankAccountNumber nvarchar(100)) snap
                WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                  AND r.Status=N'Approved' AND r.PeriodStart>=@Start AND r.PeriodEnd<=@End
                ORDER BY re.PartyId,r.PaymentDate,r.PayrollRunId;

                SELECT re.PartyId,r.PayrollRunId,re.EmploymentId,l.ConceptId,l.ConceptCode,
                       l.ConceptName,l.NatureCode,l.DianConceptCode,l.Quantity,l.Rate,
                       l.BaseAmount,l.Amount,l.IsEmployerCost,l.IsSalaryBase
                FROM payroll.Runs r
                JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
                JOIN @FiscalEmployees selected ON selected.PayrollRunEmployeeId=re.PayrollRunEmployeeId
                JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
                WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                  AND r.Status=N'Approved' AND r.PeriodStart>=@Start AND r.PeriodEnd<=@End
                  AND l.Amount>0
                ORDER BY re.PartyId,r.PayrollRunId,l.LineNumber;
                """, connection, tx))
            {
                source.Parameters.AddWithValue("@TenantId", user.TenantId);
                source.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                source.Parameters.AddWithValue("@Start", periodStart.ToDateTime(TimeOnly.MinValue));
                source.Parameters.AddWithValue("@End", periodEnd.ToDateTime(TimeOnly.MinValue));
                await using var reader = await source.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var partyId = reader.GetGuid(0);
                    if (reader.IsDBNull(28))
                        logger.LogWarning("PayrollLegacyEmployeeSnapshot: tenant {TenantId}, business {BusinessId}, run {PayrollRunId} has no historical employee data; the legacy consolidation uses current master data.",
                            user.TenantId,user.BusinessId,reader.GetGuid(3));
                    else if (reader.GetInt32(28) != 1)
                        throw new PayrollValidationException("La versión del snapshot laboral no es compatible con la consolidación electrónica.");
                    if (!employees.TryGetValue(partyId, out var employee))
                    {
                        employee = new ElectronicEmployeeSnapshotSeed(
                            partyId, reader.GetString(1).Trim(), reader.GetString(2),
                            reader.GetString(12), reader.GetString(13), reader.GetString(14),
                            reader.GetGuid(4), reader.GetString(15),
                            DateOnly.FromDateTime(reader.GetDateTime(16)),
                            reader.IsDBNull(17) ? null : DateOnly.FromDateTime(reader.GetDateTime(17)),
                            reader.GetDecimal(18), reader.GetBoolean(19), reader.GetString(20),
                            reader.GetString(21), reader.GetString(22), reader.GetString(23),
                            reader.GetString(24), NullableString(reader, 25),
                            NullableString(reader, 26), NullableString(reader, 27),
                            [], [], 0, 0, 0, 0, 0, 0);
                        employees.Add(partyId, employee);
                        lines.Add(partyId, []);
                    }
                    else if (employee.EmploymentId != reader.GetGuid(4))
                        throw new PayrollValidationException(
                            "Un trabajador tiene más de una relación laboral en el mismo período electrónico.");
                    employee.RunIds.Add(reader.GetGuid(3));
                    employee.PaymentDates.Add(DateOnly.FromDateTime(reader.GetDateTime(5)));
                    employee.WorkedDays += reader.GetDecimal(6);
                    employee.Earnings += reader.GetDecimal(7);
                    employee.Deductions += reader.GetDecimal(8);
                    employee.EmployerContributions += reader.GetDecimal(9);
                    employee.Provisions += reader.GetDecimal(10);
                    employee.NetPayable += reader.GetDecimal(11);
                }
                await reader.NextResultAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var partyId = reader.GetGuid(0);
                    if (!lines.TryGetValue(partyId, out var employeeLines))
                        throw new PayrollValidationException(
                            "Las líneas de nómina no tienen un trabajador consolidado.");
                    employeeLines.Add(new ElectronicPayrollSnapshotLine(
                        reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                        reader.GetString(4), reader.GetString(5), reader.GetString(6),
                        NullableString(reader, 7), reader.GetDecimal(8),
                        reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                        reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                        reader.GetDecimal(11), reader.GetBoolean(12), reader.GetBoolean(13)));
                }
            }
            if (employees.Count == 0)
                throw new PayrollValidationException(
                    "No existen valores de nómina de liquidaciones aprobadas completamente contenidas en el período.");

            if (employees.Values.Any(employee =>
                string.IsNullOrWhiteSpace(employee.Identification) ||
                string.IsNullOrWhiteSpace(employee.IdentificationTypeCode) ||
                string.IsNullOrWhiteSpace(employee.FirstName) ||
                string.IsNullOrWhiteSpace(employee.FirstSurname) ||
                string.IsNullOrWhiteSpace(employee.ContractTypeCode) ||
                string.IsNullOrWhiteSpace(employee.WorkerTypeCode) ||
                string.IsNullOrWhiteSpace(employee.PaymentMethodCode) ||
                employee.PayrollPeriodCode <= 0))
                throw new PayrollValidationException(
                    "Completa identificación, nombres y códigos DIAN de todos los trabajadores antes de generar.");

            Guid issuerConfigurationId;
            string fiscalPrefix;
            long nextConsecutive;
            string qrValidationUrl;
            string softwareIdentificationCode;
            string softwarePinSecretReference;
            Guid? testSetId;
            await using (var configuration = new SqlCommand("""
                SELECT ec.FiscalIssuerConfigurationId,ec.Prefix,ec.NextConsecutive,ec.QrValidationUrl,
                       ec.SoftwareIdentificationCode,ec.SoftwarePinSecretReference,ec.TestSetId
                FROM payroll.ElectronicConfigurations ec WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.FiscalIssuerConfigurations issuer
                  ON issuer.FiscalIssuerConfigurationId=ec.FiscalIssuerConfigurationId
                 AND issuer.BusinessId=ec.BusinessId
                WHERE ec.TenantId=@TenantId AND ec.BusinessId=@BusinessId AND ec.IsActive=1;
                """, connection, tx))
            {
                configuration.Parameters.AddWithValue("@TenantId", user.TenantId);
                configuration.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await configuration.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new PayrollValidationException(
                        "Configura una serie de nómina electrónica y un emisor fiscal antes de generar.");
                issuerConfigurationId = reader.GetGuid(0);
                fiscalPrefix = reader.GetString(1);
                nextConsecutive = reader.GetInt64(2);
                qrValidationUrl = reader.GetString(3);
                softwareIdentificationCode = reader.GetString(4);
                softwarePinSecretReference = reader.GetString(5);
                testSetId = reader.IsDBNull(6) ? null : reader.GetGuid(6);
            }

            var now = timeProvider.GetUtcNow();
            await using (var replace = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM payroll.ElectronicPeriods WHERE ElectronicPeriodId=@PeriodId)
                  INSERT payroll.ElectronicPeriods(
                    ElectronicPeriodId,TenantId,BusinessId,[Year],[Month],Status,CreatedBy,CreatedAt)
                  VALUES(@PeriodId,@TenantId,@BusinessId,@Year,@Month,N'Draft',@UserId,@Now);
                DELETE FROM payroll.ElectronicDocuments
                WHERE ElectronicPeriodId=@PeriodId AND Status=N'Draft' AND FiscalDocumentId IS NULL;
                """, connection, tx))
            {
                replace.Parameters.AddWithValue("@PeriodId", request.ElectronicPeriodId);
                replace.Parameters.AddWithValue("@TenantId", user.TenantId);
                replace.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                replace.Parameters.AddWithValue("@Year", request.Year);
                replace.Parameters.AddWithValue("@Month", request.Month);
                replace.Parameters.AddWithValue("@UserId", user.UserId);
                replace.Parameters.AddWithValue("@Now", now);
                await replace.ExecuteNonQueryAsync(ct);
            }

            var documentIds = new List<Guid>();
            foreach (var employee in employees.Values.OrderBy(value => value.PartyId))
            {
                var documentId = ids.NewId();
                if (!await TryReserveDianQuotaAsync(connection, tx,
                    user.BusinessId, documentId, "ElectronicPayroll", now, ct))
                    throw new PayrollValidationException(
                        "La nómina no se enviará porque no hay cupo de documentos DIAN. Compra un paquete y vuelve a generar el envío.");
                var consecutive = nextConsecutive++;
                var fiscalNumber = fiscalPrefix + consecutive.ToString(CultureInfo.InvariantCulture);
                var snapshot = new ElectronicPayrollSnapshot(
                    user.TenantId, user.BusinessId, employee.PartyId, employee.Name,
                    employee.Identification, employee.IdentificationTypeCode,
                    employee.FirstName, "", employee.FirstSurname, "",
                    employee.EmploymentId, employee.EmployeeCode, employee.EmploymentStart,
                    employee.EmploymentEnd, employee.MonthlySalary, employee.IntegralSalary,
                    employee.ContractTypeCode, employee.WorkerTypeCode, employee.WorkerSubtypeCode,
                    false, employee.PaymentMethodCode, employee.Bank,
                    employee.BankAccountType, employee.BankAccountNumber,
                    employee.PayrollPeriodCode, softwareIdentificationCode,
                    softwarePinSecretReference, testSetId,
                    fiscalPrefix, consecutive, now.ToOffset(TimeSpan.FromHours(-5)), qrValidationUrl,
                    request.Year, request.Month, periodStart, periodEnd,
                    employee.PaymentDates.Distinct().Order().ToArray(),
                    employee.RunIds.Distinct().Order().ToArray(),
                    employee.WorkedDays, employee.Earnings, employee.Deductions,
                    employee.EmployerContributions, employee.Provisions, employee.NetPayable,
                    lines[employee.PartyId]);
                var json = PayrollContractSerializer.Serialize(snapshot);
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
                await using var insert = new SqlCommand("""
                    INSERT dbo.FiscalDocuments(
                      DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
                      AuralyDocumentNumber,FiscalNumber,UniqueCodeType,IssuedAt,FiscalStatus,
                      CreatedAt,UpdatedAt)
                    VALUES(@Id,@BusinessId,N'ElectronicPayroll',N'ElectronicPayroll',
                      @AuralyNumber,@FiscalNumber,N'CUNE',@GeneratedAt,@FiscalStatus,@Now,@Now);
                    INSERT dbo.FiscalDocumentProcesses(
                      DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,AttemptCount,
                      NextAttemptAt,CreatedAt,UpdatedAt)
                    VALUES(@Id,@BusinessId,@IssuerId,@FiscalStatus,0,@Now,@Now,@Now);
                    INSERT payroll.ElectronicDocuments(
                      ElectronicPayrollDocumentId,ElectronicPeriodId,TenantId,BusinessId,PartyId,
                      DocumentKind,FiscalDocumentId,TestSetId,SourceSnapshotJson,SourceHash,Status,CreatedAt)
                    VALUES(@Id,@PeriodId,@TenantId,@BusinessId,@PartyId,N'Individual',
                           @Id,@TestSetId,@Snapshot,@Hash,N'Queued',@Now);
                    """, connection, tx);
                insert.Parameters.AddWithValue("@Id", documentId);
                insert.Parameters.AddWithValue("@PeriodId", request.ElectronicPeriodId);
                insert.Parameters.AddWithValue("@TenantId", user.TenantId);
                insert.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                insert.Parameters.AddWithValue("@PartyId", employee.PartyId);
                insert.Parameters.AddWithValue("@Snapshot", json);
                insert.Parameters.AddWithValue("@IssuerId", issuerConfigurationId);
                insert.Parameters.AddWithValue("@TestSetId", (object?)testSetId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@AuralyNumber", $"PAYROLL-{request.Year:D4}-{request.Month:D2}-{employee.EmployeeCode}");
                insert.Parameters.AddWithValue("@FiscalNumber", fiscalNumber);
                insert.Parameters.AddWithValue("@FiscalStatus", FiscalDocumentStatusCodes.PendingGeneration);
                insert.Parameters.AddWithValue("@GeneratedAt", snapshot.GeneratedAt);
                insert.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
                insert.Parameters.AddWithValue("@Now", now);
                await insert.ExecuteNonQueryAsync(ct);
                documentIds.Add(documentId);
            }

            await using (var sequence = new SqlCommand("""
                UPDATE payroll.ElectronicConfigurations SET NextConsecutive=@Next,UpdatedAt=@Now
                WHERE TenantId=@TenantId AND BusinessId=@BusinessId
                  AND FiscalIssuerConfigurationId=@IssuerId;
                IF @@ROWCOUNT<>1 THROW 51773,N'La serie electrónica cambió durante la generación.',1;
                """, connection, tx))
            {
                sequence.Parameters.AddWithValue("@Next", nextConsecutive);
                sequence.Parameters.AddWithValue("@Now", now);
                sequence.Parameters.AddWithValue("@TenantId", user.TenantId);
                sequence.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                sequence.Parameters.AddWithValue("@IssuerId", issuerConfigurationId);
                await sequence.ExecuteNonQueryAsync(ct);
            }

            var outboxPayload = JsonSerializer.Serialize(new
            {
                electronicPeriodId = request.ElectronicPeriodId,
                businessId = user.BusinessId,
                documents = documentIds
            });
            await using (var complete = new SqlCommand("""
                UPDATE payroll.ElectronicPeriods SET Status=N'Generated'
                WHERE ElectronicPeriodId=@PeriodId AND TenantId=@TenantId
                  AND BusinessId=@BusinessId AND Status IN(N'Draft',N'Generated');
                IF @@ROWCOUNT<>1 THROW 51773,N'El período electrónico cambió durante la consolidación.',1;
                MERGE payroll.OutboxMessages AS target
                USING (SELECT @PeriodId AggregateId,N'ElectronicPayrollPrepared' MessageType) AS source
                  ON target.AggregateId=source.AggregateId AND target.MessageType=source.MessageType
                WHEN MATCHED THEN UPDATE SET PayloadJson=@Payload,OccurredAt=@Now,
                  PublishedAt=NULL,AttemptCount=0,LastError=NULL
                WHEN NOT MATCHED THEN INSERT(
                  OutboxMessageId,TenantId,BusinessId,AggregateId,MessageType,PayloadJson,
                  OccurredAt,AttemptCount)
                  VALUES(@OutboxId,@TenantId,@BusinessId,@PeriodId,source.MessageType,@Payload,@Now,0);
                """, connection, tx))
            {
                complete.Parameters.AddWithValue("@PeriodId", request.ElectronicPeriodId);
                complete.Parameters.AddWithValue("@TenantId", user.TenantId);
                complete.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                complete.Parameters.AddWithValue("@Payload", outboxPayload);
                complete.Parameters.AddWithValue("@OutboxId", ids.NewId());
                complete.Parameters.AddWithValue("@Now", now);
                await complete.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            return await ReadElectronicPeriodAsync(user, request.ElectronicPeriodId, ct);
        }
        catch (SqlException error) when (error.Number is 51770 or 51771 or 51772 or 51773)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new PayrollConflictException(error.Message);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task MarkAccountingSignalPublishedAsync(Guid payrollRunId, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE payroll.OutboxMessages SET PublishedAt=COALESCE(PublishedAt,@Now),LastError=NULL
            WHERE AggregateId=@RunId AND MessageType=N'AccountingPostingRequested';
            """, connection);
        command.Parameters.AddWithValue("@RunId", payrollRunId); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkElectronicSignalPublishedAsync(
        Guid electronicPeriodId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE payroll.OutboxMessages
            SET PublishedAt=COALESCE(PublishedAt,@Now),LastError=NULL
            WHERE AggregateId=@PeriodId AND MessageType=N'ElectronicPayrollPrepared';
            """, connection);
        command.Parameters.AddWithValue("@PeriodId", electronicPeriodId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<ElectronicPayrollPeriodView> ReadElectronicPeriodAsync(
        PayrollUserIdentity user, Guid periodId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT ElectronicPeriodId,[Year],[Month],Status,RowVersion
            FROM payroll.ElectronicPeriods
            WHERE ElectronicPeriodId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId;
            SELECT d.ElectronicPayrollDocumentId,d.PartyId,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador'),
                   d.DocumentKind,d.FiscalDocumentId,d.Status,d.SourceHash
            FROM payroll.ElectronicDocuments d
            JOIN dbo.Parties p ON p.PartyId=d.PartyId AND p.TenantId=d.TenantId
            WHERE d.ElectronicPeriodId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId
            ORDER BY 3,d.ElectronicPayrollDocumentId;
            """, connection);
        command.Parameters.AddWithValue("@Id", periodId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new PayrollNotFoundException("El período de nómina electrónica no existe.");
        var id = reader.GetGuid(0);
        var year = reader.GetInt16(1);
        var month = reader.GetByte(2);
        var status = reader.GetString(3);
        var version = (byte[])reader[4];
        await reader.NextResultAsync(ct);
        var documents = new List<ElectronicPayrollDocumentView>();
        while (await reader.ReadAsync(ct))
            documents.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2).Trim(),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5), Convert.ToHexString((byte[])reader[6]).ToLowerInvariant()));
        return new(id, year, month, status, documents, version);
    }

    public async Task<PayrollRunView> GetRunAsync(PayrollUserIdentity user, Guid runId, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        return await ReadRunAsync(connection,null,user,runId,ct,null);
    }

    private static async Task<PayrollRunView> ReadRunAsync(SqlConnection connection,SqlTransaction? transaction,
        PayrollUserIdentity user,Guid runId,CancellationToken ct,string? employeeIds)
    {
        await using var command = new SqlCommand("""
            DECLARE @ReadEmployees TABLE(PayrollRunEmployeeId uniqueidentifier PRIMARY KEY);
            IF @EmployeeIds IS NULL
                INSERT @ReadEmployees SELECT e.PayrollRunEmployeeId FROM payroll.RunEmployees e
                JOIN payroll.Runs r ON r.PayrollRunId=e.PayrollRunId AND r.TenantId=e.TenantId
                WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId;
            ELSE
                INSERT @ReadEmployees SELECT e.PayrollRunEmployeeId
                FROM OPENJSON(@EmployeeIds) WITH(EmploymentId uniqueidentifier) selected
                JOIN payroll.RunEmployees e ON e.PayrollRunId=@RunId AND e.EmploymentId=selected.EmploymentId AND e.TenantId=@TenantId
                JOIN payroll.Runs r ON r.PayrollRunId=e.PayrollRunId AND r.TenantId=e.TenantId AND r.BusinessId=@BusinessId
                OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1,RECOMPILE);
            SELECT r.PayrollRunId,r.BusinessId,r.RuleSetId,r.RunKind,r.OriginalPayrollRunId,
              r.PeriodStart,r.PeriodEnd,r.PaymentDate,r.Status,r.CalculationVersion,r.TotalEarnings,
              r.TotalDeductions,r.TotalEmployerContributions,r.TotalProvisions,r.NetPayable,r.RowVersion,
              (SELECT COUNT(*) FROM payroll.RunEmployees countRows WHERE countRows.PayrollRunId=r.PayrollRunId AND countRows.TenantId=r.TenantId)
            FROM payroll.Runs r WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId;
            SELECT e.PayrollRunEmployeeId,e.EmploymentId,e.PartyId,
              COALESCE(CONVERT(nvarchar(500),JSON_VALUE(e.EmployeeSnapshotJson,'$.EmployeeName')),p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador'),
              e.WorkedDays,e.Earnings,e.Deductions,e.EmployerContributions,e.Provisions,e.NetPayable,
              TRY_CONVERT(decimal(9,4),JSON_VALUE(e.EmployeeSnapshotJson,'$.BaseWorkedDays')),
              TRY_CONVERT(decimal(9,4),JSON_VALUE(e.EmployeeSnapshotJson,'$.MaximumContractDays')),
              TRY_CONVERT(int,JSON_VALUE(e.EmployeeSnapshotJson,'$.PeriodDays')),
              JSON_VALUE(e.EmployeeSnapshotJson,'$.Identification'),JSON_VALUE(e.EmployeeSnapshotJson,'$.ContractNumber'),
              TRY_CONVERT(decimal(19,4),JSON_VALUE(e.EmployeeSnapshotJson,'$.MonthlySalary')),
              JSON_QUERY(e.EmployeeSnapshotJson,'$.ConceptWorkedDays')
            FROM @ReadEmployees selected JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=selected.PayrollRunEmployeeId
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            ORDER BY 4,e.EmploymentId OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1,RECOMPILE);
            SELECT e.PayrollRunEmployeeId,l.LineNumber,l.ConceptId,l.ConceptCode,l.ConceptName,l.NatureCode,
              l.DianConceptCode,l.AccountingCategoryCode,l.Quantity,l.Rate,l.BaseAmount,l.Amount,l.IsEmployerCost,l.IsSalaryBase,l.NoveltyId,l.DeductionAgreementId,l.PayrollRunLineId
            FROM @ReadEmployees selected JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=selected.PayrollRunEmployeeId
            JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=selected.PayrollRunEmployeeId
            ORDER BY e.PayrollRunEmployeeId,l.LineNumber OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1,RECOMPILE);
            SELECT TOP(@EmployeeReadLimit) PayrollRunEmployeeId,PaymentMethodOptionId,PaymentMethodName,NetPayable,
              BankDestinationComplete,EmployeeName
            FROM payroll.PayableRunEmployees(@TenantId,@BusinessId,@RunId);
            DECLARE @ReadNovelties TABLE(NoveltyId uniqueidentifier PRIMARY KEY);
            INSERT @ReadNovelties
            SELECT n.NoveltyId FROM @ReadEmployees selected
            JOIN payroll.RunEmployees employee ON employee.PayrollRunEmployeeId=selected.PayrollRunEmployeeId
            JOIN payroll.Runs run ON run.PayrollRunId=employee.PayrollRunId AND run.TenantId=@TenantId AND run.BusinessId=@BusinessId
            JOIN payroll.Novelties n ON n.EmploymentId=employee.EmploymentId AND n.TenantId=run.TenantId AND n.BusinessId=run.BusinessId
              AND n.StartDate<=run.PeriodEnd AND n.EndDate>=run.PeriodStart;
            INSERT @ReadNovelties
            SELECT DISTINCT line.NoveltyId FROM @ReadEmployees selected
            JOIN payroll.RunLines line ON line.PayrollRunEmployeeId=selected.PayrollRunEmployeeId
            WHERE line.NoveltyId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM @ReadNovelties present WHERE present.NoveltyId=line.NoveltyId);
            """ + NoveltyProjectionSql + """

            WHERE n.TenantId=@TenantId AND n.BusinessId=@BusinessId
              AND EXISTS(SELECT 1 FROM @ReadNovelties selected WHERE selected.NoveltyId=n.NoveltyId)
            ORDER BY n.EmploymentId,n.StartDate,n.NoveltyId;
            """, connection,transaction);
        command.Parameters.Add("@EmployeeIds",SqlDbType.NVarChar,-1).Value=(object?)employeeIds??DBNull.Value;
        command.Parameters.AddWithValue("@EmployeeReadLimit",PayrollPaymentLimits.MaximumEmployees+1);
        command.Parameters.AddWithValue("@RunId", runId); command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La liquidación no existe.");
        var header = new RunHeader(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2)?null:reader.GetGuid(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), DateOnly.FromDateTime(reader.GetDateTime(5)), DateOnly.FromDateTime(reader.GetDateTime(6)),
            DateOnly.FromDateTime(reader.GetDateTime(7)), reader.GetString(8), reader.GetInt32(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13), reader.GetDecimal(14), (byte[])reader[15],reader.GetInt32(16));
        await reader.NextResultAsync(ct);
        var employeeRows = new List<EmployeeRow>();
        while (await reader.ReadAsync(ct))
        {
            var conceptDays=reader.IsDBNull(16)?Array.Empty<PayrollConceptWorkedDaysView>():
                JsonSerializer.Deserialize<PayrollConceptWorkedDaysView[]>(reader.GetString(16))
                ?? throw new PayrollConflictException("Las entradas de días del cálculo requieren revisión.");
            employeeRows.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetString(3).Trim(),
                reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9),
                reader.IsDBNull(10)?null:reader.GetDecimal(10),reader.IsDBNull(11)?null:reader.GetDecimal(11),reader.IsDBNull(12)?null:reader.GetInt32(12),
                NullableString(reader,13),NullableString(reader,14),reader.IsDBNull(15)?null:reader.GetDecimal(15),conceptDays));
        }
        await reader.NextResultAsync(ct);
        var lineLookup = employeeRows.ToDictionary(x => x.Id, _ => new List<PayrollRunLineView>());
        while (await reader.ReadAsync(ct))
            lineLookup[reader.GetGuid(0)].Add(new(reader.GetInt32(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), NullableString(reader, 6), reader.GetString(7), reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10), reader.GetDecimal(11), reader.GetBoolean(12)) {IsSalaryBase=reader.GetBoolean(13),NoveltyId=reader.IsDBNull(14)?null:reader.GetGuid(14),DeductionAgreementId=reader.IsDBNull(15)?null:reader.GetGuid(15),PayrollRunLineId=reader.GetGuid(16)});
        var employees = employeeRows.Select(x => new PayrollRunEmployeeView(x.Id, x.EmploymentId, x.PartyId, x.Name,
            x.Days, x.Earnings, x.Deductions, x.Employer, x.Provisions, x.Net, lineLookup[x.Id]) {BaseWorkedDays=x.BaseDays,MaximumContractDays=x.MaximumDays,PeriodDays=x.PeriodDays,Identification=x.Identification,ContractNumber=x.ContractNumber,MonthlySalary=x.MonthlySalary,ConceptWorkedDays=x.ConceptWorkedDays}).ToArray();
        await reader.NextResultAsync(ct);
        var payable=await ReadPayableEmployeesAsync(reader,ct);
        await reader.NextResultAsync(ct);
        var novelties=new List<PayrollNoveltyView>();
        while(await reader.ReadAsync(ct))novelties.Add(ReadNovelty(reader));
        var noveltyLookup=novelties.ToLookup(item=>item.EmploymentId);
        employees=employees.Select(employee=>employee with {Novelties=noveltyLookup[employee.EmploymentId].ToArray()}).ToArray();
        return new(header.Id, header.BusinessId, header.RuleSetId, header.Kind, header.OriginalId, header.Start, header.End,
            header.Payment, header.Status, header.Version, header.Earnings, header.Deductions, header.Employer,
            header.Provisions, header.Net, employees, header.RowVersion) {PayableEmployees=payable,EmployeeCount=header.EmployeeCount,IsPartialCalculation=employeeIds is not null};
    }

    private static async Task<IReadOnlyList<PayrollPayableEmployee>> ReadPayableEmployeesAsync(SqlDataReader reader,CancellationToken ct)
    {
        var result=new List<PayrollPayableEmployee>();
        while(await reader.ReadAsync(ct))
        {
            if(result.Count==PayrollPaymentLimits.MaximumEmployees) throw new PayrollValidationException("La consulta de pagos admite hasta 500 trabajadores pendientes por liquidación.");
            result.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetDecimal(3),reader.GetBoolean(4),reader.GetString(5).Trim()));
        }
        return result;
    }

    public async Task<IReadOnlyList<PayrollRunSummary>> ListRunsAsync(PayrollUserIdentity user, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT r.PayrollRunId,r.RunKind,r.PeriodStart,r.PeriodEnd,r.PaymentDate,r.Status,
              (SELECT COUNT(*) FROM payroll.RunEmployees e WHERE e.PayrollRunId=r.PayrollRunId),
              r.TotalEarnings,r.TotalDeductions,r.NetPayable,r.RowVersion
            FROM payroll.Runs r
            WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
            ORDER BY r.PeriodEnd DESC,r.CreatedAt DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        var values = new List<PayrollRunSummary>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values.Add(new(reader.GetGuid(0), reader.GetString(1), DateOnly.FromDateTime(reader.GetDateTime(2)),
                DateOnly.FromDateTime(reader.GetDateTime(3)), DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.GetString(5), reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8),
                reader.GetDecimal(9), (byte[])reader[10]));
        return values;
    }

    private static async Task<PayrollEmploymentView> ReadEmploymentAsync(SqlConnection connection,SqlTransaction transaction,PayrollUserIdentity user,Guid id,CancellationToken ct)
    {
        await using var command = new SqlCommand($"""
            SELECT e.EmploymentId,e.PartyId,e.BusinessId,e.EmployeeId,
              COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
              e.ContractTypeOptionId,e.SalaryTypeOptionId,e.PayFrequencyOptionId,e.RiskClassOptionId,
              e.WorkerTypeOptionId,e.WorkerSubtypeOptionId,e.PaymentMethodOptionId,e.ContractNumber,
              e.StartDate,e.EndDate,e.MonthlySalary,e.IntegralSalaryPercentage,e.BankAccountReference,
              e.BankOptionId,e.BankAccountTypeOptionId,e.BankAccountNumber,e.IsActive,e.RowVersion,e.AppliesTransportAllowance
                   {EmploymentConfigurationProjection}
            FROM payroll.Employments e JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            {EmploymentConfigurationJoins}
            WHERE e.EmploymentId=@Id AND e.TenantId=@TenantId AND e.BusinessId=@BusinessId;
            """, connection,transaction);
        command.Parameters.AddWithValue("@Id", id); command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La relación laboral no existe.");
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4).Trim(),
            reader.GetGuid(5), reader.GetGuid(6), reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9), reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.GetGuid(11), reader.GetString(12),
            DateOnly.FromDateTime(reader.GetDateTime(13)), reader.IsDBNull(14) ? null : DateOnly.FromDateTime(reader.GetDateTime(14)), reader.GetDecimal(15), reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            NullableString(reader, 17), reader.IsDBNull(18) ? null : reader.GetGuid(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19), NullableString(reader, 20),
            reader.GetBoolean(21), (byte[])reader[22]) { AppliesTransportAllowance = reader.GetBoolean(23), Configuration=JsonSerializer.Deserialize<PayrollEmploymentConfigurationView>(reader.GetString(24),Json) };
    }

    private async Task<PayrollDeductionAgreementView> ReadAgreementAsync(PayrollUserIdentity user, Guid id, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT a.DeductionAgreementId,a.EmploymentId,a.ConceptId,a.AuthorityOptionId,a.BeneficiaryPartyId,
              a.ReferenceNumber,a.EvidenceUrl,a.EffectiveFrom,a.EffectiveTo,a.AuthorizedTotal,a.InstallmentAmount,
              a.DeductedToDate,a.Priority,a.MustProtectMinimumNetPay,a.IsActive,a.RowVersion
            FROM payroll.DeductionAgreements a JOIN payroll.Employments e ON e.EmploymentId=a.EmploymentId
            WHERE a.DeductionAgreementId=@Id AND a.TenantId=@TenantId AND e.BusinessId=@BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@Id", id); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("El acuerdo de deducción no existe.");
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
            DateOnly.FromDateTime(reader.GetDateTime(7)), reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)),
            reader.IsDBNull(9) ? null : reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10),
            reader.GetDecimal(11), reader.GetInt16(12), reader.GetBoolean(13), reader.GetBoolean(14), (byte[])reader[15]);
    }

    private static async Task<(IReadOnlyList<PayrollAccountingLine> Lines,IReadOnlyList<PayrollAccountingEmployee> Employees,IReadOnlyList<PayrollAccountingConcept> Concepts)> BuildAccountingLinesAsync(SqlConnection connection, SqlTransaction tx, Guid runId, CancellationToken ct)
    {
        var values=new List<PayrollAccountingLine>();var contexts=new List<PayrollAccountingEmployee>();
        var concepts=new List<PayrollAccountingConcept>();var conceptIndexes=new Dictionary<(Guid? Id,string Description),int>();
        PayrollAccountingLine CreateLine(Guid? conceptId,string name,int employeeIndex,string category,decimal debit,decimal credit,Guid party)
        {
            if(!conceptIndexes.TryGetValue((conceptId,name),out var conceptIndex))
            {
                conceptIndex=concepts.Count;conceptIndexes.Add((conceptId,name),conceptIndex);concepts.Add(new(conceptId,name));
            }
            return new(category,debit,credit,party,null,employeeIndex,conceptIndex);
        }
        var employees=new Dictionary<Guid,(int Index,Guid EmploymentId,Guid PartyId,decimal Net,PayrollEmploymentConfiguration? Configuration)>();
        await using var command=new SqlCommand("""
            SELECT PayrollRunEmployeeId,EmploymentId,PartyId,NetPayable,JSON_QUERY(EmployeeSnapshotJson,'$.Configuration.Values')
            FROM payroll.RunEmployees WHERE PayrollRunId=@RunId ORDER BY PayrollRunEmployeeId;
            SELECT l.PayrollRunEmployeeId,l.ConceptId,l.NatureCode,l.AccountingCategoryCode,l.ConceptName,l.Amount,role.Code,mapping.BeneficiaryPartyId,agreement.BeneficiaryPartyId
            FROM payroll.RunEmployees employee JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=employee.PayrollRunEmployeeId
            JOIN payroll.Concepts concept ON concept.ConceptId=l.ConceptId AND concept.TenantId=employee.TenantId
            LEFT JOIN payroll.CatalogOptions role ON role.OptionId=concept.SystemRoleOptionId
            JOIN payroll.Runs run ON run.PayrollRunId=employee.PayrollRunId AND run.TenantId=employee.TenantId
            LEFT JOIN payroll.ProfileAccountMappings mapping ON mapping.ProfileId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(employee.EmployeeSnapshotJson,'$.Configuration.Values.AccountingProfileId'))
              AND mapping.TenantId=employee.TenantId AND mapping.ConceptId=l.ConceptId
              AND mapping.EffectiveFrom<=run.PeriodEnd AND (mapping.EffectiveTo IS NULL OR mapping.EffectiveTo>=run.PeriodEnd)
            LEFT JOIN payroll.DeductionAgreements agreement ON agreement.DeductionAgreementId=l.DeductionAgreementId
              AND agreement.TenantId=employee.TenantId AND agreement.EmploymentId=employee.EmploymentId
            WHERE employee.PayrollRunId=@RunId ORDER BY employee.PayrollRunEmployeeId,l.LineNumber;
            """,connection,tx);
        command.Parameters.AddWithValue("@RunId",runId);await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
        {
            PayrollEmploymentConfiguration? configuration=null;
            if(!reader.IsDBNull(4))configuration=JsonSerializer.Deserialize<PayrollEmploymentConfiguration>(reader.GetString(4),Json);
            var employmentId=reader.GetGuid(1);
            employees.Add(reader.GetGuid(0),(contexts.Count,employmentId,reader.GetGuid(2),reader.GetDecimal(3),configuration));
            contexts.Add(new(employmentId,configuration?.AccountingProfileId,configuration?.CostCenterId));
        }
        await reader.NextResultAsync(ct);
        while(await reader.ReadAsync(ct))
        {
            var employee=employees[reader.GetGuid(0)];var concept=reader.GetGuid(1);var nature=reader.GetString(2);
            var category=reader.GetString(3);var name=reader.GetString(4);var amount=reader.GetDecimal(5);
            if(amount==0)continue;
            var role=NullableString(reader,6);var config=employee.Configuration;
            var creditor=role switch
            {
                PayrollSystemConceptRoles.EmployeeHealth or PayrollSystemConceptRoles.EmployerHealth=>config?.HealthPartyId,
                PayrollSystemConceptRoles.EmployeePension or PayrollSystemConceptRoles.EmployerPension=>config?.PensionPartyId,
                PayrollSystemConceptRoles.OccupationalRisk=>config?.RiskPartyId,
                PayrollSystemConceptRoles.CompensationFund=>config?.CompensationPartyId,
                _=>reader.IsDBNull(8)?reader.IsDBNull(7)?(Guid?)null:reader.GetGuid(7):reader.GetGuid(8)
            };
            if(nature=="EmployerContribution" && (role is PayrollSystemConceptRoles.Sena or PayrollSystemConceptRoles.Icbf) && creditor is null)
                throw new PayrollValidationException($"Configura la entidad beneficiaria del concepto {name} en el perfil antes de contabilizar.");
            PayrollAccountingLine Line(string accountCategory,decimal debit,decimal credit,Guid party)=>
                CreateLine(concept,name,employee.Index,accountCategory,debit,credit,party);
            switch(nature)
            {
                case "Earning":values.Add(Line(category,amount,0,employee.PartyId));break;
                case "Deduction":values.Add(Line(category,0,amount,creditor??employee.PartyId));break;
                case "EmployerContribution":
                    values.Add(Line("EmployerContributionsExpense",amount,0,employee.PartyId));
                    values.Add(Line(category,0,amount,creditor??employee.PartyId));break;
                case "Provision":
                    values.Add(Line("BenefitsExpense",amount,0,employee.PartyId));
                    values.Add(Line(category,0,amount,employee.PartyId));break;
                default:throw new PayrollValidationException($"La naturaleza contable '{nature}' no es válida.");
            }
        }
        foreach(var employee in employees.Values)
            if(employee.Net>0)values.Add(CreateLine(null,"Neto de nómina por pagar",employee.Index,
                PayrollAccountingCategories.NetPayable,0,employee.Net,employee.PartyId));
        if(values.Count==0 || decimal.Round(values.Sum(value=>value.Debit),4)!=decimal.Round(values.Sum(value=>value.Credit),4))
            throw new PayrollValidationException("La fuente contable de nómina no está balanceada.");
        return(values,contexts,concepts);
    }

    private static async Task<Guid?> FindAccountingJobAsync(SqlConnection connection, SqlTransaction tx, Guid runId, string type, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT AccountingPostingJobId FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=@Type;", connection, tx);
        command.Parameters.AddWithValue("@Id", runId); command.Parameters.AddWithValue("@Type", type);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid id ? id : null;
    }

    private void AddEmploymentParameters(SqlCommand command, PayrollUserIdentity user, SavePayrollEmploymentRequest request)
    {
        command.Parameters.AddWithValue("@Id", request.EmploymentId); command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@PartyId", request.PartyId); command.Parameters.AddWithValue("@EmployeeId", (object?)request.EmployeeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@ContractType", request.ContractTypeOptionId); command.Parameters.AddWithValue("@SalaryType", request.SalaryTypeOptionId); command.Parameters.AddWithValue("@Frequency", request.PayFrequencyOptionId);
        command.Parameters.AddWithValue("@RiskClass", request.RiskClassOptionId); command.Parameters.AddWithValue("@WorkerType", request.WorkerTypeOptionId); command.Parameters.AddWithValue("@WorkerSubtype", (object?)request.WorkerSubtypeOptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PaymentMethod", request.PaymentMethodOptionId); command.Parameters.AddWithValue("@ContractNumber", request.ContractNumber);
        command.Parameters.AddWithValue("@StartDate", request.StartDate.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EndDate", DbDate(request.EndDate)); Decimal(command, "@Salary", request.MonthlySalary, 4);
        DecimalNullable(command, "@IntegralPercentage", request.IntegralSalaryPercentage, 6); command.Parameters.AddWithValue("@BankReference", (object?)request.BankAccountReference ?? DBNull.Value); command.Parameters.AddWithValue("@Active", request.IsActive);
        command.Parameters.AddWithValue("@Bank", (object?)request.BankOptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@BankAccountType", (object?)request.BankAccountTypeOptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@BankAccountNumber", (object?)request.BankAccountNumber ?? DBNull.Value);
        command.Parameters.Add("@ApplyTransport",SqlDbType.Bit).Value=(object?)request.AppliesTransportAllowance??DBNull.Value;
        command.Parameters.Add("@Configuration",SqlDbType.NVarChar,-1).Value=request.Configuration is null
            ? DBNull.Value : JsonSerializer.Serialize(request.Configuration,Json);
        command.Parameters.AddWithValue("@UserId", user.UserId); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow()); command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = (object?)request.RowVersion ?? DBNull.Value;
    }

    private void AddConceptParameters(SqlCommand command, PayrollUserIdentity user, SavePayrollConceptRequest request)
    {
        command.Parameters.AddWithValue("@Id", request.ConceptId); command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@Code", request.Code); command.Parameters.AddWithValue("@Name", request.Name);
        command.Parameters.AddWithValue("@Nature", request.NatureOptionId); command.Parameters.AddWithValue("@Method", request.CalculationMethodOptionId); command.Parameters.AddWithValue("@Treatment", request.TreatmentOptionId);
        command.Parameters.AddWithValue("@Dian", (object?)request.DianConceptOptionId ?? DBNull.Value); command.Parameters.AddWithValue("@Accounting", request.AccountingCategoryOptionId); command.Parameters.AddWithValue("@SystemRole", (object?)request.SystemRoleOptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SalaryBase", request.IsSalaryBase); command.Parameters.AddWithValue("@SecurityBase", request.IsSocialSecurityBase); command.Parameters.AddWithValue("@BenefitsBase", request.IsBenefitsBase);
        command.Parameters.AddWithValue("@WithholdingBase", request.IsTaxWithholdingBase); command.Parameters.AddWithValue("@RequiresAgreement", request.RequiresDeductionAgreement);
        command.Parameters.AddWithValue("@From", request.EffectiveFrom.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@To", DbDate(request.EffectiveTo)); command.Parameters.AddWithValue("@Active", request.IsActive);
        command.Parameters.AddWithValue("@UserId", user.UserId); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow()); command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = (object?)request.RowVersion ?? DBNull.Value;
    }

    private static PayrollConceptView ReadConcept(SqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        NullableString(reader, 6), reader.GetString(7), NullableString(reader, 8), reader.GetBoolean(9), reader.GetBoolean(10), reader.GetBoolean(11), reader.GetBoolean(12), reader.GetBoolean(13),
        DateOnly.FromDateTime(reader.GetDateTime(14)), reader.IsDBNull(15) ? null : DateOnly.FromDateTime(reader.GetDateTime(15)), reader.GetBoolean(16), (byte[])reader[17]);
    private static PayrollEmploymentOption ReadEmployment(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
        reader.GetString(4).Trim(), reader.GetDecimal(5), reader.GetBoolean(6),
        reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9),
        reader.GetGuid(10), reader.GetGuid(11), reader.GetGuid(12),
        reader.IsDBNull(13) ? null : reader.GetGuid(13), reader.GetGuid(14),
        DateOnly.FromDateTime(reader.GetDateTime(15)),
        reader.IsDBNull(16) ? null : DateOnly.FromDateTime(reader.GetDateTime(16)),
        reader.IsDBNull(17) ? null : reader.GetDecimal(17), NullableString(reader, 18),
        reader.IsDBNull(19) ? null : reader.GetGuid(19),
        reader.IsDBNull(20) ? null : reader.GetGuid(20), NullableString(reader, 21),
        (byte[])reader[22]) { AppliesTransportAllowance = reader.GetBoolean(23), Configuration=JsonSerializer.Deserialize<PayrollEmploymentConfigurationView>(reader.GetString(24),Json) };
    private static string? NullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static object DbDate(DateOnly? value) => value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue);
    private static void Decimal(SqlCommand command, string name, decimal value, byte scale) { var p = command.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = scale; p.Value = value; }
    private static void DecimalNullable(SqlCommand command, string name, decimal? value, byte scale) { var p = command.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = scale; p.Value = (object?)value ?? DBNull.Value; }
    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;

    private static async Task<bool> TryReserveDianQuotaAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid documentId, string documentKind, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("dbo.TenantDianDocumentQuotaReserve", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentKind", documentKind);
        command.Parameters.AddWithValue("@Now", now);
        var reserved = command.Parameters.Add("@Reserved", SqlDbType.Bit);
        reserved.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync(ct);
        return reserved.Value is true;
    }

    private sealed record EmployeeSeed(Guid EmploymentId, Guid PartyId, decimal MonthlySalary, decimal WorkedDays, bool IsIntegral, decimal RiskRate, bool AppliesTransportAllowance, byte[] EmploymentVersion, decimal MaximumContractDays,decimal TransportBaseWorkedDays);
    private sealed record RunHeader(Guid Id, Guid BusinessId, Guid? RuleSetId, string Kind, Guid? OriginalId, DateOnly Start, DateOnly End, DateOnly Payment, string Status, int Version, decimal Earnings, decimal Deductions, decimal Employer, decimal Provisions, decimal Net, byte[] RowVersion, int EmployeeCount);
    private sealed record EmployeeRow(Guid Id, Guid EmploymentId, Guid PartyId, string Name, decimal Days, decimal Earnings, decimal Deductions, decimal Employer, decimal Provisions, decimal Net,decimal? BaseDays,decimal? MaximumDays,int? PeriodDays,string? Identification,string? ContractNumber,decimal? MonthlySalary,IReadOnlyList<PayrollConceptWorkedDaysView> ConceptWorkedDays);
    private sealed class ElectronicEmployeeSnapshotSeed(
        Guid partyId,
        string name,
        string identification,
        string identificationTypeCode,
        string firstName,
        string firstSurname,
        Guid employmentId,
        string employeeCode,
        DateOnly employmentStart,
        DateOnly? employmentEnd,
        decimal monthlySalary,
        bool integralSalary,
        string contractTypeCode,
        string workerTypeCode,
        string workerSubtypeCode,
        string paymentMethodCode,
        string payrollPeriodCode,
        string? bank,
        string? bankAccountType,
        string? bankAccountNumber,
        List<Guid> runIds,
        List<DateOnly> paymentDates,
        decimal workedDays,
        decimal earnings,
        decimal deductions,
        decimal employerContributions,
        decimal provisions,
        decimal netPayable)
    {
        public Guid PartyId { get; } = partyId;
        public string Name { get; } = name;
        public string Identification { get; } = identification;
        public string IdentificationTypeCode { get; } = identificationTypeCode;
        public string FirstName { get; } = firstName;
        public string FirstSurname { get; } = firstSurname;
        public Guid EmploymentId { get; } = employmentId;
        public string EmployeeCode { get; } = employeeCode;
        public DateOnly EmploymentStart { get; } = employmentStart;
        public DateOnly? EmploymentEnd { get; } = employmentEnd;
        public decimal MonthlySalary { get; } = monthlySalary;
        public bool IntegralSalary { get; } = integralSalary;
        public string ContractTypeCode { get; } = contractTypeCode;
        public string WorkerTypeCode { get; } = workerTypeCode;
        public string WorkerSubtypeCode { get; } = workerSubtypeCode;
        public string PaymentMethodCode { get; } = paymentMethodCode;
        public int PayrollPeriodCode { get; } = int.TryParse(payrollPeriodCode,
            NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        public string? Bank { get; } = bank;
        public string? BankAccountType { get; } = bankAccountType;
        public string? BankAccountNumber { get; } = bankAccountNumber;
        public List<Guid> RunIds { get; } = runIds;
        public List<DateOnly> PaymentDates { get; } = paymentDates;
        public decimal WorkedDays { get; set; } = workedDays;
        public decimal Earnings { get; set; } = earnings;
        public decimal Deductions { get; set; } = deductions;
        public decimal EmployerContributions { get; set; } = employerContributions;
        public decimal Provisions { get; set; } = provisions;
        public decimal NetPayable { get; set; } = netPayable;
    }
}
