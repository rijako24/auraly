using System.Data;
using System.Text.Json;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed partial class SqlPayrollStore
{
    public async Task<PayrollProfilePage> PageProfilesAsync(PayrollUserIdentity user,int page,int pageSize,string? search,bool? isActive,string sortBy,bool descending,CancellationToken ct)
    {
        var column=sortBy switch {"code"=>"Code","name"=>"Name","status"=>"IsActive",_=>throw new ArgumentOutOfRangeException(nameof(sortBy))};
        var direction=descending?"DESC":"ASC";
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=new SqlCommand($"""
            SELECT COUNT(*) FROM payroll.AccountingProfiles WHERE TenantId=@Tenant
              AND (@Active IS NULL OR IsActive=@Active)
              AND (@Search IS NULL OR Code LIKE N'%'+@Search+N'%' OR Name LIKE N'%'+@Search+N'%');
            SELECT ProfileId,Code,Name,RuleSetId,IsActive,RowVersion FROM payroll.AccountingProfiles WHERE TenantId=@Tenant
              AND (@Active IS NULL OR IsActive=@Active)
              AND (@Search IS NULL OR Code LIKE N'%'+@Search+N'%' OR Name LIKE N'%'+@Search+N'%')
            ORDER BY {column} {direction},ProfileId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            """,connection);
        command.Parameters.AddWithValue("@Tenant",user.TenantId);command.Parameters.AddWithValue("@Search",(object?)search??DBNull.Value);
        command.Parameters.Add("@Active",SqlDbType.Bit).Value=(object?)isActive??DBNull.Value;
        command.Parameters.AddWithValue("@Offset",(page-1)*pageSize);command.Parameters.AddWithValue("@Size",pageSize);
        await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var count=reader.GetInt32(0);
        await reader.NextResultAsync(ct);var rows=new List<PayrollProfileView>();
        while(await reader.ReadAsync(ct))rows.Add(ReadProfile(reader,[]));
        return new(rows,page,pageSize,count,(int)Math.Ceiling(count/(double)pageSize));
    }

    public async Task<PayrollProfileView> GetProfileAsync(PayrollUserIdentity user,Guid id,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        return await ReadProfileAsync(connection,null,user.TenantId,id,ct);
    }

    public async Task<PayrollProfileView> SaveProfileAsync(PayrollUserIdentity user,SavePayrollProfileRequest request,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            PayrollRuleSetView? createdRules=null;
            if(request.Configuration is { } initial)
            {
                if(request.RowVersion is not null)
                {
                    await using var head=new SqlCommand("""
                        SELECT rules.CountryCode FROM payroll.AccountingProfiles profile WITH(UPDLOCK,HOLDLOCK)
                        JOIN payroll.RuleSets rules ON rules.RuleSetId=profile.RuleSetId AND (rules.TenantId=@Tenant OR rules.TenantId IS NULL)
                        WHERE profile.ProfileId=@Id AND profile.TenantId=@Tenant AND profile.RuleSetId=@Rules AND profile.RowVersion=@Version;
                        """,connection,transaction);
                    head.Parameters.AddWithValue("@Tenant",user.TenantId);head.Parameters.AddWithValue("@Id",request.ProfileId);
                    head.Parameters.AddWithValue("@Rules",request.RuleSetId);head.Parameters.Add("@Version",SqlDbType.Binary,8).Value=request.RowVersion;
                    await using var reader=await head.ExecuteReaderAsync(ct);
                    if(!await reader.ReadAsync(ct))throw new PayrollConflictException("El perfil cambió. Ábrelo nuevamente antes de guardar.");
                    var profileCode=$"PROFILE-{request.ProfileId:N}".ToUpperInvariant();
                    initial=initial with{CountryCode=reader.GetString(0),Code=profileCode};
                }
                var savedVersion=await SaveRuleSetCoreAsync(connection,transaction,user,initial,ct,allowRevision:true);
                await ApproveRuleSetCoreAsync(connection,transaction,user,initial.RuleSetId,savedVersion,ct);
                createdRules=await ReadRuleSetAsync(connection,transaction,user.TenantId,initial.RuleSetId,ct);
            }
            await using var command=new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM payroll.RuleSets rules WHERE rules.RuleSetId=@Rules
                  AND (rules.TenantId=@Tenant OR rules.TenantId IS NULL)
                  AND (rules.Status=N'Approved' OR rules.Status=N'Retired' AND EXISTS(
                    SELECT 1 FROM payroll.AccountingProfiles existing WHERE existing.ProfileId=@Id
                      AND existing.TenantId=@Tenant AND existing.RuleSetId=rules.RuleSetId)))
                  THROW 51707,N'Los parámetros deben estar aprobados y pertenecer a esta empresa o al catálogo general.',1;
                DECLARE @Lines TABLE(Id uniqueidentifier PRIMARY KEY,ConceptId uniqueidentifier,DebitAccountId uniqueidentifier,
                  CreditAccountId uniqueidentifier,BeneficiaryPartyId uniqueidentifier,EffectiveFrom date,EffectiveTo date);
                INSERT @Lines SELECT MappingId,ConceptId,DebitAccountId,CreditAccountId,BeneficiaryPartyId,EffectiveFrom,EffectiveTo
                  FROM OPENJSON(@Mappings) WITH(MappingId uniqueidentifier '$.mappingId',ConceptId uniqueidentifier '$.conceptId',
                    DebitAccountId uniqueidentifier '$.debitAccountId',CreditAccountId uniqueidentifier '$.creditAccountId',BeneficiaryPartyId uniqueidentifier '$.beneficiaryPartyId',
                    EffectiveFrom date '$.effectiveFrom',EffectiveTo date '$.effectiveTo');
                IF EXISTS(SELECT 1 FROM @Lines a JOIN @Lines b ON a.Id<b.Id
                  AND (a.ConceptId=b.ConceptId OR a.ConceptId IS NULL AND b.ConceptId IS NULL)
                  AND a.EffectiveFrom<=COALESCE(b.EffectiveTo,CONVERT(date,'99991231'))
                  AND b.EffectiveFrom<=COALESCE(a.EffectiveTo,CONVERT(date,'99991231')))
                  THROW 51707,N'Las vigencias de un concepto no pueden superponerse dentro del perfil.',1;
                IF EXISTS(SELECT 1 FROM @Lines l LEFT JOIN payroll.Concepts c ON c.ConceptId=l.ConceptId AND c.TenantId=@Tenant
                  LEFT JOIN payroll.CatalogOptions nature ON nature.OptionId=c.NatureOptionId
                  WHERE l.ConceptId IS NOT NULL AND (c.ConceptId IS NULL OR
                    nature.Code=N'Earning' AND l.CreditAccountId IS NOT NULL OR nature.Code=N'Deduction' AND l.DebitAccountId IS NOT NULL)
                    OR l.ConceptId IS NULL AND l.DebitAccountId IS NOT NULL)
                  THROW 51707,N'Revisa el concepto de esta empresa y la naturaleza de sus cuentas. El neto sólo admite crédito.',1;
                IF EXISTS(SELECT 1 FROM @Lines l LEFT JOIN payroll.Concepts c ON c.ConceptId=l.ConceptId AND c.TenantId=@Tenant
                  LEFT JOIN payroll.CatalogOptions nature ON nature.OptionId=c.NatureOptionId
                  LEFT JOIN payroll.CatalogOptions category ON category.OptionId=c.AccountingCategoryOptionId
                  CROSS APPLY(VALUES(l.DebitAccountId,CASE nature.Code WHEN N'EmployerContribution' THEN N'EmployerContributionsExpense'
                    WHEN N'Provision' THEN N'BenefitsExpense' ELSE category.Code END),
                    (l.CreditAccountId,COALESCE(category.Code,N'NetPayrollPayable'))) chosen(AccountId,Category)
                  WHERE chosen.AccountId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM accounting.PostingAccountReadiness(@Tenant,chosen.Category,chosen.AccountId)))
                  THROW 51707,N'Una cuenta explícita no está activa, no permite contabilizar o es incompatible con su concepto.',1;
                IF EXISTS(SELECT 1 FROM @Lines line LEFT JOIN payroll.Concepts concept ON concept.ConceptId=line.ConceptId AND concept.TenantId=@Tenant
                  LEFT JOIN payroll.CatalogOptions role ON role.OptionId=concept.SystemRoleOptionId
                  LEFT JOIN dbo.Parties beneficiary ON beneficiary.PartyId=line.BeneficiaryPartyId AND beneficiary.TenantId=@Tenant
                  WHERE line.BeneficiaryPartyId IS NOT NULL AND (role.Code IS NULL OR role.Code NOT IN(N'Sena',N'Icbf')
                    OR beneficiary.PartyId IS NULL OR beneficiary.IsActive=0 OR beneficiary.PartyType<>N'Organization'))
                  THROW 51707,N'El beneficiario del perfil aplica a SENA o ICBF y debe ser una entidad jurídica activa de esta empresa. Las demás afiliaciones pertenecen al contrato.',1;
                IF EXISTS(SELECT 1 FROM payroll.AccountingProfiles WITH(UPDLOCK,HOLDLOCK) WHERE ProfileId=@Id AND TenantId=@Tenant)
                BEGIN
                  UPDATE payroll.AccountingProfiles SET Code=COALESCE(@Code,Code),Name=@Name,RuleSetId=@Rules,IsActive=@Active,UpdatedBy=@Actor,UpdatedAt=@Now
                    WHERE ProfileId=@Id AND TenantId=@Tenant AND RowVersion=@Version;
                  IF @@ROWCOUNT<>1 THROW 51708,N'El perfil cambió; recárgalo antes de guardar.',1;
                END
                ELSE BEGIN
                  IF @Version IS NOT NULL THROW 51708,N'El perfil ya no está disponible.',1;
                  INSERT payroll.AccountingProfiles(ProfileId,TenantId,Code,Name,RuleSetId,IsActive,UpdatedBy,UpdatedAt)
                    VALUES(@Id,@Tenant,COALESCE(@Code,UPPER(REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''))),@Name,@Rules,@Active,@Actor,@Now);
                END;
                DELETE payroll.ProfileAccountMappings WHERE ProfileId=@Id AND TenantId=@Tenant;
                INSERT payroll.ProfileAccountMappings(MappingId,TenantId,ProfileId,ConceptId,DebitAccountId,CreditAccountId,BeneficiaryPartyId,EffectiveFrom,EffectiveTo)
                  SELECT Id,@Tenant,@Id,ConceptId,DebitAccountId,CreditAccountId,BeneficiaryPartyId,EffectiveFrom,EffectiveTo FROM @Lines;
                """,connection,transaction);
            command.Parameters.AddWithValue("@Tenant",user.TenantId);command.Parameters.AddWithValue("@Id",request.ProfileId);
            command.Parameters.Add("@Code",SqlDbType.NVarChar,32).Value=(object?)request.Code??DBNull.Value;command.Parameters.AddWithValue("@Name",request.Name);
            command.Parameters.AddWithValue("@Rules",request.RuleSetId);command.Parameters.AddWithValue("@Active",request.IsActive);
            command.Parameters.AddWithValue("@Actor",user.UserId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
            command.Parameters.Add("@Version",SqlDbType.Binary,8).Value=(object?)request.RowVersion??DBNull.Value;
            command.Parameters.Add("@Mappings",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Mappings,Json);
            await command.ExecuteNonQueryAsync(ct);
            var saved=await ReadProfileAsync(connection,transaction,user.TenantId,request.ProfileId,ct);
            await transaction.CommitAsync(ct);return saved with{CreatedRuleSet=createdRules};
        }
        catch(SqlException error) when(error.Number is 51707 or 51708 or 51720 or 51721 or 2601 or 2627)
        {
            if(transaction.Connection is not null)await transaction.RollbackAsync(CancellationToken.None);
            if(error.Number==51707)throw new PayrollValidationException(error.Message);
            throw new PayrollConflictException(error.Number is 51708 or 51720 or 51721?error.Message:
                "El código, una identidad o la fecha de vigencia ya están en uso. Revisa los datos del perfil.");
        }
        catch{if(transaction.Connection is not null)await transaction.RollbackAsync(CancellationToken.None);throw;}
    }

    private static async Task<PayrollProfileView> ReadProfileAsync(SqlConnection connection,SqlTransaction? transaction,Guid tenant,Guid id,CancellationToken ct)
    {
        await using var command=new SqlCommand("""
            SELECT ProfileId,Code,Name,RuleSetId,IsActive,RowVersion FROM payroll.AccountingProfiles WHERE TenantId=@Tenant AND ProfileId=@Id;
            SELECT m.MappingId,m.ConceptId,m.DebitAccountId,m.CreditAccountId,m.EffectiveFrom,m.EffectiveTo,
              CONCAT(d.Code,N' · ',d.Name),CONCAT(c.Code,N' · ',c.Name),m.BeneficiaryPartyId,beneficiary.DisplayName
            FROM payroll.ProfileAccountMappings m
            LEFT JOIN dbo.AccountingAccounts d ON d.AccountId=m.DebitAccountId AND d.TenantId=m.TenantId
            LEFT JOIN dbo.AccountingAccounts c ON c.AccountId=m.CreditAccountId AND c.TenantId=m.TenantId
            LEFT JOIN dbo.Parties beneficiary ON beneficiary.PartyId=m.BeneficiaryPartyId AND beneficiary.TenantId=m.TenantId
            WHERE m.TenantId=@Tenant AND m.ProfileId=@Id ORDER BY m.ConceptId,m.EffectiveFrom,m.MappingId;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",tenant);command.Parameters.AddWithValue("@Id",id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw new PayrollNotFoundException("El perfil no existe en esta empresa.");
        var value=ReadProfile(reader,[]);await reader.NextResultAsync(ct);var mappings=new List<PayrollProfileMapping>();
        while(await reader.ReadAsync(ct))mappings.Add(new(reader.GetGuid(0),reader.IsDBNull(1)?null:reader.GetGuid(1),reader.IsDBNull(2)?null:reader.GetGuid(2),
            reader.IsDBNull(3)?null:reader.GetGuid(3),DateOnly.FromDateTime(reader.GetDateTime(4)),reader.IsDBNull(5)?null:DateOnly.FromDateTime(reader.GetDateTime(5)),
            reader.IsDBNull(2)?null:reader.GetString(6),reader.IsDBNull(3)?null:reader.GetString(7))
            {BeneficiaryPartyId=reader.IsDBNull(8)?null:reader.GetGuid(8),BeneficiaryName=reader.IsDBNull(9)?null:reader.GetString(9)});
        return value with{Mappings=mappings};
    }
    private static PayrollProfileView ReadProfile(SqlDataReader reader,IReadOnlyList<PayrollProfileMapping> mappings)=>
        new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetGuid(3),reader.GetBoolean(4),(byte[])reader[5],mappings);
}
