using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed class SqlPayrollStore(
    PayrollSqlConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IPayrollStore
{
    public async Task<PayrollWorkspaceOptions> GetOptionsAsync(PayrollUserIdentity user, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
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
                   e.BankOptionId,e.BankAccountTypeOptionId,e.BankAccountNumber,e.RowVersion
            FROM payroll.Employments e JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId ORDER BY e.IsActive DESC,5;

            SELECT p.PartyId,e.EmployeeId,p.Identification,
                   COALESCE(p.DisplayName,CONCAT(p.FirstName,N' ',p.LastName))
            FROM dbo.Parties p
            JOIN dbo.Employees e ON e.PartyId=p.PartyId AND e.BusinessId=@BusinessId AND e.IsActive=1
            JOIN payroll.CatalogOptions idtype
              ON idtype.CatalogCode=N'payroll-identification-type'
             AND idtype.Code=p.IdentificationTypeCode AND idtype.IsActive=1
             AND NULLIF(idtype.DianCode,N'') IS NOT NULL
            WHERE p.TenantId=@TenantId AND p.PartyType=N'NaturalPerson' AND p.IsActive=1
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

            SELECT n.NoveltyId,n.EmploymentId,
                   COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
                   n.ConceptId,c.Name,n.NoveltyTypeOptionId,o.Label,n.DeductionAgreementId,
                   n.StartDate,n.EndDate,n.Quantity,n.UnitAmount,n.TotalAmount,n.Notes,n.EvidenceUrl,n.Status
            FROM payroll.Novelties n
            JOIN payroll.Employments e ON e.EmploymentId=n.EmploymentId AND e.TenantId=n.TenantId
            JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            JOIN payroll.Concepts c ON c.ConceptId=n.ConceptId AND c.TenantId=n.TenantId
            JOIN payroll.CatalogOptions o ON o.OptionId=n.NoveltyTypeOptionId
            WHERE n.TenantId=@TenantId AND n.BusinessId=@BusinessId
            ORDER BY n.StartDate DESC,n.CreatedAt DESC;

            SELECT b.PaymentBatchId,
                   (SELECT TOP(1) re.PayrollRunId FROM payroll.PaymentLines pl
                    JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
                    WHERE pl.PaymentBatchId=b.PaymentBatchId ORDER BY re.PayrollRunId),
                   b.PaymentDate,b.PaymentMethodOptionId,o.Label,b.ReferenceNumber,b.Status,
                   COUNT(l.PaymentLineId),b.TotalAmount,b.RowVersion
            FROM payroll.PaymentBatches b
            JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
            LEFT JOIN payroll.PaymentLines l ON l.PaymentBatchId=b.PaymentBatchId
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
            GROUP BY b.PaymentBatchId,b.PaymentDate,b.PaymentMethodOptionId,o.Label,
                     b.ReferenceNumber,b.Status,b.TotalAmount,b.RowVersion,b.CreatedAt
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
        while (await reader.ReadAsync(ct))
            employments.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4).Trim(), reader.GetDecimal(5), reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9),
                reader.GetGuid(10), reader.GetGuid(11), reader.GetGuid(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13), reader.GetGuid(14),
                DateOnly.FromDateTime(reader.GetDateTime(15)),
                reader.IsDBNull(16) ? null : DateOnly.FromDateTime(reader.GetDateTime(16)),
                reader.IsDBNull(17) ? null : reader.GetDecimal(17), NullableString(reader, 18),
                reader.IsDBNull(19) ? null : reader.GetGuid(19),
                reader.IsDBNull(20) ? null : reader.GetGuid(20), NullableString(reader, 21),
                (byte[])reader[22]));

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
        var novelties = new List<PayrollNoveltyView>();
        while (await reader.ReadAsync(ct))
            novelties.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2).Trim(),
                reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                DateOnly.FromDateTime(reader.GetDateTime(8)), DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.GetDecimal(10), reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.GetDecimal(12), NullableString(reader, 13), NullableString(reader, 14), reader.GetString(15)));

        await reader.NextResultAsync(ct);
        var paymentBatches = new List<PayrollPaymentBatchView>();
        while (await reader.ReadAsync(ct))
            paymentBatches.Add(new(reader.GetGuid(0), reader.GetGuid(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetGuid(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetDecimal(8),
                (byte[])reader[9]));

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
            electronicConfiguration, fiscalIssuers, agreements, novelties,
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
                IF @Active=0 AND EXISTS(
                    SELECT 1 FROM payroll.Employments
                    WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId)
                BEGIN
                    UPDATE payroll.Employments SET IsActive=0,EndDate=@EndDate,
                      UpdatedBy=@UserId,UpdatedAt=@Now
                    WHERE EmploymentId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                      AND RowVersion=@RowVersion;
                    IF @@ROWCOUNT<>1 THROW 51705,N'La relación laboral cambió; recargue antes de guardar.',1;
                END
                ELSE
                BEGIN
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
                    WHERE BusinessId=@BusinessId AND PartyId=@PartyId AND IsActive=1);
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
                IF @Bank IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@Bank AND CatalogCode=N'payroll-bank' AND IsActive=1)
                    THROW 51704,N'El banco no pertenece al catálogo de nómina.',1;
                IF @BankAccountType IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@BankAccountType AND CatalogCode=N'payroll-bank-account-type' AND IsActive=1)
                    THROW 51704,N'El tipo de cuenta no pertenece al catálogo de nómina.',1;
                IF EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@PaymentMethod AND Code=N'BankTransfer')
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
                      IsActive=@Active,UpdatedBy=@UserId,UpdatedAt=@Now
                    WHERE EmploymentId=@Id AND TenantId=@TenantId AND RowVersion=@RowVersion;
                    IF @@ROWCOUNT<>1 THROW 51705,N'La relación laboral cambió; recargue antes de guardar.',1;
                END
                ELSE
                    INSERT payroll.Employments(EmploymentId,TenantId,PartyId,BusinessId,EmployeeId,
                      ContractTypeOptionId,SalaryTypeOptionId,PayFrequencyOptionId,RiskClassOptionId,
                      WorkerTypeOptionId,WorkerSubtypeOptionId,PaymentMethodOptionId,ContractNumber,
                      StartDate,EndDate,MonthlySalary,IntegralSalaryPercentage,BankAccountReference,
                      BankOptionId,BankAccountTypeOptionId,BankAccountNumber,
                      IsActive,CreatedBy,CreatedAt)
                    VALUES(@Id,@TenantId,@PartyId,@BusinessId,@ResolvedEmployeeId,@ContractType,@SalaryType,@Frequency,
                      @RiskClass,@WorkerType,@WorkerSubtype,@PaymentMethod,@ContractNumber,@StartDate,@EndDate,
                      @Salary,@IntegralPercentage,@BankReference,@Bank,@BankAccountType,
                      @BankAccountNumber,@Active,@UserId,@Now);
                END
                """, connection, tx);
            AddEmploymentParameters(command, user, request);
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number is >= 51700 and <= 51705)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException("Ya existe un contrato o una relación activa para esa persona."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return await ReadEmploymentAsync(user, request.EmploymentId, ct);
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
                await command.ExecuteNonQueryAsync(ct);
            }
            foreach (var parameter in request.Parameters)
            {
                await using var insert = new SqlCommand("""
                    INSERT payroll.RuleParameters(RuleParameterId,RuleSetId,Code,NumericValue,UnitCode,Description)
                    VALUES(@ParameterId,@RuleSetId,@Code,@Value,@Unit,@Description);
                    """, connection, tx);
                insert.Parameters.AddWithValue("@ParameterId", ids.NewId());
                insert.Parameters.AddWithValue("@RuleSetId", request.RuleSetId);
                insert.Parameters.AddWithValue("@Code", parameter.Code);
                Decimal(insert, "@Value", parameter.NumericValue, 8);
                insert.Parameters.AddWithValue("@Unit", parameter.UnitCode);
                insert.Parameters.AddWithValue("@Description", (object?)parameter.Description ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number == 51720)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException("Ya existe un conjunto o parámetro con ese código y vigencia."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return (await GetOptionsAsync(user, ct)).RuleSets.Single(x => x.RuleSetId == request.RuleSetId);
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
            await update.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        catch (Exception error) when (error is PayrollConflictException or PayrollValidationException)
        { await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number == 51721)
        { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return (await GetOptionsAsync(user, ct)).RuleSets.Single(x => x.RuleSetId == ruleSetId);
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
        return (await GetOptionsAsync(user, ct)).RuleSets.Single(x => x.RuleSetId == ruleSetId);
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

    public async Task SaveNoveltyAsync(PayrollUserIdentity user, SavePayrollNoveltyRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
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
            INSERT payroll.Novelties(NoveltyId,TenantId,BusinessId,EmploymentId,ConceptId,NoveltyTypeOptionId,
              ReasonId,DeductionAgreementId,StartDate,EndDate,Quantity,UnitAmount,TotalAmount,Notes,EvidenceUrl,
              Status,CreatedBy,CreatedAt,ApprovedBy,ApprovedAt)
            VALUES(@Id,@TenantId,@BusinessId,@EmploymentId,@ConceptId,@TypeId,@ReasonId,@AgreementId,
              @StartDate,@EndDate,@Quantity,@UnitAmount,@TotalAmount,@Notes,@Evidence,N'Approved',@UserId,@Now,@UserId,@Now);
            """, connection);
        command.Parameters.AddWithValue("@Id", request.NoveltyId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@EmploymentId", request.EmploymentId);
        command.Parameters.AddWithValue("@ConceptId", request.ConceptId); command.Parameters.AddWithValue("@TypeId", request.NoveltyTypeOptionId);
        command.Parameters.AddWithValue("@ReasonId", (object?)request.ReasonId ?? DBNull.Value); command.Parameters.AddWithValue("@AgreementId", (object?)request.DeductionAgreementId ?? DBNull.Value);
        command.Parameters.AddWithValue("@StartDate", request.StartDate.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@EndDate", request.EndDate.ToDateTime(TimeOnly.MinValue));
        Decimal(command, "@Quantity", request.Quantity, 6); DecimalNullable(command, "@UnitAmount", request.UnitAmount, 4); Decimal(command, "@TotalAmount", request.TotalAmount, 4);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value); command.Parameters.AddWithValue("@Evidence", (object?)request.EvidenceUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@UserId", user.UserId); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number is >= 51730 and <= 51734) { throw new PayrollValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627) { throw new PayrollConflictException("La novedad ya existe."); }
    }

    public async Task<PayrollPaymentBatchView> CreatePaymentBatchAsync(
        PayrollUserIdentity user, CreatePayrollPaymentBatchRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var existing = new SqlCommand("""
                SELECT b.PaymentDate,b.PaymentMethodOptionId,o.Label,b.ReferenceNumber,b.Status,
                       COUNT(l.PaymentLineId),b.TotalAmount,b.RowVersion,
                       (SELECT TOP(1) re.PayrollRunId FROM payroll.PaymentLines pl
                        JOIN payroll.RunEmployees re ON re.PayrollRunEmployeeId=pl.PayrollRunEmployeeId
                        WHERE pl.PaymentBatchId=b.PaymentBatchId ORDER BY re.PayrollRunId)
                FROM payroll.PaymentBatches b
                JOIN payroll.CatalogOptions o ON o.OptionId=b.PaymentMethodOptionId
                LEFT JOIN payroll.PaymentLines l ON l.PaymentBatchId=b.PaymentBatchId
                WHERE b.PaymentBatchId=@BatchId AND b.TenantId=@TenantId AND b.BusinessId=@BusinessId
                GROUP BY b.PaymentDate,b.PaymentMethodOptionId,o.Label,b.ReferenceNumber,b.Status,
                         b.TotalAmount,b.RowVersion,b.PaymentBatchId;
                """, connection, tx))
            {
                existing.Parameters.AddWithValue("@BatchId", request.PaymentBatchId);
                existing.Parameters.AddWithValue("@TenantId", user.TenantId);
                existing.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await existing.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var existingRunId = reader.GetGuid(8);
                    if (existingRunId != request.PayrollRunId ||
                        DateOnly.FromDateTime(reader.GetDateTime(0)) != request.PaymentDate ||
                        reader.GetGuid(1) != request.PaymentMethodOptionId ||
                        !string.Equals(reader.GetString(3), request.ReferenceNumber, StringComparison.Ordinal))
                        throw new PayrollConflictException(
                            "El identificador del lote ya fue usado con otros datos.");
                    var replay = new PayrollPaymentBatchView(request.PaymentBatchId,
                        existingRunId, request.PaymentDate, request.PaymentMethodOptionId,
                        reader.GetString(2), reader.GetString(3), reader.GetString(4),
                        reader.GetInt32(5), reader.GetDecimal(6), (byte[])reader[7]);
                    await reader.DisposeAsync();
                    await tx.CommitAsync(ct);
                    return replay;
                }
            }
            DateOnly periodStart; DateOnly periodEnd; decimal total; string settlementCategory;
            var lines = new List<(Guid RunEmployeeId, Guid PartyId, decimal Amount, string Name)>();
            await using (var read = new SqlCommand("""
                SELECT r.PeriodStart,r.PeriodEnd,o.MetadataCode,re.PayrollRunEmployeeId,re.PartyId,
                       re.NetPayable,COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),p.Identification,N'Persona')
                FROM payroll.Runs r
                JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
                JOIN dbo.Parties p ON p.PartyId=re.PartyId AND p.TenantId=r.TenantId
                JOIN payroll.CatalogOptions o ON o.OptionId=@PaymentMethod
                  AND o.CatalogCode=N'payroll-payment-method' AND o.IsActive=1
                WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                  AND r.Status=N'Approved' AND re.NetPayable>0
                  AND NOT EXISTS(SELECT 1 FROM payroll.PaymentLines pl
                    JOIN payroll.PaymentBatches pb ON pb.PaymentBatchId=pl.PaymentBatchId
                    WHERE pl.PayrollRunEmployeeId=re.PayrollRunEmployeeId AND pb.Status<>N'Voided')
                ORDER BY re.PartyId;
                """, connection, tx))
            {
                read.Parameters.AddWithValue("@RunId", request.PayrollRunId);
                read.Parameters.AddWithValue("@TenantId", user.TenantId);
                read.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                read.Parameters.AddWithValue("@PaymentMethod", request.PaymentMethodOptionId);
                await using var reader = await read.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new PayrollConflictException("La liquidación no está aprobada, ya fue pagada o no tiene valores por pagar.");
                periodStart = DateOnly.FromDateTime(reader.GetDateTime(0));
                periodEnd = DateOnly.FromDateTime(reader.GetDateTime(1));
                settlementCategory = reader.IsDBNull(2)
                    ? throw new PayrollConflictException("El medio de pago no tiene categoría contable configurada.")
                    : reader.GetString(2);
                do
                {
                    lines.Add((reader.GetGuid(3), reader.GetGuid(4), reader.GetDecimal(5),
                        reader.GetString(6).Trim()));
                } while (await reader.ReadAsync(ct));
            }
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
                $"Pago de nómina {periodStart:yyyy-MM-dd} a {periodEnd:yyyy-MM-dd}", accountingLines);
            var json = PayrollContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            var jobId = ids.NewId(); var now = timeProvider.GetUtcNow();
            await using var save = new SqlCommand("""
                INSERT payroll.PaymentBatches(PaymentBatchId,TenantId,BusinessId,PaymentDate,
                  PaymentMethodOptionId,ReferenceNumber,Status,TotalAmount,CreatedBy,CreatedAt,
                  ConfirmedBy,ConfirmedAt)
                VALUES(@BatchId,@TenantId,@BusinessId,@PaymentDate,@PaymentMethod,@Reference,
                  N'Confirmed',@Total,@UserId,@Now,@UserId,@Now);
                INSERT payroll.PaymentLines(PaymentLineId,PaymentBatchId,PayrollRunEmployeeId,Amount)
                SELECT NEWID(),@BatchId,re.PayrollRunEmployeeId,re.NetPayable
                FROM payroll.RunEmployees re WHERE re.PayrollRunId=@RunId AND re.NetPayable>0
                  AND NOT EXISTS(SELECT 1 FROM payroll.PaymentLines pl
                    JOIN payroll.PaymentBatches pb ON pb.PaymentBatchId=pl.PaymentBatchId
                    WHERE pl.PayrollRunEmployeeId=re.PayrollRunEmployeeId AND pb.Status<>N'Voided');
                IF @@ROWCOUNT<>@EmployeeCount THROW 51735,N'Los empleados por pagar cambiaron durante la confirmación.',1;
                INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                  PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
                VALUES(@BatchId,N'PayrollPayment',@TenantId,@BusinessId,@Payload,@PayloadHash,@OccurredAt,@Now);
                INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                  SourceDocumentType,SourcePayloadHash,OccurredAt,Status,CreatedAt)
                VALUES(@JobId,@TenantId,@BusinessId,@BatchId,N'PayrollPayment',@PayloadHash,@OccurredAt,N'Pending',@Now);
                INSERT payroll.OutboxMessages(OutboxMessageId,TenantId,BusinessId,AggregateId,MessageType,
                  PayloadJson,OccurredAt,AttemptCount)
                VALUES(@OutboxId,@TenantId,@BusinessId,@BatchId,N'AccountingPostingRequested',@Payload,@Now,0);
                """, connection, tx);
            save.Parameters.AddWithValue("@BatchId", request.PaymentBatchId);
            save.Parameters.AddWithValue("@RunId", request.PayrollRunId);
            save.Parameters.AddWithValue("@TenantId", user.TenantId);
            save.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            save.Parameters.AddWithValue("@PaymentDate", request.PaymentDate.ToDateTime(TimeOnly.MinValue));
            save.Parameters.AddWithValue("@PaymentMethod", request.PaymentMethodOptionId);
            save.Parameters.AddWithValue("@Reference", request.ReferenceNumber);
            save.Parameters.AddWithValue("@Total", total);
            save.Parameters.AddWithValue("@EmployeeCount", lines.Count);
            save.Parameters.AddWithValue("@UserId", user.UserId);
            save.Parameters.AddWithValue("@Now", now);
            save.Parameters.AddWithValue("@Payload", json);
            save.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
            save.Parameters.AddWithValue("@OccurredAt", request.PaymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            save.Parameters.AddWithValue("@JobId", jobId);
            save.Parameters.AddWithValue("@OutboxId", ids.NewId());
            await save.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return new(request.PaymentBatchId, request.PayrollRunId, request.PaymentDate,
                request.PaymentMethodOptionId,
                "", request.ReferenceNumber, "Confirmed", lines.Count, total, []);
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
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51740,N'La empresa está fuera del tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM payroll.RuleSets WHERE RuleSetId=@RuleSetId AND (TenantId=@TenantId OR TenantId IS NULL)
              AND Status=N'Approved' AND EffectiveFrom<=@PeriodEnd AND (EffectiveTo IS NULL OR EffectiveTo>=@PeriodStart))
              THROW 51741,N'El conjunto de reglas no está aprobado o vigente.',1;
            IF NOT EXISTS(SELECT 1 FROM payroll.CatalogOptions WHERE OptionId=@Frequency AND CatalogCode=N'payroll-pay-frequency' AND IsActive=1)
              THROW 51742,N'La periodicidad no es válida.',1;
            IF @OriginalId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM payroll.Runs WHERE PayrollRunId=@OriginalId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Approved')
              THROW 51743,N'La liquidación original no está aprobada.',1;
            IF @OriginalId IS NOT NULL AND EXISTS(SELECT 1 FROM payroll.Runs WHERE PayrollRunId=@OriginalId
              AND (PeriodStart<>@PeriodStart OR PeriodEnd<>@PeriodEnd OR PayFrequencyOptionId<>@Frequency))
              THROW 51744,N'El ajuste debe conservar período y periodicidad de la liquidación original.',1;
            INSERT payroll.Runs(PayrollRunId,TenantId,BusinessId,RuleSetId,PayFrequencyOptionId,RunKind,
              OriginalPayrollRunId,PeriodStart,PeriodEnd,PaymentDate,Status,CalculationVersion,TotalEarnings,
              TotalDeductions,TotalEmployerContributions,TotalProvisions,NetPayable,CreatedBy,CreatedAt)
            VALUES(@Id,@TenantId,@BusinessId,@RuleSetId,@Frequency,@Kind,@OriginalId,@PeriodStart,@PeriodEnd,
              @PaymentDate,N'Draft',0,0,0,0,0,0,@UserId,@Now);
            """, connection);
        command.Parameters.AddWithValue("@Id", request.PayrollRunId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@RuleSetId", request.RuleSetId);
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

    public async Task<PayrollRunCalculationData> LoadCalculationDataAsync(PayrollUserIdentity user, Guid runId, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        Guid ruleSetId; DateOnly periodStart; DateOnly periodEnd; int expectedDays; bool exempt;
        await using (var run = new SqlCommand("""
            SELECT r.RuleSetId,r.PeriodStart,r.PeriodEnd,TRY_CONVERT(int,f.MetadataCode),
                   r.RunKind,
                   COALESCE(s.IsEmployerExemptFromHealthSenaIcbf,CAST(0 AS bit))
            FROM payroll.Runs r JOIN payroll.CatalogOptions f ON f.OptionId=r.PayFrequencyOptionId
            LEFT JOIN payroll.Settings s ON s.TenantId=r.TenantId
            WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId
              AND r.Status IN (N'Draft',N'Calculated');
            """, connection))
        {
            run.Parameters.AddWithValue("@RunId", runId); run.Parameters.AddWithValue("@TenantId", user.TenantId); run.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            await using var reader = await run.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new PayrollConflictException("La liquidación no existe o ya no admite cálculo.");
            ruleSetId = reader.GetGuid(0); periodStart = DateOnly.FromDateTime(reader.GetDateTime(1)); periodEnd = DateOnly.FromDateTime(reader.GetDateTime(2));
            expectedDays = reader.GetString(4) == PayrollRunKind.Adjustment.ToString()
                ? 0
                : reader.IsDBNull(3) ? periodEnd.DayNumber - periodStart.DayNumber + 1 : reader.GetInt32(3);
            exempt = reader.GetBoolean(5);
        }

        var rules = new Dictionary<string, decimal>(StringComparer.Ordinal);
        await using (var command = new SqlCommand("SELECT Code,NumericValue FROM payroll.RuleParameters WHERE RuleSetId=@Id;", connection))
        {
            command.Parameters.AddWithValue("@Id", ruleSetId); await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rules.Add(reader.GetString(0), reader.GetDecimal(1));
        }
        try { PayrollCalculator.ValidateRuleParameters(rules); }
        catch (PayrollCalculationException error) { throw new PayrollValidationException(error.Message); }

        var concepts = new Dictionary<Guid, PayrollConceptDefinition>();
        var systemConcepts = new Dictionary<string, PayrollConceptDefinition>(StringComparer.Ordinal);
        await using (var command = new SqlCommand("""
            SELECT c.ConceptId,c.Code,c.Name,n.Code,m.Code,a.Code,d.Code,s.Code,c.IsSalaryBase,
                   c.IsSocialSecurityBase,c.IsBenefitsBase,c.IsTaxWithholdingBase,c.RequiresDeductionAgreement
            FROM payroll.Concepts c JOIN payroll.CatalogOptions n ON n.OptionId=c.NatureOptionId
            JOIN payroll.CatalogOptions m ON m.OptionId=c.CalculationMethodOptionId
            JOIN payroll.CatalogOptions a ON a.OptionId=c.AccountingCategoryOptionId
            LEFT JOIN payroll.CatalogOptions d ON d.OptionId=c.DianConceptOptionId
            LEFT JOIN payroll.CatalogOptions s ON s.OptionId=c.SystemRoleOptionId
            WHERE c.TenantId=@TenantId AND c.IsActive=1 AND c.EffectiveFrom<=@End
              AND (c.EffectiveTo IS NULL OR c.EffectiveTo>=@Start);
            """, connection))
        {
            command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@Start", periodStart.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@End", periodEnd.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!Enum.TryParse<PayrollLineNature>(reader.GetString(3), false, out var nature))
                    throw new PayrollValidationException($"La naturaleza '{reader.GetString(3)}' no es compatible con el calculador.");
                var concept = new PayrollConceptDefinition(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), nature,
                    reader.GetString(4), reader.GetString(5), NullableString(reader, 6), NullableString(reader, 7),
                    reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10), reader.GetBoolean(11),
                    reader.GetBoolean(12));
                concepts.Add(concept.ConceptId, concept);
                if (concept.SystemRoleCode is not null) systemConcepts.Add(concept.SystemRoleCode, concept);
            }
        }

        var employees = new List<EmployeeSeed>();
        await using (var command = new SqlCommand("""
            SELECT e.EmploymentId,e.PartyId,e.MonthlySalary,e.StartDate,e.EndDate,st.Code,
                   TRY_CONVERT(decimal(19,8),risk.MetadataCode)
            FROM payroll.Employments e
            JOIN payroll.CatalogOptions st ON st.OptionId=e.SalaryTypeOptionId
            JOIN payroll.CatalogOptions risk ON risk.OptionId=e.RiskClassOptionId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId AND e.IsActive=1
              AND e.PayFrequencyOptionId=(SELECT PayFrequencyOptionId FROM payroll.Runs WHERE PayrollRunId=@RunId)
              AND e.StartDate<=@End AND (e.EndDate IS NULL OR e.EndDate>=@Start)
            ORDER BY e.EmploymentId;
            """, connection))
        {
            command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@Start", periodStart.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@End", periodEnd.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var employmentStart = DateOnly.FromDateTime(reader.GetDateTime(3));
                var employmentEnd = reader.IsDBNull(4) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(4));
                var start = Max(periodStart, employmentStart);
                var end = employmentEnd is null ? periodEnd : Min(periodEnd, employmentEnd.Value);
                var calendarDays = Math.Max(0, end.DayNumber - start.DayNumber + 1);
                var workedDays = employmentStart <= periodStart && (employmentEnd is null || employmentEnd >= periodEnd)
                    ? expectedDays : Math.Min(expectedDays, calendarDays);
                employees.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                    workedDays, reader.GetString(5) == "Integral",
                    reader.IsDBNull(6) ? throw new PayrollValidationException("La clase de riesgo no tiene tarifa configurada.") : reader.GetDecimal(6)));
            }
        }

        var noveltyLookup = employees.ToDictionary(x => x.EmploymentId, _ => new List<PayrollNoveltyInput>());
        await using (var command = new SqlCommand("""
            SELECT n.NoveltyId,n.EmploymentId,n.ConceptId,t.Code,n.Quantity,n.UnitAmount,n.TotalAmount,n.DeductionAgreementId,
              CASE WHEN a.DeductionAgreementId IS NOT NULL AND a.IsActive=1 AND LEN(a.EvidenceUrl)>0
                AND a.EffectiveFrom<=n.EndDate AND (a.EffectiveTo IS NULL OR a.EffectiveTo>=n.StartDate)
                AND (a.AuthorizedTotal IS NULL OR a.DeductedToDate+n.TotalAmount<=a.AuthorizedTotal) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
              auth.Code,a.MustProtectMinimumNetPay
            FROM payroll.Novelties n
            JOIN payroll.CatalogOptions t ON t.OptionId=n.NoveltyTypeOptionId
            LEFT JOIN payroll.DeductionAgreements a ON a.DeductionAgreementId=n.DeductionAgreementId
            LEFT JOIN payroll.CatalogOptions auth ON auth.OptionId=a.AuthorityOptionId
            WHERE n.TenantId=@TenantId AND n.BusinessId=@BusinessId AND n.Status=N'Approved'
              AND n.StartDate<=@End AND n.EndDate>=@Start;
            """, connection))
        {
            command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@Start", periodStart.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue("@End", periodEnd.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!noveltyLookup.TryGetValue(reader.GetGuid(1), out var list)) continue;
                if (!concepts.TryGetValue(reader.GetGuid(2), out var concept))
                    throw new PayrollValidationException("Una novedad usa un concepto inactivo o fuera de vigencia.");
                var authority = NullableString(reader, 9);
                list.Add(new(reader.GetGuid(0), concept, reader.GetString(3), reader.GetDecimal(4),
                    reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetBoolean(8),
                    reader.IsDBNull(10) ? authority is "WrittenAuthorization" : reader.GetBoolean(10)));
            }
        }

        var inputs = employees.Select(employee => new PayrollEmployeeCalculationInput(
            employee.EmploymentId, employee.PartyId, employee.MonthlySalary, employee.WorkedDays,
            employee.IsIntegral, exempt, employee.RiskRate, rules, systemConcepts,
            noveltyLookup[employee.EmploymentId])).ToArray();
        return new(runId, inputs);
    }

    public async Task<PayrollRunView> SaveCalculationAsync(PayrollUserIdentity user, Guid runId,
        IReadOnlyList<PayrollEmployeeCalculation> calculations, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            Guid ruleSetId;
            await using (var lockRun = new SqlCommand("""
                SELECT RuleSetId FROM payroll.Runs WITH(UPDLOCK,HOLDLOCK)
                WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND Status IN(N'Draft',N'Calculated');
                """, connection, tx))
            {
                lockRun.Parameters.AddWithValue("@RunId", runId); lockRun.Parameters.AddWithValue("@TenantId", user.TenantId); lockRun.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                ruleSetId = (Guid?)await lockRun.ExecuteScalarAsync(ct) ?? throw new PayrollConflictException("La liquidación ya no admite cálculo.");
            }
            await using (var clear = new SqlCommand("""
                DELETE l FROM payroll.RunLines l JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId WHERE e.PayrollRunId=@RunId;
                DELETE payroll.RunEmployees WHERE PayrollRunId=@RunId;
                """, connection, tx))
            { clear.Parameters.AddWithValue("@RunId", runId); await clear.ExecuteNonQueryAsync(ct); }
            var ruleSnapshot = await RuleSnapshotAsync(connection, tx, ruleSetId, ct);
            foreach (var calculation in calculations)
            {
                var employeeId = ids.NewId();
                var employeeSnapshot = JsonSerializer.Serialize(new { calculation.EmploymentId, calculation.PartyId });
                await using (var employee = new SqlCommand("""
                    INSERT payroll.RunEmployees(PayrollRunEmployeeId,TenantId,PayrollRunId,EmploymentId,PartyId,
                      EmployeeSnapshotJson,RuleSnapshotJson,WorkedDays,Earnings,Deductions,EmployerContributions,
                      Provisions,NetPayable,CalculationHash)
                    VALUES(@Id,@TenantId,@RunId,@EmploymentId,@PartyId,@EmployeeSnapshot,@RuleSnapshot,@Days,
                      @Earnings,@Deductions,@Employer,@Provisions,@Net,@Hash);
                    """, connection, tx))
                {
                    employee.Parameters.AddWithValue("@Id", employeeId); employee.Parameters.AddWithValue("@TenantId", user.TenantId); employee.Parameters.AddWithValue("@RunId", runId);
                    employee.Parameters.AddWithValue("@EmploymentId", calculation.EmploymentId); employee.Parameters.AddWithValue("@PartyId", calculation.PartyId);
                    employee.Parameters.AddWithValue("@EmployeeSnapshot", employeeSnapshot); employee.Parameters.AddWithValue("@RuleSnapshot", ruleSnapshot);
                    Decimal(employee, "@Days", calculation.WorkedDays, 4); Decimal(employee, "@Earnings", calculation.Earnings, 4); Decimal(employee, "@Deductions", calculation.Deductions, 4);
                    Decimal(employee, "@Employer", calculation.EmployerContributions, 4); Decimal(employee, "@Provisions", calculation.Provisions, 4); Decimal(employee, "@Net", calculation.NetPayable, 4);
                    employee.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = calculation.CalculationHash;
                    await employee.ExecuteNonQueryAsync(ct);
                }
                var lineNumber = 0;
                foreach (var line in calculation.Lines)
                {
                    await using var insertLine = new SqlCommand("""
                        INSERT payroll.RunLines(PayrollRunLineId,PayrollRunEmployeeId,ConceptId,NoveltyId,
                          DeductionAgreementId,LineNumber,NatureCode,ConceptCode,ConceptName,DianConceptCode,
                          AccountingCategoryCode,Quantity,Rate,BaseAmount,Amount,IsEmployerCost,IsSalaryBase)
                        VALUES(@Id,@EmployeeId,@ConceptId,@NoveltyId,@AgreementId,@Number,@Nature,@Code,@Name,
                          @Dian,@Category,@Quantity,@Rate,@Base,@Amount,@EmployerCost,@IsSalaryBase);
                        """, connection, tx);
                    insertLine.Parameters.AddWithValue("@Id", ids.NewId()); insertLine.Parameters.AddWithValue("@EmployeeId", employeeId); insertLine.Parameters.AddWithValue("@ConceptId", line.ConceptId);
                    insertLine.Parameters.AddWithValue("@NoveltyId", (object?)line.NoveltyId ?? DBNull.Value); insertLine.Parameters.AddWithValue("@AgreementId", (object?)line.DeductionAgreementId ?? DBNull.Value);
                    insertLine.Parameters.AddWithValue("@Number", ++lineNumber); insertLine.Parameters.AddWithValue("@Nature", line.Nature.ToString()); insertLine.Parameters.AddWithValue("@Code", line.ConceptCode);
                    insertLine.Parameters.AddWithValue("@Name", line.ConceptName); insertLine.Parameters.AddWithValue("@Dian", (object?)line.DianConceptCode ?? DBNull.Value); insertLine.Parameters.AddWithValue("@Category", line.AccountingCategoryCode);
                    Decimal(insertLine, "@Quantity", line.Quantity, 6); DecimalNullable(insertLine, "@Rate", line.Rate, 8); DecimalNullable(insertLine, "@Base", line.BaseAmount, 4); Decimal(insertLine, "@Amount", line.Amount, 4);
                    insertLine.Parameters.AddWithValue("@EmployerCost", line.IsEmployerCost);
                    insertLine.Parameters.AddWithValue("@IsSalaryBase", line.IsSalaryBase);
                    await insertLine.ExecuteNonQueryAsync(ct);
                }
            }
            var totalEarnings = calculations.Sum(x => x.Earnings); var totalDeductions = calculations.Sum(x => x.Deductions);
            var totalEmployer = calculations.Sum(x => x.EmployerContributions); var totalProvisions = calculations.Sum(x => x.Provisions); var totalNet = calculations.Sum(x => x.NetPayable);
            var inputHash = SHA256.HashData(calculations.SelectMany(x => x.CalculationHash).ToArray());
            await using var update = new SqlCommand("""
                UPDATE payroll.Runs SET Status=N'Calculated',CalculationVersion=CalculationVersion+1,
                  InputHash=@Hash,TotalEarnings=@Earnings,TotalDeductions=@Deductions,
                  TotalEmployerContributions=@Employer,TotalProvisions=@Provisions,NetPayable=@Net,CalculatedAt=@Now
                WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status IN(N'Draft',N'Calculated');
                IF @@ROWCOUNT<>1 THROW 51750,N'La liquidación cambió antes de guardar el cálculo.',1;
                """, connection, tx);
            update.Parameters.AddWithValue("@RunId", runId); update.Parameters.AddWithValue("@TenantId", user.TenantId); update.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            update.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = inputHash; Decimal(update, "@Earnings", totalEarnings, 4); Decimal(update, "@Deductions", totalDeductions, 4);
            Decimal(update, "@Employer", totalEmployer, 4); Decimal(update, "@Provisions", totalProvisions, 4); Decimal(update, "@Net", totalNet, 4); update.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await update.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number == 51750) { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return await GetRunAsync(user, runId, ct);
    }

    public async Task<PayrollRunAcceptance> ApproveRunAsync(PayrollUserIdentity user, Guid runId,
        string idempotencyKey, byte[] rowVersion, CancellationToken ct)
    {
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runId:D}|{idempotencyKey}"));
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            string kind; string status; string? existingKey; byte[]? existingHash; DateOnly start; DateOnly end; DateOnly payment;
            await using (var run = new SqlCommand("""
                SELECT RunKind,Status,ApprovalIdempotencyKey,ApprovalRequestHash,PeriodStart,PeriodEnd,PaymentDate
                FROM payroll.Runs WITH(UPDLOCK,HOLDLOCK)
                WHERE PayrollRunId=@RunId AND TenantId=@TenantId AND BusinessId=@BusinessId;
                """, connection, tx))
            {
                run.Parameters.AddWithValue("@RunId", runId); run.Parameters.AddWithValue("@TenantId", user.TenantId); run.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                await using var reader = await run.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La liquidación no existe.");
                kind = reader.GetString(0); status = reader.GetString(1); existingKey = NullableString(reader, 2); existingHash = reader.IsDBNull(3) ? null : (byte[])reader[3];
                start = DateOnly.FromDateTime(reader.GetDateTime(4)); end = DateOnly.FromDateTime(reader.GetDateTime(5)); payment = DateOnly.FromDateTime(reader.GetDateTime(6));
            }
            var documentType = kind == PayrollRunKind.Adjustment.ToString() ? PayrollAccountingDocumentTypes.Adjustment : PayrollAccountingDocumentTypes.Accrual;
            if (status == PayrollRunStatus.Approved.ToString())
            {
                if (existingKey != idempotencyKey || existingHash is null || !existingHash.AsSpan().SequenceEqual(requestHash))
                    throw new PayrollConflictException("La liquidación ya fue aprobada con otra operación.");
                var existingJob = await FindAccountingJobAsync(connection, tx, runId, documentType, ct);
                await tx.CommitAsync(ct); return new(runId, status, kind, existingJob, true);
            }
            if (status != PayrollRunStatus.Calculated.ToString()) throw new PayrollConflictException("La liquidación debe estar calculada antes de aprobar.");

            var accountingLines = await BuildAccountingLinesAsync(connection, tx, runId, ct);
            var description = $"Nómina {start:yyyy-MM-dd} a {end:yyyy-MM-dd}";
            var payload = new PayrollAccountingPayload(user.TenantId, user.BusinessId, runId, kind,
                start, end, payment, description, accountingLines);
            var json = PayrollContractSerializer.Serialize(payload); var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(json)); var jobId = ids.NewId(); var now = timeProvider.GetUtcNow();
            await using var approve = new SqlCommand("""
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

                INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                  PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
                VALUES(@RunId,@DocumentType,@TenantId,@BusinessId,@Payload,@PayloadHash,@OccurredAt,@Now);
                INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                  SourceDocumentType,SourcePayloadHash,OccurredAt,Status,CreatedAt)
                VALUES(@JobId,@TenantId,@BusinessId,@RunId,@DocumentType,@PayloadHash,@OccurredAt,N'Pending',@Now);
                INSERT payroll.OutboxMessages(OutboxMessageId,TenantId,BusinessId,AggregateId,MessageType,
                  PayloadJson,OccurredAt,AttemptCount)
                VALUES(@OutboxId,@TenantId,@BusinessId,@RunId,N'AccountingPostingRequested',@Payload,@Now,0);
                """, connection, tx);
            approve.Parameters.AddWithValue("@RunId", runId); approve.Parameters.AddWithValue("@TenantId", user.TenantId); approve.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            approve.Parameters.AddWithValue("@UserId", user.UserId); approve.Parameters.AddWithValue("@Now", now); approve.Parameters.AddWithValue("@Key", idempotencyKey);
            approve.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash; approve.Parameters.Add("@Version", SqlDbType.Timestamp).Value = rowVersion;
            approve.Parameters.AddWithValue("@DocumentType", documentType); approve.Parameters.AddWithValue("@Payload", json); approve.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
            approve.Parameters.AddWithValue("@OccurredAt", payment.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); approve.Parameters.AddWithValue("@JobId", jobId); approve.Parameters.AddWithValue("@OutboxId", ids.NewId());
            await approve.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return new(runId, PayrollRunStatus.Approved.ToString(), kind, jobId, false);
        }
        catch (Exception error) when (error is PayrollConflictException or PayrollNotFoundException)
        { await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number == 51760) { await tx.RollbackAsync(CancellationToken.None); throw new PayrollConflictException(error.Message); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
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
                SELECT re.PartyId,
                       COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador'),
                       COALESCE(p.Identification,N''),r.PayrollRunId,re.EmploymentId,
                       r.PaymentDate,re.WorkedDays,re.Earnings,re.Deductions,
                       re.EmployerContributions,re.Provisions,re.NetPayable,
                       COALESCE(idtype.DianCode,N''),COALESCE(p.FirstName,N''),COALESCE(p.LastName,N''),
                       e.ContractNumber,e.StartDate,e.EndDate,e.MonthlySalary,
                       CONVERT(bit,CASE WHEN salary.Code=N'Integral' THEN 1 ELSE 0 END),
                       COALESCE(contract.DianCode,N''),COALESCE(worker.DianCode,N''),
                       COALESCE(subtype.DianCode,N'00'),COALESCE(payment.DianCode,N''),
                       COALESCE(frequency.DianCode,N''),bank.Label,accountType.MetadataCode,
                       e.BankAccountNumber
                FROM payroll.Runs r
                JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
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
                WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                  AND r.Status=N'Approved' AND r.PeriodStart>=@Start AND r.PeriodEnd<=@End
                ORDER BY re.PartyId,r.PaymentDate,r.PayrollRunId;

                SELECT re.PartyId,r.PayrollRunId,re.EmploymentId,l.ConceptId,l.ConceptCode,
                       l.ConceptName,l.NatureCode,l.DianConceptCode,l.Quantity,l.Rate,
                       l.BaseAmount,l.Amount,l.IsEmployerCost,l.IsSalaryBase
                FROM payroll.Runs r
                JOIN payroll.RunEmployees re ON re.PayrollRunId=r.PayrollRunId
                JOIN payroll.RunLines l ON l.PayrollRunEmployeeId=re.PayrollRunEmployeeId
                WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
                  AND r.Status=N'Approved' AND r.PeriodStart>=@Start AND r.PeriodEnd<=@End
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
                    "No existen liquidaciones aprobadas completamente contenidas en el período.");

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
        await using var command = new SqlCommand("""
            SELECT r.PayrollRunId,r.BusinessId,r.RuleSetId,r.RunKind,r.OriginalPayrollRunId,
              r.PeriodStart,r.PeriodEnd,r.PaymentDate,r.Status,r.CalculationVersion,r.TotalEarnings,
              r.TotalDeductions,r.TotalEmployerContributions,r.TotalProvisions,r.NetPayable,r.RowVersion
            FROM payroll.Runs r WHERE r.PayrollRunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId;
            SELECT e.PayrollRunEmployeeId,e.EmploymentId,e.PartyId,
              COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),N'Trabajador'),
              e.WorkedDays,e.Earnings,e.Deductions,e.EmployerContributions,e.Provisions,e.NetPayable
            FROM payroll.RunEmployees e JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            WHERE e.PayrollRunId=@RunId ORDER BY 4,e.EmploymentId;
            SELECT e.PayrollRunEmployeeId,l.LineNumber,l.ConceptId,l.ConceptCode,l.ConceptName,l.NatureCode,
              l.DianConceptCode,l.AccountingCategoryCode,l.Quantity,l.Rate,l.BaseAmount,l.Amount,l.IsEmployerCost
            FROM payroll.RunLines l JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
            WHERE e.PayrollRunId=@RunId ORDER BY e.PayrollRunEmployeeId,l.LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@RunId", runId); command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La liquidación no existe.");
        var header = new RunHeader(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), DateOnly.FromDateTime(reader.GetDateTime(5)), DateOnly.FromDateTime(reader.GetDateTime(6)),
            DateOnly.FromDateTime(reader.GetDateTime(7)), reader.GetString(8), reader.GetInt32(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13), reader.GetDecimal(14), (byte[])reader[15]);
        await reader.NextResultAsync(ct);
        var employeeRows = new List<EmployeeRow>();
        while (await reader.ReadAsync(ct)) employeeRows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3).Trim(), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9)));
        await reader.NextResultAsync(ct);
        var lineLookup = employeeRows.ToDictionary(x => x.Id, _ => new List<PayrollRunLineView>());
        while (await reader.ReadAsync(ct))
            lineLookup[reader.GetGuid(0)].Add(new(reader.GetInt32(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), NullableString(reader, 6), reader.GetString(7), reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10), reader.GetDecimal(11), reader.GetBoolean(12)));
        var employees = employeeRows.Select(x => new PayrollRunEmployeeView(x.Id, x.EmploymentId, x.PartyId, x.Name,
            x.Days, x.Earnings, x.Deductions, x.Employer, x.Provisions, x.Net, lineLookup[x.Id])).ToArray();
        return new(header.Id, header.BusinessId, header.RuleSetId, header.Kind, header.OriginalId, header.Start, header.End,
            header.Payment, header.Status, header.Version, header.Earnings, header.Deductions, header.Employer,
            header.Provisions, header.Net, employees, header.RowVersion);
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

    private async Task<PayrollEmploymentView> ReadEmploymentAsync(PayrollUserIdentity user, Guid id, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT e.EmploymentId,e.PartyId,e.BusinessId,e.EmployeeId,
              COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName),e.ContractNumber),
              e.ContractTypeOptionId,e.SalaryTypeOptionId,e.PayFrequencyOptionId,e.RiskClassOptionId,
              e.WorkerTypeOptionId,e.WorkerSubtypeOptionId,e.PaymentMethodOptionId,e.ContractNumber,
              e.StartDate,e.EndDate,e.MonthlySalary,e.IntegralSalaryPercentage,e.BankAccountReference,
              e.BankOptionId,e.BankAccountTypeOptionId,e.BankAccountNumber,e.IsActive,e.RowVersion
            FROM payroll.Employments e JOIN dbo.Parties p ON p.PartyId=e.PartyId AND p.TenantId=e.TenantId
            WHERE e.EmploymentId=@Id AND e.TenantId=@TenantId AND e.BusinessId=@BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@Id", id); command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new PayrollNotFoundException("La relación laboral no existe.");
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4).Trim(),
            reader.GetGuid(5), reader.GetGuid(6), reader.GetGuid(7), reader.GetGuid(8), reader.GetGuid(9), reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.GetGuid(11), reader.GetString(12),
            DateOnly.FromDateTime(reader.GetDateTime(13)), reader.IsDBNull(14) ? null : DateOnly.FromDateTime(reader.GetDateTime(14)), reader.GetDecimal(15), reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            NullableString(reader, 17), reader.IsDBNull(18) ? null : reader.GetGuid(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19), NullableString(reader, 20),
            reader.GetBoolean(21), (byte[])reader[22]);
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

    private static async Task<string> RuleSnapshotAsync(SqlConnection connection, SqlTransaction tx, Guid ruleSetId, CancellationToken ct)
    {
        var values = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        await using var command = new SqlCommand("SELECT Code,NumericValue FROM payroll.RuleParameters WHERE RuleSetId=@Id ORDER BY Code;", connection, tx);
        command.Parameters.AddWithValue("@Id", ruleSetId); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) values.Add(reader.GetString(0), reader.GetDecimal(1));
        return JsonSerializer.Serialize(values);
    }

    private static async Task<IReadOnlyList<PayrollAccountingLine>> BuildAccountingLinesAsync(SqlConnection connection, SqlTransaction tx, Guid runId, CancellationToken ct)
    {
        var values = new List<PayrollAccountingLine>();
        await using var command = new SqlCommand("""
            SELECT e.PartyId,l.NatureCode,l.AccountingCategoryCode,l.ConceptName,SUM(l.Amount)
            FROM payroll.RunLines l JOIN payroll.RunEmployees e ON e.PayrollRunEmployeeId=l.PayrollRunEmployeeId
            WHERE e.PayrollRunId=@RunId GROUP BY e.PartyId,l.NatureCode,l.AccountingCategoryCode,l.ConceptName
            ORDER BY e.PartyId,l.NatureCode,l.AccountingCategoryCode,l.ConceptName;
            SELECT PartyId,NetPayable FROM payroll.RunEmployees WHERE PayrollRunId=@RunId ORDER BY PartyId;
            """, connection, tx);
        command.Parameters.AddWithValue("@RunId", runId); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var party = reader.GetGuid(0); var nature = reader.GetString(1); var category = reader.GetString(2); var name = reader.GetString(3); var amount = reader.GetDecimal(4);
            if (amount == 0) continue;
            switch (nature)
            {
                case "Earning": values.Add(new(category, amount, 0, party, name)); break;
                case "Deduction": values.Add(new(category, 0, amount, party, name)); break;
                case "EmployerContribution":
                    values.Add(new("EmployerContributionsExpense", amount, 0, party, name));
                    values.Add(new(category, 0, amount, party, name)); break;
                case "Provision":
                    values.Add(new("BenefitsExpense", amount, 0, party, name));
                    values.Add(new(category, 0, amount, party, name)); break;
                default: throw new PayrollValidationException($"La naturaleza contable '{nature}' no es válida.");
            }
        }
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            if (reader.GetDecimal(1) > 0) values.Add(new(PayrollAccountingCategories.NetPayable, 0, reader.GetDecimal(1), reader.GetGuid(0), "Neto de nómina por pagar"));
        if (values.Count == 0 || decimal.Round(values.Sum(x => x.Debit), 4) != decimal.Round(values.Sum(x => x.Credit), 4))
            throw new PayrollValidationException("La fuente contable de nómina no está balanceada.");
        return values;
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
    private static string? NullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static object DbDate(DateOnly? value) => value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue);
    private static void Decimal(SqlCommand command, string name, decimal value, byte scale) { var p = command.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = scale; p.Value = value; }
    private static void DecimalNullable(SqlCommand command, string name, decimal? value, byte scale) { var p = command.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = scale; p.Value = (object?)value ?? DBNull.Value; }
    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;

    private sealed record EmployeeSeed(Guid EmploymentId, Guid PartyId, decimal MonthlySalary, decimal WorkedDays, bool IsIntegral, decimal RiskRate);
    private sealed record RunHeader(Guid Id, Guid BusinessId, Guid RuleSetId, string Kind, Guid? OriginalId, DateOnly Start, DateOnly End, DateOnly Payment, string Status, int Version, decimal Earnings, decimal Deductions, decimal Employer, decimal Provisions, decimal Net, byte[] RowVersion);
    private sealed record EmployeeRow(Guid Id, Guid EmploymentId, Guid PartyId, string Name, decimal Days, decimal Earnings, decimal Deductions, decimal Employer, decimal Provisions, decimal Net);
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
