using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlCommercialPartyRoleStore(SqlServerConnectionFactory connections) : ICommercialPartyRoleStore
{
    public Task<CommercialRoleAcceptance> CreateSellerAsync(PartyActorIdentity actor, Guid partyId, Guid roleId, Guid siteId,
        CreateSellerRequest request, string normalizedIdentification, DateTimeOffset now, CancellationToken ct) =>
        CreateAsync(actor, partyId, roleId, siteId, request.OperationId, request.BusinessId, request.Party, request.PrimarySite,
            normalizedIdentification, now, "Seller", request.Code, request.DefaultCommissionPercent,
            request.CommissionBasis, request.CommissionTrigger, ct);

    public Task<CommercialRoleAcceptance> CreateCarrierAsync(PartyActorIdentity actor, Guid partyId, Guid roleId, Guid siteId,
        CreateCarrierRequest request, string normalizedIdentification, DateTimeOffset now, CancellationToken ct) =>
        CreateAsync(actor, partyId, roleId, siteId, request.OperationId, request.BusinessId, request.Party, request.PrimarySite,
            normalizedIdentification, now, "Carrier", request.Code, null, request.TransportationMode, null, ct);

    public async Task<CustomerPricingOptions> PricingOptionsAsync(PartyActorIdentity actor, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
              THROW 51060,'Business is outside the authenticated tenant.',1;
            SELECT PriceListId,Code,Name FROM dbo.PriceLists WHERE BusinessId=@BusinessId AND IsActive=1 ORDER BY Name;
            SELECT PriceChannelId,Code,Name FROM dbo.PriceChannels WHERE BusinessId=@BusinessId AND IsActive=1 ORDER BY Name;
            """;
        command.Parameters.AddRange([P("@BusinessId", actor.BusinessId), P("@TenantId", actor.TenantId)]);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var lists = new List<CustomerPricingOption>();
            while (await reader.ReadAsync(ct)) lists.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            await reader.NextResultAsync(ct);
            var channels = new List<CustomerPricingOption>();
            while (await reader.ReadAsync(ct)) channels.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            return new(lists, channels);
        }
        catch (SqlException ex) when (ex.Number == 51060) { throw new PartyForbiddenException(ex.Message); }
    }

    private async Task<CommercialRoleAcceptance> CreateAsync(
        PartyActorIdentity actor, Guid partyId, Guid roleId, Guid siteId, Guid operationId, Guid businessId,
        PartyInput party, PartySiteInput site, string normalized, DateTimeOffset now, string role,
        string code, decimal? commission, string option1, string? option2, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var replay = await ReadReceiptAsync(connection, transaction, businessId, operationId, role, ct);
            if (replay is not null)
            {
                await transaction.CommitAsync(ct);
                return replay;
            }

            await ValidateScopeAsync(connection, transaction, actor, businessId, site, ct);
            var resolvedPartyId = await FindPartyAsync(connection, transaction, actor.TenantId, party, normalized, ct) ?? partyId;
            if (resolvedPartyId == partyId)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                      Identification,NormalizedIdentification,VerificationDigit,DisplayName,LegalName,FirstName,LastName,
                      CompletionStatus,IsActive,CreatedBy,CreatedAt)
                    VALUES(@PartyId,@TenantId,@PartyType,@Country,@IdentificationType,@Identification,@Normalized,
                      @Digit,@DisplayName,@LegalName,@FirstName,@LastName,N'Complete',1,@Actor,@Now);
                    """, [P("@PartyId",partyId),P("@TenantId",actor.TenantId),P("@PartyType",party.PartyType),
                    P("@Country",party.IdentificationCountryId),P("@IdentificationType",party.IdentificationTypeCode.Trim().ToUpperInvariant()),
                    P("@Identification",party.Identification.Trim()),P("@Normalized",normalized),P("@Digit",Empty(party.VerificationDigit)),
                    P("@DisplayName",party.DisplayName.Trim()),P("@LegalName",Empty(party.LegalName)),P("@FirstName",Empty(party.FirstName)),
                    P("@LastName",Empty(party.LastName)),P("@Actor",actor.ActorId),P("@Now",now)], ct);
                await AddContactAsync(connection, transaction, partyId, "Email", party.Email, now, ct);
                await AddContactAsync(connection, transaction, partyId, "Phone", party.Phone, now, ct);
            }

            if (await RoleExistsAsync(connection, transaction, businessId, resolvedPartyId, role, ct))
                throw new PartyConflictException($"The Party already has the {role} role in this business.");

            if (role == "Seller")
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.CommerceSellers(SellerId,BusinessId,PartyId,Code,DefaultCommissionPercent,CommissionBasis,CommissionTrigger,IsActive,CreatedAt)
                    VALUES(@RoleId,@BusinessId,@PartyId,@Code,@Commission,@Option1,@Option2,1,@Now);
                    """, [P("@RoleId",roleId),P("@BusinessId",businessId),P("@PartyId",resolvedPartyId),P("@Code",code.Trim().ToUpperInvariant()),
                    P("@Commission",commission),P("@Option1",option1),P("@Option2",option2),P("@Now",now)], ct);
            else
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Carriers(CarrierId,BusinessId,PartyId,Code,TransportationMode,IsActive,CreatedAt)
                    VALUES(@RoleId,@BusinessId,@PartyId,@Code,@Option1,1,@Now);
                    """, [P("@RoleId",roleId),P("@BusinessId",businessId),P("@PartyId",resolvedPartyId),
                    P("@Code",code.Trim().ToUpperInvariant()),P("@Option1",option1),P("@Now",now)], ct);

            await InsertSiteAsync(connection, transaction, actor, resolvedPartyId, siteId, site, now, ct);
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.CommercialRoleCreationReceipts(BusinessId,OperationId,RoleType,RoleId,CreatedAt)
                VALUES(@BusinessId,@OperationId,@Role,@RoleId,@Now);
                """, [P("@BusinessId",businessId),P("@OperationId",operationId),P("@Role",role),P("@RoleId",roleId),P("@Now",now)], ct);
            await transaction.CommitAsync(ct);
            return new(roleId, resolvedPartyId, role, false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        { await transaction.RollbackAsync(ct); throw new PartyConflictException("The role code, identity or site is already in use."); }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    private static async Task<CommercialRoleAcceptance?> ReadReceiptAsync(SqlConnection c, SqlTransaction t, Guid business, Guid operation, string role, CancellationToken ct)
    {
        await using var x=c.CreateCommand(); x.Transaction=t; x.CommandText="""
            SELECT r.RoleType,r.RoleId,
              CASE WHEN r.RoleType=N'Seller' THEN (SELECT PartyId FROM dbo.CommerceSellers WHERE SellerId=r.RoleId)
                   ELSE (SELECT PartyId FROM dbo.Carriers WHERE CarrierId=r.RoleId) END
            FROM dbo.CommercialRoleCreationReceipts r WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@Business AND OperationId=@Operation;
            """; x.Parameters.AddRange([P("@Business",business),P("@Operation",operation)]);
        await using var reader=await x.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)) return null;
        if(!string.Equals(reader.GetString(0),role,StringComparison.Ordinal)) throw new PartyConflictException("Operation ID was already used for another role.");
        return new(reader.GetGuid(1),reader.GetGuid(2),role,true);
    }

    private static async Task ValidateScopeAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity actor,Guid business,PartySiteInput site,CancellationToken ct)
    {
        await using var x=c.CreateCommand();x.Transaction=t;x.CommandText="""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@Business AND TenantId=@Tenant AND IsActive=1)
              THROW 51060,'Business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Cities ci JOIN dbo.AdministrativeDivisions d ON d.AdministrativeDivisionId=ci.AdministrativeDivisionId
              WHERE ci.CityId=@City AND d.AdministrativeDivisionId=@Division AND d.CountryId=@Country AND ci.IsActive=1 AND d.IsActive=1)
              THROW 51061,'The geographic hierarchy is invalid or inactive.',1;
            """;x.Parameters.AddRange([P("@Business",business),P("@Tenant",actor.TenantId),P("@City",site.CityId),P("@Division",site.AdministrativeDivisionId),P("@Country",site.CountryId)]);
        try{await x.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51060){throw new PartyForbiddenException(ex.Message);}catch(SqlException ex) when(ex.Number==51061){throw new PartyValidationException(ex.Message);}
    }

    private static async Task<Guid?> FindPartyAsync(SqlConnection c,SqlTransaction t,Guid tenant,PartyInput party,string normalized,CancellationToken ct)
    {await using var x=c.CreateCommand();x.Transaction=t;x.CommandText="""
        SELECT TOP (1) p.PartyId
        FROM dbo.Parties p WITH(UPDLOCK,HOLDLOCK)
        JOIN dbo.Countries requested ON requested.CountryId=@Country
        JOIN dbo.Countries existing ON existing.CountryId=p.IdentificationCountryId
        WHERE p.TenantId=@Tenant
          AND UPPER(LTRIM(RTRIM(existing.Name)))=UPPER(LTRIM(RTRIM(requested.Name)))
          AND p.IdentificationTypeCode=@Type
          AND p.NormalizedIdentification=@Normalized
        ORDER BY p.CreatedAt,p.PartyId;
        """;x.Parameters.AddRange([P("@Tenant",tenant),P("@Country",party.IdentificationCountryId),P("@Type",party.IdentificationTypeCode.Trim().ToUpperInvariant()),P("@Normalized",normalized)]);var result=await x.ExecuteScalarAsync(ct);return result is Guid id?id:null;}
    private static async Task<bool> RoleExistsAsync(SqlConnection c,SqlTransaction t,Guid business,Guid party,string role,CancellationToken ct)
    {await using var x=c.CreateCommand();x.Transaction=t;x.CommandText=role=="Seller"?"SELECT COUNT(1) FROM dbo.CommerceSellers WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@Business AND PartyId=@Party":"SELECT COUNT(1) FROM dbo.Carriers WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@Business AND PartyId=@Party";x.Parameters.AddRange([P("@Business",business),P("@Party",party)]);return Convert.ToInt32(await x.ExecuteScalarAsync(ct))>0;}
    private static async Task AddContactAsync(SqlConnection c,SqlTransaction t,Guid party,string type,string? value,DateTimeOffset now,CancellationToken ct)
    {if(string.IsNullOrWhiteSpace(value))return;var v=value.Trim();await ExecuteAsync(c,t,"INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt) VALUES(NEWID(),@Party,@Type,@Value,@Normalized,1,1,@Now)",[P("@Party",party),P("@Type",type),P("@Value",v),P("@Normalized",type=="Email"?v.ToUpperInvariant():string.Concat(v.Where(char.IsDigit))),P("@Now",now)],ct);}
    private static Task InsertSiteAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity actor,Guid party,Guid siteId,PartySiteInput s,DateTimeOffset now,CancellationToken ct)=>ExecuteAsync(c,t,"""
        IF NOT EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@Party AND Code=@Code)
        INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,Neighborhood,PostalCode,Email,Phone,IsPrimary,IsActive,CreatedBy,CreatedAt)
        VALUES(@Id,@Party,@Code,@Name,@Country,@Division,@City,@Address,@Neighborhood,@Postal,@Email,@Phone,CASE WHEN EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@Party AND IsPrimary=1 AND IsActive=1) THEN 0 ELSE @Primary END,1,@Actor,@Now)
        """,[P("@Id",siteId),P("@Party",party),P("@Code",s.Code.Trim().ToUpperInvariant()),P("@Name",s.Name.Trim()),P("@Country",s.CountryId),P("@Division",s.AdministrativeDivisionId),P("@City",s.CityId),P("@Address",s.AddressLine.Trim()),P("@Neighborhood",Empty(s.Neighborhood)),P("@Postal",Empty(s.PostalCode)),P("@Email",Empty(s.Email)),P("@Phone",Empty(s.Phone)),P("@Primary",s.IsPrimary),P("@Actor",actor.ActorId),P("@Now",now)],ct);
    private static async Task ExecuteAsync(SqlConnection c,SqlTransaction t,string sql,SqlParameter[] ps,CancellationToken ct){await using var x=c.CreateCommand();x.Transaction=t;x.CommandText=sql;x.Parameters.AddRange(ps);await x.ExecuteNonQueryAsync(ct);}
    private static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
    private static string? Empty(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
