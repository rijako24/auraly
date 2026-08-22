using Auraly.Application.Parties;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPartyWorkspaceStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IPartyWorkspaceStore
{
    public async Task<PartyWorkspacePage> PageAsync(
        PartyActorIdentity actor, int page, PartyWorkspaceQuery query, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var offset = (page - 1) * query.PageSize;
        const string scope = """
            FROM dbo.Parties p
            WHERE p.TenantId=@TenantId
              AND (EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId))
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%' OR p.Identification LIKE N'%'+@Search+N'%' OR p.NormalizedIdentification LIKE N'%'+@Search+N'%'
                   OR EXISTS(SELECT 1 FROM dbo.PartyContacts pc WHERE pc.PartyId=p.PartyId AND pc.IsActive=1 AND pc.Value LIKE N'%'+@Search+N'%'))
              AND (@Role IS NULL
                   OR @Role=N'Customer' AND EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR @Role=N'Supplier' AND EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR @Role=N'Seller' AND EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR @Role=N'Carrier' AND EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR @Role=N'Employee' AND EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId)
                   OR @Role=N'User' AND EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId))
              AND (@IsActive IS NULL OR @IsActive=CASE WHEN
                   EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId AND e.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId AND u.IsActive=1)
                   THEN 1 ELSE 0 END)
              AND (@Incomplete IS NULL OR @Incomplete=CASE WHEN p.CompletionStatus=N'Incomplete' THEN 1 ELSE 0 END)
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT_BIG(1) {scope};
            SELECT p.PartyId,p.PartyType,p.IdentificationTypeCode,p.Identification,p.VerificationDigit,
                   COALESCE(p.DisplayName,p.LegalName,N'Sin nombre'),p.LegalName,p.FirstName,p.LastName,
                   (SELECT TOP(1) Value FROM dbo.PartyContacts pc WHERE pc.PartyId=p.PartyId AND pc.ContactType=N'Email' AND pc.IsActive=1 ORDER BY pc.IsPrimary DESC,pc.CreatedAt),
                   (SELECT TOP(1) Value FROM dbo.PartyContacts pc WHERE pc.PartyId=p.PartyId AND pc.ContactType=N'Phone' AND pc.IsActive=1 ORDER BY pc.IsPrimary DESC,pc.CreatedAt),
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId) THEN 1 ELSE 0 END,
                   site.Name,site.CityName,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                          OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                          OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                          OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId AND e.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId AND u.IsActive=1)
                        THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                   p.CompletionStatus,p.RowVersion
            FROM dbo.Parties p
            OUTER APPLY(SELECT TOP(1) ps.Name,ci.Name CityName
                        FROM dbo.PartySites ps JOIN dbo.Cities ci ON ci.CityId=ps.CityId
                        WHERE ps.PartyId=p.PartyId AND ps.IsActive=1
                        ORDER BY ps.IsPrimary DESC,ps.CreatedAt) site
            WHERE p.TenantId=@TenantId
              AND (EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId))
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%' OR p.Identification LIKE N'%'+@Search+N'%' OR p.NormalizedIdentification LIKE N'%'+@Search+N'%'
                   OR EXISTS(SELECT 1 FROM dbo.PartyContacts pc WHERE pc.PartyId=p.PartyId AND pc.IsActive=1 AND pc.Value LIKE N'%'+@Search+N'%'))
              AND (@Role IS NULL
                   OR @Role=N'Customer' AND EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR @Role=N'Supplier' AND EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR @Role=N'Seller' AND EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR @Role=N'Carrier' AND EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR @Role=N'Employee' AND EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId)
                   OR @Role=N'User' AND EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId))
              AND (@IsActive IS NULL OR @IsActive=CASE WHEN
                   EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId AND e.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId AND u.IsActive=1)
                   THEN 1 ELSE 0 END)
              AND (@Incomplete IS NULL OR @Incomplete=CASE WHEN p.CompletionStatus=N'Incomplete' THEN 1 ELSE 0 END)
            ORDER BY CASE WHEN p.IdentificationTypeCode=N'CC' AND p.NormalizedIdentification=N'222222222222' THEN 0 ELSE 1 END,
                     p.DisplayName,p.PartyId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        command.Parameters.AddRange([
            P("@TenantId",actor.TenantId),P("@BusinessId",actor.BusinessId),P("@Search",Empty(query.Search)),
            P("@Role",Empty(query.Role)),P("@IsActive",query.IsActive),P("@Incomplete",query.IsIncomplete),
            P("@Offset",offset),P("@PageSize",query.PageSize)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var total = checked((int)reader.GetInt64(0));
        await reader.NextResultAsync(ct);
        var items = new List<PartyWorkspaceItem>();
        while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        return new PartyWorkspacePage(items,page,query.PageSize,total,(int)Math.Ceiling(total/(double)query.PageSize));
    }

    public async Task<PartyIdentityAcceptance> CreateIdentityAsync(
        PartyActorIdentity actor, Guid partyId, Guid siteId,
        CreatePartyIdentityRequest request, string normalizedIdentification,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await ValidateScopeAsync(connection, transaction, actor, request.PrimarySite, ct);
            var existingPartyId = await FindPartyIdAsync(
                connection, transaction, actor.TenantId, request.Party, normalizedIdentification, ct);
            if (existingPartyId is not null)
            {
                await transaction.CommitAsync(ct);
                return new PartyIdentityAcceptance(existingPartyId.Value, true);
            }

            await ExecuteAsync(connection, transaction, """
                INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                  Identification,NormalizedIdentification,VerificationDigit,DisplayName,LegalName,FirstName,LastName,
                  CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@PartyId,@TenantId,@PartyType,@CountryId,@IdentificationType,@Identification,@Normalized,
                  @Digit,@DisplayName,@LegalName,@FirstName,@LastName,N'Complete',1,@ActorId,@Now);
                """,[
                P("@PartyId",partyId),P("@TenantId",actor.TenantId),P("@PartyType",request.Party.PartyType),
                P("@CountryId",request.Party.IdentificationCountryId),P("@IdentificationType",request.Party.IdentificationTypeCode.Trim().ToUpperInvariant()),
                P("@Identification",request.Party.Identification.Trim()),P("@Normalized",normalizedIdentification),
                P("@Digit",Empty(request.Party.VerificationDigit)),P("@DisplayName",request.Party.DisplayName.Trim()),
                P("@LegalName",Empty(request.Party.LegalName)),P("@FirstName",Empty(request.Party.FirstName)),P("@LastName",Empty(request.Party.LastName)),
                P("@ActorId",actor.ActorId),P("@Now",now)],ct);
            await AddContactAsync(connection, transaction, partyId, "Email", request.Party.Email, now, ct);
            await AddContactAsync(connection, transaction, partyId, "Phone", request.Party.Phone, now, ct);
            await InsertSiteAsync(connection, transaction, actor, partyId, siteId, request.PrimarySite, now, ct);
            await transaction.CommitAsync(ct);
            return new PartyIdentityAcceptance(partyId, false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            throw new PartyConflictException("The Party identity or site is already in use.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
    public async Task<SupplierAcceptance> CreateSupplierAsync(
        PartyActorIdentity actor, Guid partyId, Guid supplierId, Guid siteId,
        CreateSupplierRequest request, string normalizedIdentification,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        try
        {
            await using var receipt=connection.CreateCommand();
            receipt.Transaction=transaction;
            receipt.CommandText="""
                SELECT r.SupplierId,s.PartyId,p.NormalizedIdentification
                FROM dbo.SupplierCreationReceipts r
                JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
                JOIN dbo.Parties p ON p.PartyId=s.PartyId
                WHERE r.BusinessId=@BusinessId AND r.OperationId=@OperationId;
                """;
            receipt.Parameters.AddRange([P("@BusinessId",actor.BusinessId),P("@OperationId",request.OperationId)]);
            await using(var existingReader=await receipt.ExecuteReaderAsync(ct))
            {
                if(await existingReader.ReadAsync(ct))
                {
                    if(!string.Equals(existingReader.GetString(2),normalizedIdentification,StringComparison.Ordinal))
                        throw new PartyConflictException("The operation ID was already used for another supplier.");
                    var accepted=new SupplierAcceptance(existingReader.GetGuid(0),existingReader.GetGuid(1),true);
                    await existingReader.CloseAsync(); await transaction.CommitAsync(ct); return accepted;
                }
            }

            await ValidateScopeAsync(connection,transaction,actor,request.PrimarySite,ct);
            var resolvedPartyId=await FindPartyIdAsync(connection,transaction,actor.TenantId,request.Party,normalizedIdentification,ct) ?? partyId;
            if(resolvedPartyId==partyId)
            {
                await ExecuteAsync(connection,transaction,"""
                    INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                      Identification,NormalizedIdentification,VerificationDigit,DisplayName,LegalName,FirstName,LastName,
                      CompletionStatus,IsActive,CreatedBy,CreatedAt)
                    VALUES(@PartyId,@TenantId,@PartyType,@CountryId,@IdentificationType,@Identification,@Normalized,
                      @Digit,@DisplayName,@LegalName,@FirstName,@LastName,N'Complete',1,@ActorId,@Now);
                    """,[
                    P("@PartyId",partyId),P("@TenantId",actor.TenantId),P("@PartyType",request.Party.PartyType),
                    P("@CountryId",request.Party.IdentificationCountryId),P("@IdentificationType",request.Party.IdentificationTypeCode.Trim().ToUpperInvariant()),
                    P("@Identification",request.Party.Identification.Trim()),P("@Normalized",normalizedIdentification),
                    P("@Digit",Empty(request.Party.VerificationDigit)),P("@DisplayName",request.Party.DisplayName.Trim()),
                    P("@LegalName",Empty(request.Party.LegalName)),P("@FirstName",Empty(request.Party.FirstName)),P("@LastName",Empty(request.Party.LastName)),
                    P("@ActorId",actor.ActorId),P("@Now",now)],ct);
                await AddContactAsync(connection,transaction,resolvedPartyId,"Email",request.Party.Email,now,ct);
                await AddContactAsync(connection,transaction,resolvedPartyId,"Phone",request.Party.Phone,now,ct);
            }

            var existingSupplierId=await FindSupplierIdAsync(connection,transaction,resolvedPartyId,actor.BusinessId,ct);
            if(existingSupplierId is not null) throw new PartyConflictException("The Party already has the Supplier role in this business.");
            var resolvedSupplierId=supplierId;
            if(resolvedSupplierId==supplierId)
            {
                await ExecuteAsync(connection,transaction,"""
                    INSERT dbo.Suppliers(SupplierId,BusinessId,PartyId,Identification,Name,IsActive,CreatedAt)
                    VALUES(@SupplierId,@BusinessId,@PartyId,@Identification,@Name,1,@Now);
                    """,[P("@SupplierId",supplierId),P("@BusinessId",actor.BusinessId),P("@PartyId",resolvedPartyId),
                    P("@Identification",request.Party.Identification.Trim()),P("@Name",request.Party.DisplayName.Trim()),P("@Now",now)],ct);
                await InsertSiteAsync(connection,transaction,actor,resolvedPartyId,siteId,request.PrimarySite,now,ct);
            }
            await ExecuteAsync(connection,transaction,"""
                INSERT dbo.SupplierCreationReceipts(BusinessId,OperationId,SupplierId,CreatedAt)
                VALUES(@BusinessId,@OperationId,@SupplierId,@Now);
                """,[P("@BusinessId",actor.BusinessId),P("@OperationId",request.OperationId),P("@SupplierId",resolvedSupplierId),P("@Now",now)],ct);
            await transaction.CommitAsync(ct);
            return new SupplierAcceptance(resolvedSupplierId,resolvedPartyId,false);
        }
        catch(SqlException ex) when(ex.Number is 2601 or 2627)
        { await transaction.RollbackAsync(ct); throw new PartyConflictException("The supplier identity or site is already in use."); }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task<PartyWorkspaceItem> UpdateAsync(
        PartyActorIdentity actor, Guid partyId, UpdatePartyRequest request, byte[] rowVersion,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await RequirePartyAsync(connection,transaction,actor,partyId,ct);
            await RequireMutablePartyAsync(connection,transaction,actor,partyId,ct);
            if(request.Sites is not null)
            {
                foreach(var site in request.Sites) await ValidateScopeAsync(connection,transaction,actor,site.Site,ct);
                await ApplySitesAsync(connection,transaction,actor,partyId,request.Sites,now,ct);
            }
            if(request.Customer is not null)
            {
                await using var customer=connection.CreateCommand(); customer.Transaction=transaction;
                customer.CommandText="""
                    IF @PriceChannelId IS NOT NULL AND NOT EXISTS(
                      SELECT 1 FROM dbo.PriceChannels
                      WHERE PriceChannelId=@PriceChannelId AND BusinessId=@BusinessId AND IsActive=1)
                      THROW 51066,'The selected price channel is not active in this business.',1;
                    DECLARE @CustomerId UNIQUEIDENTIFIER;
                    SELECT @CustomerId=CustomerId FROM dbo.Customers
                    WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                    IF @CustomerId IS NULL THROW 51063,'The Party is not a customer in the authenticated business.',1;
                    UPDATE dbo.Customers
                    SET RequiresElectronicInvoice=@RequiresElectronicInvoice,
                        UpdatedBy=@ActorId,UpdatedAt=@Now
                    WHERE CustomerId=@CustomerId;
                    UPDATE dbo.CustomerPricingSettings
                    SET PriceChannelId=@PriceChannelId,ValidFrom=COALESCE(@ValidFrom,@Now),
                        ValidUntil=@ValidUntil,UpdatedBy=@ActorId,UpdatedAt=@Now
                    WHERE CustomerId=@CustomerId;
                    IF @@ROWCOUNT=0 INSERT dbo.CustomerPricingSettings
                      (CustomerId,PriceChannelId,ValidFrom,ValidUntil,UpdatedBy,UpdatedAt)
                    VALUES(@CustomerId,@PriceChannelId,COALESCE(@ValidFrom,@Now),@ValidUntil,@ActorId,@Now);
                    """;
                customer.Parameters.AddRange([P("@PartyId",partyId),P("@BusinessId",actor.BusinessId),
                    P("@PriceChannelId",request.Customer.PriceChannelId),P("@RequiresElectronicInvoice",request.Customer.RequiresElectronicInvoice),
                    P("@ValidFrom",request.Customer.ValidFrom),P("@ValidUntil",request.Customer.ValidUntil),
                    P("@ActorId",actor.ActorId),P("@Now",now)]);
                await customer.ExecuteNonQueryAsync(ct);
            }
            if(request.Seller is not null)
            {
                await using var seller=connection.CreateCommand(); seller.Transaction=transaction;
                seller.CommandText="""
                    UPDATE dbo.CommerceSellers
                    SET Code=@Code,DefaultCommissionPercent=@Commission,
                        CommissionBasis=@Basis,CommissionTrigger=@Trigger
                    WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                    IF @@ROWCOUNT=0 THROW 51063,'The Party is not a seller in the authenticated business.',1;
                    """;
                seller.Parameters.AddRange([P("@PartyId",partyId),P("@BusinessId",actor.BusinessId),
                    P("@Code",request.Seller.Code.Trim().ToUpperInvariant()),P("@Commission",request.Seller.DefaultCommissionPercent),
                    P("@Basis",request.Seller.CommissionBasis),P("@Trigger",request.Seller.CommissionTrigger)]);
                await seller.ExecuteNonQueryAsync(ct);
            }
            if(request.Carrier is not null)
            {
                await using var carrier=connection.CreateCommand(); carrier.Transaction=transaction;
                carrier.CommandText="""
                    UPDATE dbo.Carriers
                    SET Code=@Code,TransportationMode=@Mode
                    WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                    IF @@ROWCOUNT=0 THROW 51063,'The Party is not a carrier in the authenticated business.',1;
                    """;
                carrier.Parameters.AddRange([P("@PartyId",partyId),P("@BusinessId",actor.BusinessId),
                    P("@Code",request.Carrier.Code.Trim().ToUpperInvariant()),P("@Mode",request.Carrier.TransportationMode)]);
                await carrier.ExecuteNonQueryAsync(ct);
            }
            await using var update=connection.CreateCommand(); update.Transaction=transaction;
            update.CommandText="""
                UPDATE dbo.Parties SET PartyType=@PartyType,DisplayName=@DisplayName,LegalName=@LegalName,
                  FirstName=@FirstName,LastName=@LastName,VerificationDigit=@Digit,UpdatedBy=@ActorId,UpdatedAt=@Now
                WHERE PartyId=@PartyId AND TenantId=@TenantId AND RowVersion=@RowVersion;
                IF @@ROWCOUNT=0 THROW 51062,'The Party changed after it was loaded.',1;
                UPDATE dbo.Suppliers SET Name=@DisplayName
                WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                UPDATE dbo.Employees SET Name=@DisplayName,UpdatedAt=@Now
                WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                """;
            update.Parameters.AddRange([P("@PartyId",partyId),P("@TenantId",actor.TenantId),P("@BusinessId",actor.BusinessId),
                P("@PartyType",request.PartyType),P("@DisplayName",request.DisplayName.Trim()),P("@LegalName",Empty(request.LegalName)),
                P("@FirstName",Empty(request.FirstName)),P("@LastName",Empty(request.LastName)),P("@Digit",Empty(request.VerificationDigit)),
                P("@ActorId",actor.ActorId),P("@Now",now),P("@RowVersion",rowVersion)]);
            await update.ExecuteNonQueryAsync(ct);
            await ReplacePrimaryContactAsync(connection,transaction,partyId,"Email",request.Email,now,ct);
            await ReplacePrimaryContactAsync(connection,transaction,partyId,"Phone",request.Phone,now,ct);
            await EnqueueCustomerChangeAsync(connection,transaction,actor.BusinessId,partyId,now,ct);
            await transaction.CommitAsync(ct);
            return await RequiredItemAsync(actor,partyId,ct);
        }
        catch(FormatException)
        { await transaction.RollbackAsync(ct); throw new PartyValidationException("A site row version is invalid."); }
        catch(SqlException ex) when(ex.Number is 51062 or 51063 or 51064 or 51065 or 51066)
        { await transaction.RollbackAsync(ct); throw new PartyConflictException(ex.Message); }
        catch(SqlException ex) when(ex.Number is 2601 or 2627)
        { await transaction.RollbackAsync(ct); throw new PartyConflictException("A site code is already used by this customer."); }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task<PartyWorkspaceItem> SetStatusAsync(
        PartyActorIdentity actor, Guid partyId, SetPartyBusinessStatusRequest request,
        byte[] rowVersion, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await RequirePartyAsync(connection,transaction,actor,partyId,ct);
            await RequireMutablePartyAsync(connection,transaction,actor,partyId,ct);
            await using var command=connection.CreateCommand(); command.Transaction=transaction;
            command.CommandText="""
                IF NOT EXISTS(SELECT 1 FROM dbo.Parties WHERE PartyId=@PartyId AND TenantId=@TenantId AND RowVersion=@RowVersion)
                  THROW 51062,'The Party changed after it was loaded.',1;
                UPDATE dbo.Customers SET IsActive=@Active,UpdatedBy=@ActorId,UpdatedAt=@Now
                WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                UPDATE dbo.Suppliers SET IsActive=@Active WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                UPDATE dbo.CommerceSellers SET IsActive=@Active WHERE PartyId=@PartyId AND BusinessId=@BusinessId;

                DELETE assignment
                FROM dbo.UserRoles assignment
                JOIN dbo.AppUsers app ON app.UserId=assignment.UserId AND app.PartyId=@PartyId
                JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.NormalizedName=N'SELLER'
                WHERE assignment.BusinessId=@BusinessId AND @Active=0;

                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt,AssignedByUserId)
                SELECT NEWID(),app.UserId,role.RoleId,@BusinessId,@Now,@ActorId
                FROM dbo.AppUsers app
                JOIN dbo.AppRoles role ON role.TenantId=app.TenantId AND role.NormalizedName=N'SELLER' AND role.IsActive=1
                WHERE app.PartyId=@PartyId AND app.IsActive=1 AND @Active=1
                  AND EXISTS(SELECT 1 FROM dbo.CommerceSellers seller WHERE seller.PartyId=@PartyId AND seller.BusinessId=@BusinessId AND seller.IsActive=1)
                  AND NOT EXISTS(SELECT 1 FROM dbo.UserRoles assignment WHERE assignment.UserId=app.UserId AND assignment.RoleId=role.RoleId AND assignment.BusinessId=@BusinessId);

                UPDATE dbo.Carriers SET IsActive=@Active WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                UPDATE dbo.Employees SET IsActive=@Active,UpdatedAt=@Now WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
                UPDATE dbo.AppUsers SET IsActive=@Active,UpdatedAt=@Now WHERE PartyId=@PartyId AND TenantId=@TenantId;
                UPDATE dbo.Parties SET UpdatedBy=@ActorId,UpdatedAt=@Now WHERE PartyId=@PartyId;
                """;
            command.Parameters.AddRange([P("@PartyId",partyId),P("@TenantId",actor.TenantId),P("@BusinessId",actor.BusinessId),
                P("@Active",request.IsActive),P("@ActorId",actor.ActorId),P("@Now",now),P("@RowVersion",rowVersion)]);
            await command.ExecuteNonQueryAsync(ct);
            await EnqueueCustomerChangeAsync(connection,transaction,actor.BusinessId,partyId,now,ct);
            await transaction.CommitAsync(ct);
            return await RequiredItemAsync(actor,partyId,ct);
        }
        catch(SqlException ex) when(ex.Number is 51062 or 51064)
        { await transaction.RollbackAsync(ct); throw new PartyConflictException(ex.Message); }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    private async Task<PartyWorkspaceItem> RequiredItemAsync(PartyActorIdentity actor,Guid partyId,CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT p.PartyId,p.PartyType,p.IdentificationTypeCode,p.Identification,p.VerificationDigit,
                   COALESCE(p.DisplayName,p.LegalName,N'Sin nombre'),p.LegalName,p.FirstName,p.LastName,
                   (SELECT TOP(1) Value FROM dbo.PartyContacts pc WHERE pc.PartyId=p.PartyId AND pc.ContactType=N'Email' AND pc.IsActive=1 ORDER BY pc.IsPrimary DESC,pc.CreatedAt),
                   (SELECT TOP(1) Value FROM dbo.PartyContacts pc WHERE pc.PartyId=p.PartyId AND pc.ContactType=N'Phone' AND pc.IsActive=1 ORDER BY pc.IsPrimary DESC,pc.CreatedAt),
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId) THEN 1 ELSE 0 END,
                   site.Name,site.CityName,
                   CASE WHEN EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                          OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                          OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                          OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId AND c.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId AND e.IsActive=1)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId AND u.IsActive=1)
                        THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                   p.CompletionStatus,p.RowVersion
            FROM dbo.Parties p
            OUTER APPLY(SELECT TOP(1) ps.Name,ci.Name CityName FROM dbo.PartySites ps
                        JOIN dbo.Cities ci ON ci.CityId=ps.CityId WHERE ps.PartyId=p.PartyId AND ps.IsActive=1
                        ORDER BY ps.IsPrimary DESC,ps.CreatedAt) site
            WHERE p.PartyId=@PartyId AND p.TenantId=@TenantId
              AND (EXISTS(SELECT 1 FROM dbo.Customers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.CommerceSellers s WHERE s.PartyId=p.PartyId AND s.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Carriers c WHERE c.PartyId=p.PartyId AND c.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.PartyId=p.PartyId AND e.BusinessId=@BusinessId)
                   OR EXISTS(SELECT 1 FROM dbo.AppUsers u WHERE u.PartyId=p.PartyId AND u.TenantId=@TenantId));
            """;
        command.Parameters.AddRange([P("@PartyId",partyId),P("@TenantId",actor.TenantId),P("@BusinessId",actor.BusinessId)]);
        await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)?Read(reader):throw new PartyForbiddenException("Party is outside the authenticated business.");
    }

    private static PartyWorkspaceItem Read(SqlDataReader r)
    {
        var roles=new List<string>(6); if(Convert.ToBoolean(r.GetValue(11)))roles.Add("Customer"); if(Convert.ToBoolean(r.GetValue(12)))roles.Add("Supplier"); if(Convert.ToBoolean(r.GetValue(13)))roles.Add("Seller"); if(Convert.ToBoolean(r.GetValue(14)))roles.Add("Carrier"); if(Convert.ToBoolean(r.GetValue(15)))roles.Add("Employee"); if(Convert.ToBoolean(r.GetValue(16)))roles.Add("User");
        return new PartyWorkspaceItem(r.GetGuid(0),r.GetString(1),S(r,2),S(r,3),S(r,4),r.GetString(5),S(r,6),S(r,7),S(r,8),
            S(r,9),S(r,10),roles,S(r,17),S(r,18),r.GetBoolean(19),r.GetString(20),Convert.ToBase64String((byte[])r[21]));
    }

    private static async Task ValidateScopeAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity a,PartySiteInput s,CancellationToken ct)
    {
        await using var command=c.CreateCommand(); command.Transaction=t;
        command.CommandText="""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
              THROW 51060,'Business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Cities ci JOIN dbo.AdministrativeDivisions d ON d.AdministrativeDivisionId=ci.AdministrativeDivisionId
              WHERE ci.CityId=@CityId AND d.AdministrativeDivisionId=@DivisionId AND d.CountryId=@CountryId AND ci.IsActive=1 AND d.IsActive=1)
              THROW 51061,'The geographic hierarchy is invalid or inactive.',1;
            """;
        command.Parameters.AddRange([P("@BusinessId",a.BusinessId),P("@TenantId",a.TenantId),P("@CityId",s.CityId),P("@DivisionId",s.AdministrativeDivisionId),P("@CountryId",s.CountryId)]);
        try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51060){throw new PartyForbiddenException(ex.Message);}catch(SqlException ex) when(ex.Number==51061){throw new PartyValidationException(ex.Message);}
    }

    private static async Task RequirePartyAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity a,Guid id,CancellationToken ct)
    {
        await using var command=c.CreateCommand();command.Transaction=t;command.CommandText="""
            IF NOT EXISTS(SELECT 1 FROM dbo.Parties p WHERE p.PartyId=@PartyId AND p.TenantId=@TenantId AND
              (EXISTS(SELECT 1 FROM dbo.Customers x WHERE x.PartyId=p.PartyId AND x.BusinessId=@BusinessId)
               OR EXISTS(SELECT 1 FROM dbo.Suppliers x WHERE x.PartyId=p.PartyId AND x.BusinessId=@BusinessId)
               OR EXISTS(SELECT 1 FROM dbo.CommerceSellers x WHERE x.PartyId=p.PartyId AND x.BusinessId=@BusinessId)
               OR EXISTS(SELECT 1 FROM dbo.Carriers x WHERE x.PartyId=p.PartyId AND x.BusinessId=@BusinessId)
               OR EXISTS(SELECT 1 FROM dbo.Employees x WHERE x.PartyId=p.PartyId AND x.BusinessId=@BusinessId)
               OR EXISTS(SELECT 1 FROM dbo.AppUsers x WHERE x.PartyId=p.PartyId AND x.TenantId=@TenantId)))
              THROW 51060,'Party is outside the authenticated business.',1;
            """;command.Parameters.AddRange([P("@PartyId",id),P("@TenantId",a.TenantId),P("@BusinessId",a.BusinessId)]);
        try{await command.ExecuteNonQueryAsync(ct);}catch(SqlException ex) when(ex.Number==51060){throw new PartyForbiddenException(ex.Message);}
    }

    private static async Task RequireMutablePartyAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity a,Guid id,CancellationToken ct)
    {
        await using var command=c.CreateCommand();command.Transaction=t;command.CommandText="""
            IF EXISTS(SELECT 1 FROM dbo.Parties WHERE PartyId=@PartyId AND TenantId=@TenantId
              AND Identification=N'222222222222' AND DisplayName=N'Consumidor final')
              THROW 51064,'Consumidor final is a protected system customer and cannot be changed.',1;
            """;
        command.Parameters.AddRange([P("@PartyId",id),P("@TenantId",a.TenantId)]);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task ApplySitesAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity actor,Guid partyId,IReadOnlyCollection<PartySiteSaveInput> sites,DateTimeOffset now,CancellationToken ct)
    {
        foreach(var value in sites.Where(value=>value.PartySiteId.HasValue))
        {
            if(string.IsNullOrWhiteSpace(value.RowVersion)) throw new FormatException();
            await using var check=c.CreateCommand();check.Transaction=t;check.CommandText="""
                IF NOT EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartySiteId=@SiteId AND PartyId=@PartyId AND RowVersion=@RowVersion)
                  THROW 51065,'A customer site changed after it was loaded.',1;
                """;
            check.Parameters.AddRange([P("@SiteId",value.PartySiteId),P("@PartyId",partyId),P("@RowVersion",Convert.FromBase64String(value.RowVersion))]);
            await check.ExecuteNonQueryAsync(ct);
        }
        await ExecuteAsync(c,t,"UPDATE dbo.PartySites SET IsActive=0,IsPrimary=0,UpdatedAt=@Now WHERE PartyId=@PartyId",[P("@PartyId",partyId),P("@Now",now)],ct);
        foreach(var value in sites)
        {
            var site=value.Site;
            if(value.PartySiteId is null)
            {
                await ExecuteAsync(c,t,"""
                    INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,Neighborhood,PostalCode,Email,Phone,GoogleMapsUrl,GooglePlaceId,Latitude,Longitude,IsPrimary,IsActive,CreatedBy,CreatedAt)
                    VALUES(@SiteId,@PartyId,@Code,@Name,@Country,@Division,@City,@Address,@Neighborhood,@Postal,@Email,@Phone,@Maps,@Place,@Latitude,@Longitude,@Primary,1,@Actor,@Now)
                    """,SiteParameters(ids.NewId(),partyId,site,actor.ActorId,now),ct);
                continue;
            }
            await ExecuteAsync(c,t,"""
                UPDATE dbo.PartySites SET Code=@Code,Name=@Name,CountryId=@Country,AdministrativeDivisionId=@Division,CityId=@City,
                  AddressLine=@Address,Neighborhood=@Neighborhood,PostalCode=@Postal,Email=@Email,Phone=@Phone,
                  GoogleMapsUrl=@Maps,GooglePlaceId=@Place,Latitude=@Latitude,Longitude=@Longitude,IsPrimary=@Primary,IsActive=1,UpdatedAt=@Now
                WHERE PartySiteId=@SiteId AND PartyId=@PartyId
                """,SiteParameters(value.PartySiteId.Value,partyId,site,actor.ActorId,now),ct);
        }
    }

    private static SqlParameter[] SiteParameters(Guid siteId,Guid partyId,PartySiteInput site,Guid actorId,DateTimeOffset now)=>
    [P("@SiteId",siteId),P("@PartyId",partyId),P("@Code",site.Code.Trim().ToUpperInvariant()),P("@Name",site.Name.Trim()),P("@Country",site.CountryId),P("@Division",site.AdministrativeDivisionId),P("@City",site.CityId),P("@Address",site.AddressLine.Trim()),P("@Neighborhood",Empty(site.Neighborhood)),P("@Postal",Empty(site.PostalCode)),P("@Email",Empty(site.Email)),P("@Phone",Empty(site.Phone)),P("@Maps",Empty(site.GoogleMapsUrl)),P("@Place",Empty(site.GooglePlaceId)),P("@Latitude",site.Latitude),P("@Longitude",site.Longitude),P("@Primary",site.IsPrimary),P("@Actor",actorId),P("@Now",now)];

    private static async Task<Guid?> FindPartyIdAsync(SqlConnection c,SqlTransaction t,Guid tenant,PartyInput p,string normalized,CancellationToken ct)
    { await using var x=c.CreateCommand();x.Transaction=t;x.CommandText="SELECT PartyId FROM dbo.Parties WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@Tenant AND IdentificationCountryId=@Country AND IdentificationTypeCode=@Type AND NormalizedIdentification=@Normalized";x.Parameters.AddRange([P("@Tenant",tenant),P("@Country",p.IdentificationCountryId),P("@Type",p.IdentificationTypeCode.Trim().ToUpperInvariant()),P("@Normalized",normalized)]);return await x.ExecuteScalarAsync(ct) as Guid?; }
    private static async Task<Guid?> FindSupplierIdAsync(SqlConnection c,SqlTransaction t,Guid party,Guid business,CancellationToken ct)
    { await using var x=c.CreateCommand();x.Transaction=t;x.CommandText="SELECT SupplierId FROM dbo.Suppliers WITH(UPDLOCK,HOLDLOCK) WHERE PartyId=@Party AND BusinessId=@Business";x.Parameters.AddRange([P("@Party",party),P("@Business",business)]);return await x.ExecuteScalarAsync(ct) as Guid?; }

    private async Task EnqueueCustomerChangeAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid partyId, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteAsync(connection,transaction,"""
            IF EXISTS(SELECT 1 FROM dbo.Customers WHERE BusinessId=@BusinessId AND PartyId=@PartyId)
            BEGIN
              DECLARE @Cursor BIGINT;
              SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND Stream=N'Customers';
              INSERT dbo.PosSynchronizationOutboxMessages
                (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              VALUES(@NotificationId,@BusinessId,N'Customers',@Cursor,@Now);
            END
            """,[P("@NotificationId",ids.NewId()),P("@BusinessId",businessId),P("@PartyId",partyId),P("@Now",now)],ct);
    }
    private async Task AddContactAsync(SqlConnection c,SqlTransaction t,Guid party,string type,string? value,DateTimeOffset now,CancellationToken ct)
    { if(string.IsNullOrWhiteSpace(value))return;var v=value.Trim();await ExecuteAsync(c,t,"INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt) VALUES(@Id,@Party,@Type,@Value,@Normalized,1,1,@Now)", [P("@Id",ids.NewId()),P("@Party",party),P("@Type",type),P("@Value",v),P("@Normalized",NormalizeContact(type,v)),P("@Now",now)],ct); }
    private async Task ReplacePrimaryContactAsync(SqlConnection c,SqlTransaction t,Guid party,string type,string? value,DateTimeOffset now,CancellationToken ct)
    {
        await ExecuteAsync(c,t,"UPDATE dbo.PartyContacts SET IsPrimary=0 WHERE PartyId=@Party AND ContactType=@Type",[P("@Party",party),P("@Type",type)],ct);
        if(string.IsNullOrWhiteSpace(value)) return;
        var normalized=NormalizeContact(type,value.Trim());
        await ExecuteAsync(c,t,"""
            IF EXISTS(SELECT 1 FROM dbo.PartyContacts WHERE PartyId=@Party AND ContactType=@Type AND NormalizedValue=@Normalized)
              UPDATE dbo.PartyContacts SET Value=@Value,IsPrimary=1,IsActive=1
              WHERE PartyId=@Party AND ContactType=@Type AND NormalizedValue=@Normalized;
            ELSE
              INSERT dbo.PartyContacts(PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
              VALUES(@Id,@Party,@Type,@Value,@Normalized,1,1,@Now);
            """,[P("@Id",ids.NewId()),P("@Party",party),P("@Type",type),P("@Value",value.Trim()),P("@Normalized",normalized),P("@Now",now)],ct);
    }
    private static Task InsertSiteAsync(SqlConnection c,SqlTransaction t,PartyActorIdentity a,Guid party,Guid id,PartySiteInput s,DateTimeOffset now,CancellationToken ct)=>ExecuteAsync(c,t,"""
        IF NOT EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@Party AND Code=@Code)
        INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,Neighborhood,PostalCode,Email,Phone,GoogleMapsUrl,GooglePlaceId,Latitude,Longitude,IsPrimary,IsActive,CreatedBy,CreatedAt)
        VALUES(@Id,@Party,@Code,@Name,@Country,@Division,@City,@Address,@Neighborhood,@Postal,@Email,@Phone,@GoogleMapsUrl,@GooglePlaceId,@Latitude,@Longitude,
          CASE WHEN EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@Party AND IsPrimary=1 AND IsActive=1) THEN 0 ELSE @Primary END,1,@Actor,@Now)
        """,[P("@Id",id),P("@Party",party),P("@Code",s.Code.Trim().ToUpperInvariant()),P("@Name",s.Name.Trim()),P("@Country",s.CountryId),P("@Division",s.AdministrativeDivisionId),P("@City",s.CityId),P("@Address",s.AddressLine.Trim()),P("@Neighborhood",Empty(s.Neighborhood)),P("@Postal",Empty(s.PostalCode)),P("@Email",Empty(s.Email)),P("@Phone",Empty(s.Phone)),P("@GoogleMapsUrl",Empty(s.GoogleMapsUrl)),P("@GooglePlaceId",Empty(s.GooglePlaceId)),P("@Latitude",s.Latitude),P("@Longitude",s.Longitude),P("@Primary",s.IsPrimary),P("@Actor",a.ActorId),P("@Now",now)],ct);
    private static async Task ExecuteAsync(SqlConnection c,SqlTransaction t,string sql,SqlParameter[] ps,CancellationToken ct){await using var x=c.CreateCommand();x.Transaction=t;x.CommandText=sql;x.Parameters.AddRange(ps);await x.ExecuteNonQueryAsync(ct);}
    private static SqlParameter P(string n,object? v)=>new(n,v??DBNull.Value);
    private static string? Empty(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
    private static string? S(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static string NormalizeContact(string type,string value)=>type=="Email"?value.ToUpperInvariant():string.Concat(value.Where(char.IsDigit));
}












