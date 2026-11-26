using System.Data;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Domain;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore
{
    public async Task<PayrollRuleSetPage> PageProfileRuleSetsAsync(PayrollUserIdentity user,Guid profileId,int page,int pageSize,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand("""
            DECLARE @Country nvarchar(2),@Code nvarchar(64),@OwnCode nvarchar(64)=N'PROFILE-'+UPPER(REPLACE(CONVERT(nvarchar(36),@Profile),N'-',N''));
            SELECT @Country=r.CountryCode,@Code=r.Code FROM payroll.AccountingProfiles profile
            JOIN payroll.RuleSets r ON r.RuleSetId=profile.RuleSetId AND (r.TenantId=@Tenant OR r.TenantId IS NULL)
            WHERE profile.ProfileId=@Profile AND profile.TenantId=@Tenant;
            IF @Code IS NULL THROW 51709,N'El perfil no está disponible en esta empresa.',1;
            DECLARE @Page TABLE(RuleSetId uniqueidentifier PRIMARY KEY);
            INSERT @Page SELECT RuleSetId FROM payroll.RuleSets
            WHERE (TenantId=@Tenant OR TenantId IS NULL) AND CountryCode=@Country AND Code IN(@Code,@OwnCode)
            ORDER BY CASE WHEN Code=@OwnCode THEN 0 ELSE 1 END,EffectiveFrom DESC,CASE WHEN TenantId IS NULL THEN 1 ELSE 0 END,Revision DESC,RuleSetId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            SELECT COUNT(*) FROM payroll.RuleSets
            WHERE (TenantId=@Tenant OR TenantId IS NULL) AND CountryCode=@Country AND Code IN(@Code,@OwnCode);
            SELECT r.RuleSetId,r.CountryCode,r.Code,r.Name,r.EffectiveFrom,r.EffectiveTo,r.SourceReference,r.Status,r.RowVersion,
              CONVERT(bit,CASE WHEN r.TenantId IS NULL THEN 1 ELSE 0 END)
            FROM @Page page JOIN payroll.RuleSets r ON r.RuleSetId=page.RuleSetId
            ORDER BY CASE WHEN r.Code=@OwnCode THEN 0 ELSE 1 END,r.EffectiveFrom DESC,CASE WHEN r.TenantId IS NULL THEN 1 ELSE 0 END,r.Revision DESC,r.RuleSetId;
            SELECT p.RuleSetId,p.Code,p.NumericValue,p.UnitCode,p.Description FROM @Page page
            JOIN payroll.RuleParameters p ON p.RuleSetId=page.RuleSetId ORDER BY p.RuleSetId,p.Code;
            """,connection);
        command.Parameters.AddWithValue("@Tenant",user.TenantId);command.Parameters.AddWithValue("@Profile",profileId);
        command.Parameters.AddWithValue("@Offset",(page-1)*pageSize);command.Parameters.AddWithValue("@Size",pageSize);
        try
        {
            await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var count=reader.GetInt32(0);
            await reader.NextResultAsync(ct);var rows=new List<PayrollRuleSetView>();
            while(await reader.ReadAsync(ct))rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),reader.IsDBNull(5)?null:DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetString(6),reader.GetString(7),[],(byte[])reader[8]){IsGlobal=reader.GetBoolean(9)});
            await reader.NextResultAsync(ct);var parameters=rows.ToDictionary(row=>row.RuleSetId,_=>new List<PayrollRuleParameterView>());
            while(await reader.ReadAsync(ct))parameters[reader.GetGuid(0)].Add(new(reader.GetString(1),reader.GetDecimal(2),reader.GetString(3),NullableString(reader,4)));
            return new(rows.Select(row=>row with{Parameters=parameters[row.RuleSetId]}).ToArray(),page,pageSize,count);
        }
        catch(SqlException error) when(error.Number==51709){throw new PayrollNotFoundException(error.Message);}
    }
    private async Task<byte[]> SaveRuleSetCoreAsync(SqlConnection connection,SqlTransaction tx,PayrollUserIdentity user,SavePayrollRuleSetRequest request,CancellationToken ct,bool allowRevision=false)
    {
            await using (var command = new SqlCommand("""
                IF EXISTS(SELECT 1 FROM payroll.RuleSets WITH(UPDLOCK,HOLDLOCK) WHERE RuleSetId=@Id AND TenantId=@TenantId)
                BEGIN
                  UPDATE payroll.RuleSets SET CountryCode=@Country,Code=@Code,Name=@Name,EffectiveFrom=@From,
                    EffectiveTo=@To,SourceReference=@Source
                  WHERE RuleSetId=@Id AND TenantId=@TenantId AND Status=N'Draft' AND RowVersion=@RowVersion;
                  IF @@ROWCOUNT<>1 THROW 51720,N'La regla cambió o ya fue aprobada.',1;
                  DELETE payroll.RuleParameters WHERE RuleSetId=@Id;
                END
                ELSE BEGIN
                  DECLARE @Revision int=1;
                  IF @AllowRevision=1
                    SELECT @Revision=COALESCE(MAX(Revision),0)+1 FROM payroll.RuleSets WITH(UPDLOCK,HOLDLOCK)
                    WHERE TenantId=@TenantId AND CountryCode=@Country AND Code=@Code AND EffectiveFrom=@From;
                  INSERT payroll.RuleSets(RuleSetId,TenantId,CountryCode,Code,Name,EffectiveFrom,EffectiveTo,
                    SourceReference,Status,CreatedBy,CreatedAt,Revision)
                    VALUES(@Id,@TenantId,@Country,@Code,@Name,@From,@To,@Source,N'Draft',@UserId,@Now,@Revision);
                END;
                INSERT payroll.RuleParameters(RuleParameterId,RuleSetId,Code,NumericValue,UnitCode,Description)
                SELECT ParameterId,@Id,Code,NumericValue,UnitCode,Description FROM OPENJSON(@Parameters)
                  WITH(ParameterId uniqueidentifier,Code nvarchar(64),NumericValue decimal(19,8),UnitCode nvarchar(32),Description nvarchar(300));
                SELECT RowVersion FROM payroll.RuleSets WHERE RuleSetId=@Id AND TenantId=@TenantId;
                """, connection, tx))
            {
                command.Parameters.AddWithValue("@Id", request.RuleSetId);
                command.Parameters.AddWithValue("@AllowRevision",allowRevision);
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
                return (byte[])(await command.ExecuteScalarAsync(ct)
                    ?? throw new PayrollConflictException("No se obtuvo la versión de la configuración guardada."));
            }
    }

    private async Task ApproveRuleSetCoreAsync(SqlConnection connection,SqlTransaction tx,PayrollUserIdentity user,Guid ruleSetId,byte[] rowVersion,CancellationToken ct)
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
    }

    private static async Task<PayrollRuleSetView> ReadRuleSetAsync(SqlConnection connection,SqlTransaction? transaction,
        Guid tenantId,Guid id,CancellationToken ct)
    {
        await using var command=new SqlCommand("""
            SELECT RuleSetId,CountryCode,Code,Name,EffectiveFrom,EffectiveTo,SourceReference,Status,RowVersion,
              CONVERT(bit,CASE WHEN TenantId IS NULL THEN 1 ELSE 0 END)
              FROM payroll.RuleSets WHERE RuleSetId=@Id AND (TenantId=@Tenant OR TenantId IS NULL);
            SELECT p.Code,p.NumericValue,p.UnitCode,p.Description FROM payroll.RuleParameters p
              JOIN payroll.RuleSets rules ON rules.RuleSetId=p.RuleSetId
              WHERE rules.RuleSetId=@Id AND (rules.TenantId=@Tenant OR rules.TenantId IS NULL) ORDER BY p.Code;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",tenantId);command.Parameters.AddWithValue("@Id",id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw new PayrollNotFoundException("La configuración de porcentajes no existe en esta empresa.");
        var result=new PayrollRuleSetView(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
            DateOnly.FromDateTime(reader.GetDateTime(4)),reader.IsDBNull(5)?null:DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.GetString(6),reader.GetString(7),[],(byte[])reader[8]){IsGlobal=reader.GetBoolean(9)};
        await reader.NextResultAsync(ct);var parameters=new List<PayrollRuleParameterView>();
        while(await reader.ReadAsync(ct))parameters.Add(new(reader.GetString(0),reader.GetDecimal(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3)));
        return result with{Parameters=parameters};
    }
}
