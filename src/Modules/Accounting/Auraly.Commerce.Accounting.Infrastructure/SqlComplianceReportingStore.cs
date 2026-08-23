using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlComplianceReportingStore(
    AccountingSqlConnectionFactory connections,
    TimeProvider timeProvider) : IComplianceReportingStore
{
    public async Task<IReadOnlyList<ComplianceReportDefinitionView>> ListDefinitionsAsync(
        AccountingUserIdentity user, short? taxYear, CancellationToken token)
    {
        _ = user;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT AuthorityCode,TaxYear,FormatCode,FormatVersion,Name,ReportKind,
                   ResolutionNumber,ResolutionDate,TechnicalAnnex,SourceUrl,SourceSha256
            FROM compliance.ComplianceReportDefinitions
            WHERE IsActive=1 AND (@TaxYear IS NULL OR TaxYear=@TaxYear)
            ORDER BY TaxYear DESC,ReportKind,FormatCode;
            """, connection);
        command.Parameters.AddWithValue("@TaxYear", (object?)taxYear ?? DBNull.Value);
        var result = new List<ComplianceReportDefinitionView>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(ReadDefinition(reader));
        return result;
    }

    public async Task<IReadOnlyList<ComplianceConceptMappingView>> ListMappingsAsync(
        AccountingUserIdentity user, short taxYear, string? formatCode, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT m.MappingId,m.TenantId,m.BusinessId,m.AuthorityCode,m.TaxYear,
                   m.FormatCode,m.FormatVersion,m.AccountId,a.Code,a.Name,
                   m.ConceptCode,m.TargetField
            FROM compliance.ComplianceConceptMappings m
            INNER JOIN dbo.AccountingAccounts a ON a.TenantId=m.TenantId AND a.AccountId=m.AccountId
            WHERE m.TenantId=@TenantId AND (m.BusinessId IS NULL OR m.BusinessId=@BusinessId)
              AND m.TaxYear=@TaxYear AND (@FormatCode IS NULL OR m.FormatCode=@FormatCode)
            ORDER BY m.FormatCode,a.Code,m.ConceptCode,m.TargetField;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TaxYear", taxYear);
        command.Parameters.AddWithValue("@FormatCode", (object?)formatCode ?? DBNull.Value);
        var result = new List<ComplianceConceptMappingView>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(ReadMapping(reader));
        return result;
    }

    public async Task<ComplianceConceptMappingView> SetMappingAsync(
        AccountingUserIdentity user, Guid mappingId, SetComplianceConceptMappingRequest request,
        CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM compliance.ComplianceReportDefinitions
              WHERE AuthorityCode=@AuthorityCode AND TaxYear=@TaxYear AND FormatCode=@FormatCode
                AND FormatVersion=@FormatVersion AND IsActive=1)
              THROW 51500,'Unknown compliance report definition.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts
              WHERE TenantId=@TenantId AND AccountId=@AccountId AND IsActive=1 AND AllowsPosting=1)
              THROW 51501,'Unknown posting account.',1;
            IF @BusinessId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Businesses
              WHERE TenantId=@TenantId AND BusinessId=@BusinessId)
              THROW 51502,'Invalid business scope.',1;

            DECLARE @Existing UNIQUEIDENTIFIER=(SELECT TOP(1) MappingId
              FROM compliance.ComplianceConceptMappings WITH(UPDLOCK,HOLDLOCK)
              WHERE TenantId=@TenantId AND ((BusinessId=@BusinessId) OR (BusinessId IS NULL AND @BusinessId IS NULL))
                AND AuthorityCode=@AuthorityCode AND TaxYear=@TaxYear AND FormatCode=@FormatCode
                AND FormatVersion=@FormatVersion AND AccountId=@AccountId
                AND ConceptCode=@ConceptCode AND TargetField=@TargetField);
            IF @Existing IS NULL
            BEGIN
              INSERT compliance.ComplianceConceptMappings(MappingId,TenantId,BusinessId,AuthorityCode,
                TaxYear,FormatCode,FormatVersion,AccountId,ConceptCode,TargetField,CreatedAt,UpdatedAt)
              VALUES(@MappingId,@TenantId,@BusinessId,@AuthorityCode,@TaxYear,@FormatCode,
                @FormatVersion,@AccountId,@ConceptCode,@TargetField,@Now,@Now);
              SET @Existing=@MappingId;
            END
            ELSE UPDATE compliance.ComplianceConceptMappings SET UpdatedAt=@Now WHERE MappingId=@Existing;

            SELECT m.MappingId,m.TenantId,m.BusinessId,m.AuthorityCode,m.TaxYear,
                   m.FormatCode,m.FormatVersion,m.AccountId,a.Code,a.Name,m.ConceptCode,m.TargetField
            FROM compliance.ComplianceConceptMappings m
            INNER JOIN dbo.AccountingAccounts a ON a.TenantId=m.TenantId AND a.AccountId=m.AccountId
            WHERE m.MappingId=@Existing;
            """, connection);
        AddMappingParameters(command, user, mappingId, request, timeProvider.GetUtcNow());
        try
        {
            await using var reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) return ReadMapping(reader);
            throw new AccountingConflictException("The compliance mapping was not saved.");
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 or 51500 or 51501 or 51502)
        {
            throw new AccountingConflictException(exception.Message);
        }
    }

    public async Task<ComplianceReportRunView> GenerateAsync(
        AccountingUserIdentity user, Guid runId, GenerateComplianceReportRequest request,
        CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, token);
        try
        {
            var definition = await ReadDefinitionAsync(connection, transaction, request, token)
                ?? throw new AccountingValidationException("The requested authority/year/version definition does not exist.");
            if (!await IsAccountingReadyAsync(connection, transaction, user.TenantId, token))
                throw new AccountingConflictException("Accounting must be Ready before a fiscal report can be generated.");

            var mappings = await ReadMappingsAsync(connection, transaction, user, request, token);
            var validations = new List<ComplianceValidationView>();
            if (mappings.Count == 0)
                validations.Add(new("Error", "CONCEPT_MAPPING_REQUIRED",
                    "Configure at least one account, concept and target-field mapping for this format.", null, null));

            var source = mappings.Count == 0
                ? []
                : await ReadSourceRowsAsync(connection, transaction, user, request, token);
            var rows = BuildRows(source, definition.ReportKind, validations);
            var status = validations.Any(value => value.Severity == "Error") ? "Blocked" : "Ready";
            var csv = status == "Ready" ? BuildCsv(definition, request, rows) : null;
            var now = timeProvider.GetUtcNow();
            var mappingJson = JsonSerializer.Serialize(mappings.Select(value => new
            {
                value.MappingId, value.AccountId, value.AccountCode, value.ConceptCode, value.TargetField,
                Scope = value.BusinessId.HasValue ? "Business" : "Tenant"
            }));
            await InsertRunAsync(connection, transaction, user, runId, request, definition,
                status, mappingJson, rows, validations, csv, now, token);
            await transaction.CommitAsync(token);
            return new(runId, definition.AuthorityCode, definition.TaxYear,
                definition.FormatCode, definition.FormatVersion, definition.Name,
                definition.ReportKind, request.PeriodFrom, request.PeriodTo, status,
                definition.ResolutionNumber, definition.SourceUrl, definition.SourceSha256,
                rows.Count, rows.Sum(value => value.ControlAmount), now, validations);
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    public async Task<IReadOnlyList<ComplianceReportRunView>> ListRunsAsync(
        AccountingUserIdentity user, short? taxYear, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT r.RunId,r.AuthorityCode,r.TaxYear,r.FormatCode,r.FormatVersion,d.Name,d.ReportKind,
                   r.PeriodFrom,r.PeriodTo,r.Status,r.ResolutionNumber,r.SourceUrl,r.SourceSha256,
                   r.[RowCount],r.ControlTotal,r.CreatedAt
            FROM compliance.ComplianceReportRuns r
            INNER JOIN compliance.ComplianceReportDefinitions d
              ON d.AuthorityCode=r.AuthorityCode AND d.TaxYear=r.TaxYear
             AND d.FormatCode=r.FormatCode AND d.FormatVersion=r.FormatVersion
            WHERE r.TenantId=@TenantId AND r.BusinessId=@BusinessId
              AND (@TaxYear IS NULL OR r.TaxYear=@TaxYear)
            ORDER BY r.CreatedAt DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TaxYear", (object?)taxYear ?? DBNull.Value);
        var runs = new List<ComplianceReportRunView>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) runs.Add(ReadRun(reader, []));
        await reader.DisposeAsync();
        foreach (var run in runs.ToArray())
        {
            var validations = await ReadValidationsAsync(connection, run.RunId, token);
            runs[runs.IndexOf(run)] = run with { Validations = validations };
        }
        return runs;
    }

    public async Task<ComplianceReportArtifact?> GetArtifactAsync(
        AccountingUserIdentity user, Guid runId, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT a.FileName,a.MediaType,a.Content,a.ContentSha256
            FROM compliance.ComplianceReportArtifacts a
            INNER JOIN compliance.ComplianceReportRuns r ON r.RunId=a.RunId
            WHERE a.RunId=@RunId AND r.TenantId=@TenantId AND r.BusinessId=@BusinessId
              AND r.Status=N'Ready';
            """, connection);
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(runId, reader.GetString(0), reader.GetString(1), (byte[])reader[2],
                Convert.ToHexString((byte[])reader[3]))
            : null;
    }

    private static async Task<ComplianceReportDefinitionView?> ReadDefinitionAsync(
        SqlConnection connection, SqlTransaction transaction,
        GenerateComplianceReportRequest request, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT AuthorityCode,TaxYear,FormatCode,FormatVersion,Name,ReportKind,
                   ResolutionNumber,ResolutionDate,TechnicalAnnex,SourceUrl,SourceSha256
            FROM compliance.ComplianceReportDefinitions
            WHERE AuthorityCode=@Authority AND TaxYear=@TaxYear AND FormatCode=@Format
              AND FormatVersion=@Version AND IsActive=1;
            """, connection, transaction);
        AddDefinitionScope(command, request);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadDefinition(reader) : null;
    }

    private static async Task<bool> IsAccountingReadyAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1) FROM dbo.AccountingTenantSettings
            WHERE TenantId=@TenantId AND Status=N'Ready';
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token)) == 1;
    }

    private static async Task<List<ComplianceConceptMappingView>> ReadMappingsAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        GenerateComplianceReportRequest request, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT m.MappingId,m.TenantId,m.BusinessId,m.AuthorityCode,m.TaxYear,
                   m.FormatCode,m.FormatVersion,m.AccountId,a.Code,a.Name,m.ConceptCode,m.TargetField
            FROM compliance.ComplianceConceptMappings m
            INNER JOIN dbo.AccountingAccounts a ON a.TenantId=m.TenantId AND a.AccountId=m.AccountId
            WHERE m.TenantId=@TenantId AND (m.BusinessId IS NULL OR m.BusinessId=@BusinessId)
              AND m.AuthorityCode=@Authority AND m.TaxYear=@TaxYear AND m.FormatCode=@Format
              AND m.FormatVersion=@Version
              AND NOT EXISTS(SELECT 1 FROM compliance.ComplianceConceptMappings override
                WHERE override.TenantId=m.TenantId AND override.BusinessId=@BusinessId
                  AND m.BusinessId IS NULL AND override.AuthorityCode=m.AuthorityCode
                  AND override.TaxYear=m.TaxYear AND override.FormatCode=m.FormatCode
                  AND override.FormatVersion=m.FormatVersion AND override.AccountId=m.AccountId
                  AND override.ConceptCode=m.ConceptCode AND override.TargetField=m.TargetField)
            ORDER BY a.Code,m.ConceptCode,m.TargetField;
            """, connection, transaction);
        AddScope(command, user, request);
        var result = new List<ComplianceConceptMappingView>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(ReadMapping(reader));
        return result;
    }

    private static async Task<List<SourceRow>> ReadSourceRowsAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        GenerateComplianceReportRequest request, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT l.PartyId,m.ConceptCode,m.TargetField,
              CASE WHEN a.AccountType IN(N'Asset',N'Expense',N'ContraRevenue')
                   THEN l.Debit-l.Credit ELSE l.Credit-l.Debit END AS Amount,
              p.IdentificationTypeCode,p.NormalizedIdentification,p.VerificationDigit,
              p.FirstName,p.LastName,p.LegalName,p.DisplayName,p.CompletionStatus,
              s.AddressLine,d.Code,c.Code,co.Code
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            INNER JOIN compliance.ComplianceConceptMappings m
              ON m.TenantId=e.TenantId AND (m.BusinessId=e.BusinessId OR m.BusinessId IS NULL)
             AND m.AuthorityCode=@Authority AND m.TaxYear=@TaxYear AND m.FormatCode=@Format
             AND m.FormatVersion=@Version AND m.AccountId=l.AccountId
            LEFT JOIN dbo.Parties p ON p.PartyId=l.PartyId AND p.TenantId=e.TenantId
            OUTER APPLY(SELECT TOP(1) ps.AddressLine,ps.AdministrativeDivisionId,ps.CityId,ps.CountryId
              FROM dbo.PartySites ps WHERE ps.PartyId=p.PartyId AND ps.IsActive=1
              ORDER BY ps.IsPrimary DESC,ps.CreatedAt) s
            LEFT JOIN dbo.AdministrativeDivisions d ON d.AdministrativeDivisionId=s.AdministrativeDivisionId
            LEFT JOIN dbo.Cities c ON c.CityId=s.CityId
            LEFT JOIN dbo.Countries co ON co.CountryId=s.CountryId
            WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
              AND e.OccurredAt>=@From AND e.OccurredAt<DATEADD(day,1,@To)
              AND NOT EXISTS(SELECT 1 FROM compliance.ComplianceConceptMappings override
                WHERE override.TenantId=m.TenantId AND override.BusinessId=e.BusinessId
                  AND m.BusinessId IS NULL AND override.AuthorityCode=m.AuthorityCode
                  AND override.TaxYear=m.TaxYear AND override.FormatCode=m.FormatCode
                  AND override.FormatVersion=m.FormatVersion AND override.AccountId=m.AccountId
                  AND override.ConceptCode=m.ConceptCode AND override.TargetField=m.TargetField);
            """, connection, transaction);
        AddScope(command, user, request);
        command.Parameters.AddWithValue("@From", request.PeriodFrom.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", request.PeriodTo.ToDateTime(TimeOnly.MinValue));
        var result = new List<SourceRow>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(
                reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), Text(reader,4), Text(reader,5), Text(reader,6), Text(reader,7),
                Text(reader,8), Text(reader,9), Text(reader,10), Text(reader,11), Text(reader,12),
                Text(reader,13), Text(reader,14), Text(reader,15)));
        return result;
    }

    private static List<GeneratedRow> BuildRows(
        IReadOnlyList<SourceRow> source, string reportKind,
        List<ComplianceValidationView> validations)
    {
        var groups = source.GroupBy(value => new
        {
            value.PartyId, value.ConceptCode, value.DocumentType, value.Identification,
            value.VerificationDigit, value.FirstName, value.LastName, value.LegalName,
            value.DisplayName, value.CompletionStatus, value.Address, value.DepartmentCode,
            value.CityCode, value.CountryCode
        }).OrderBy(value => value.Key.ConceptCode).ThenBy(value => value.Key.Identification).ToArray();
        var rows = new List<GeneratedRow>(groups.Length);
        foreach (var group in groups)
        {
            if (reportKind == "Exogenous")
            {
                if (group.Key.PartyId is null)
                    validations.Add(new("Error", "PARTY_REQUIRED",
                        $"Concept {group.Key.ConceptCode} contains accounting lines without a third party.", null, null));
                else
                {
                    if (string.IsNullOrWhiteSpace(group.Key.Identification) ||
                        string.IsNullOrWhiteSpace(group.Key.DocumentType) || group.Key.CompletionStatus != "Complete")
                        validations.Add(new("Error", "PARTY_FISCAL_IDENTITY_INCOMPLETE",
                            "The third party lacks a complete fiscal identity.", group.Key.PartyId, null));
                    if (string.IsNullOrWhiteSpace(group.Key.Address) ||
                        string.IsNullOrWhiteSpace(group.Key.CountryCode))
                        validations.Add(new("Error", "PARTY_FISCAL_ADDRESS_INCOMPLETE",
                            "The third party lacks a primary fiscal address.", group.Key.PartyId, null));
                }
            }
            var values = group.GroupBy(value => value.TargetField, StringComparer.Ordinal)
                .ToDictionary(value => value.Key,
                    value => decimal.Round(value.Sum(item => item.Amount), 0, MidpointRounding.AwayFromZero),
                    StringComparer.Ordinal);
            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["conceptCode"] = group.Key.ConceptCode,
                ["documentType"] = group.Key.DocumentType,
                ["identification"] = group.Key.Identification,
                ["verificationDigit"] = group.Key.VerificationDigit,
                ["firstName"] = group.Key.FirstName,
                ["lastName"] = group.Key.LastName,
                ["legalName"] = group.Key.LegalName ?? group.Key.DisplayName,
                ["address"] = group.Key.Address,
                ["departmentCode"] = group.Key.DepartmentCode,
                ["cityCode"] = group.Key.CityCode,
                ["countryCode"] = group.Key.CountryCode
            };
            foreach (var value in values) payload[value.Key] = value.Value;
            rows.Add(new(group.Key.PartyId, group.Key.ConceptCode,
                JsonSerializer.Serialize(payload), values.Values.Sum()));
        }
        return rows;
    }

    private static byte[] BuildCsv(
        ComplianceReportDefinitionView definition, GenerateComplianceReportRequest request,
        IReadOnlyList<GeneratedRow> rows)
    {
        var parsed = rows.Select(row => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Json)!).ToArray();
        var columns = parsed.SelectMany(value => value.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"# Authority={definition.AuthorityCode};TaxYear={definition.TaxYear};Format={definition.FormatCode};Version={definition.FormatVersion};Resolution={definition.ResolutionNumber};From={request.PeriodFrom:yyyy-MM-dd};To={request.PeriodTo:yyyy-MM-dd}");
        builder.AppendLine(string.Join(',', columns.Select(Escape)));
        foreach (var row in parsed)
            builder.AppendLine(string.Join(',', columns.Select(column =>
                Escape(row.TryGetValue(column, out var value) ? JsonValue(value) : string.Empty))));
        return new UTF8Encoding(true).GetBytes(builder.ToString());
    }

    private static async Task InsertRunAsync(
        SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        Guid runId, GenerateComplianceReportRequest request, ComplianceReportDefinitionView definition,
        string status, string mappingJson, IReadOnlyList<GeneratedRow> rows,
        IReadOnlyList<ComplianceValidationView> validations, byte[]? csv,
        DateTimeOffset now, CancellationToken token)
    {
        await using (var command = new SqlCommand("""
            INSERT compliance.ComplianceReportRuns(RunId,TenantId,BusinessId,AuthorityCode,TaxYear,
              FormatCode,FormatVersion,PeriodFrom,PeriodTo,Status,ResolutionNumber,SourceUrl,
              SourceSha256,MappingSnapshotJson,[RowCount],ControlTotal,CreatedByUserId,CreatedAt,CompletedAt)
            VALUES(@RunId,@TenantId,@BusinessId,@Authority,@TaxYear,@Format,@Version,@From,@To,
              @Status,@Resolution,@SourceUrl,@SourceHash,@Mappings,@RowCount,@ControlTotal,@UserId,@Now,@Now);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@Authority", request.AuthorityCode);
            command.Parameters.AddWithValue("@TaxYear", request.TaxYear);
            command.Parameters.AddWithValue("@Format", request.FormatCode);
            command.Parameters.AddWithValue("@Version", request.FormatVersion);
            command.Parameters.AddWithValue("@From", request.PeriodFrom.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@To", request.PeriodTo.ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@Resolution", definition.ResolutionNumber);
            command.Parameters.AddWithValue("@SourceUrl", definition.SourceUrl);
            command.Parameters.AddWithValue("@SourceHash", definition.SourceSha256);
            command.Parameters.AddWithValue("@Mappings", mappingJson);
            command.Parameters.AddWithValue("@RowCount", rows.Count);
            command.Parameters.AddWithValue("@ControlTotal", rows.Sum(value => value.ControlAmount));
            command.Parameters.AddWithValue("@UserId", user.UserId);
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(token);
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            await using var command = new SqlCommand("""
                INSERT compliance.ComplianceReportRows(RunId,RowNumber,PartyId,ConceptCode,RowJson,ControlAmount)
                VALUES(@RunId,@Number,@PartyId,@Concept,@Json,@Amount);
                """, connection, transaction);
            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@Number", index + 1);
            command.Parameters.AddWithValue("@PartyId", (object?)row.PartyId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Concept", row.ConceptCode);
            command.Parameters.AddWithValue("@Json", row.Json);
            command.Parameters.AddWithValue("@Amount", row.ControlAmount);
            await command.ExecuteNonQueryAsync(token);
        }
        for (var index = 0; index < validations.Count; index++)
        {
            var validation = validations[index];
            await using var command = new SqlCommand("""
                INSERT compliance.ComplianceReportValidations(RunId,ValidationNumber,Severity,Code,Message,PartyId,AccountId)
                VALUES(@RunId,@Number,@Severity,@Code,@Message,@PartyId,@AccountId);
                """, connection, transaction);
            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@Number", index + 1);
            command.Parameters.AddWithValue("@Severity", validation.Severity);
            command.Parameters.AddWithValue("@Code", validation.Code);
            command.Parameters.AddWithValue("@Message", validation.Message);
            command.Parameters.AddWithValue("@PartyId", (object?)validation.PartyId ?? DBNull.Value);
            command.Parameters.AddWithValue("@AccountId", (object?)validation.AccountId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(token);
        }
        if (csv is not null)
        {
            var name = $"{definition.AuthorityCode}-{definition.TaxYear}-{definition.FormatCode}-v{definition.FormatVersion}-{runId:N}.csv";
            await using var command = new SqlCommand("""
                INSERT compliance.ComplianceReportArtifacts(RunId,FileName,MediaType,Content,ContentSha256,CreatedAt)
                VALUES(@RunId,@Name,N'text/csv; charset=utf-8',@Content,@Hash,@Now);
                """, connection, transaction);
            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.Add("@Content", SqlDbType.VarBinary, -1).Value = csv;
            command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = SHA256.HashData(csv);
            command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(token);
        }
    }

    private static async Task<IReadOnlyList<ComplianceValidationView>> ReadValidationsAsync(
        SqlConnection connection, Guid runId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT Severity,Code,Message,PartyId,AccountId
            FROM compliance.ComplianceReportValidations WHERE RunId=@RunId ORDER BY ValidationNumber;
            """, connection);
        command.Parameters.AddWithValue("@RunId", runId);
        var result = new List<ComplianceValidationView>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetGuid(3),reader.IsDBNull(4)?null:reader.GetGuid(4)));
        return result;
    }

    private static ComplianceReportRunView ReadRun(SqlDataReader reader, IReadOnlyList<ComplianceValidationView> validations) => new(
        reader.GetGuid(0),reader.GetString(1),reader.GetInt16(2),reader.GetString(3),reader.GetInt16(4),
        reader.GetString(5),reader.GetString(6),DateOnly.FromDateTime(reader.GetDateTime(7)),
        DateOnly.FromDateTime(reader.GetDateTime(8)),reader.GetString(9),reader.GetString(10),
        reader.GetString(11),reader.GetString(12),reader.GetInt32(13),reader.GetDecimal(14),
        reader.GetFieldValue<DateTimeOffset>(15),validations);

    private static ComplianceReportDefinitionView ReadDefinition(SqlDataReader reader) => new(
        reader.GetString(0),reader.GetInt16(1),reader.GetString(2),reader.GetInt16(3),
        reader.GetString(4),reader.GetString(5),reader.GetString(6),
        DateOnly.FromDateTime(reader.GetDateTime(7)),reader.GetString(8),reader.GetString(9),reader.GetString(10));

    private static ComplianceConceptMappingView ReadMapping(SqlDataReader reader) => new(
        reader.GetGuid(0),reader.GetGuid(1),reader.IsDBNull(2)?null:reader.GetGuid(2),reader.GetString(3),
        reader.GetInt16(4),reader.GetString(5),reader.GetInt16(6),reader.GetGuid(7),reader.GetString(8),
        reader.GetString(9),reader.GetString(10),reader.GetString(11));

    private static void AddMappingParameters(SqlCommand command, AccountingUserIdentity user,
        Guid mappingId, SetComplianceConceptMappingRequest request, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@MappingId", mappingId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", (object?)request.BusinessId ?? DBNull.Value);
        command.Parameters.AddWithValue("@AuthorityCode", request.AuthorityCode);
        command.Parameters.AddWithValue("@TaxYear", request.TaxYear);
        command.Parameters.AddWithValue("@FormatCode", request.FormatCode);
        command.Parameters.AddWithValue("@FormatVersion", request.FormatVersion);
        command.Parameters.AddWithValue("@AccountId", request.AccountId);
        command.Parameters.AddWithValue("@ConceptCode", request.ConceptCode);
        command.Parameters.AddWithValue("@TargetField", request.TargetField);
        command.Parameters.AddWithValue("@Now", now);
    }

    private static void AddScope(SqlCommand command, AccountingUserIdentity user,
        GenerateComplianceReportRequest request)
    {
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        AddDefinitionScope(command, request);
    }

    private static void AddDefinitionScope(SqlCommand command, GenerateComplianceReportRequest request)
    {
        command.Parameters.AddWithValue("@Authority", request.AuthorityCode);
        command.Parameters.AddWithValue("@TaxYear", request.TaxYear);
        command.Parameters.AddWithValue("@Format", request.FormatCode);
        command.Parameters.AddWithValue("@Version", request.FormatVersion);
    }

    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string JsonValue(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    private static string Escape(string value) => value.IndexOfAny([',','"','\r','\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed record SourceRow(Guid? PartyId,string ConceptCode,string TargetField,decimal Amount,
        string? DocumentType,string? Identification,string? VerificationDigit,string? FirstName,
        string? LastName,string? LegalName,string? DisplayName,string? CompletionStatus,string? Address,
        string? DepartmentCode,string? CityCode,string? CountryCode);
    private sealed record GeneratedRow(Guid? PartyId,string ConceptCode,string Json,decimal ControlAmount);
}
