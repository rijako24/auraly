using Auraly.Application.Parties;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPartyStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IPartyStore
{
    public async Task<CustomerDetail> CreateCustomerAsync(
        PartyActorIdentity actor,
        Guid partyId,
        Guid customerId,
        Guid siteId,
        CreateCustomerRequest request,
        string normalizedIdentification,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await ValidateScopeAndGeographyAsync(connection, transaction, actor, request.PrimarySite, ct);
            var receipt = await ReceiptAsync(connection, transaction, actor.BusinessId, request.OperationId, ct);
            if (receipt is not null)
            {
                if (!receipt.Value.NormalizedIdentification.Equals(normalizedIdentification, StringComparison.Ordinal))
                    throw new PartyConflictException("The operation ID was already used for another identity.");
                await transaction.CommitAsync(ct);
                return await RequiredCustomerAsync(
                    actor.TenantId, actor.BusinessId, receipt.Value.CustomerId, ct);
            }

            var existingPartyId = await PartyIdAsync(
                connection, transaction, actor.TenantId, request.Party.IdentificationCountryId,
                request.Party.IdentificationTypeCode, normalizedIdentification, ct);
            var resolvedPartyId = existingPartyId ?? partyId;
            if (existingPartyId is null)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Parties
                      (PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                       Identification,NormalizedIdentification,VerificationDigit,DisplayName,LegalName,
                       FirstName,LastName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                    VALUES
                      (@PartyId,@TenantId,@PartyType,@CountryId,@IdentificationType,@Identification,
                       @Normalized,@VerificationDigit,@DisplayName,@LegalName,@FirstName,@LastName,N'Complete',1,@ActorId,@Now);
                    """,
                    [
                        P("@PartyId", resolvedPartyId), P("@TenantId", actor.TenantId),
                        P("@PartyType", request.Party.PartyType),
                        P("@CountryId", request.Party.IdentificationCountryId),
                        P("@IdentificationType", request.Party.IdentificationTypeCode.Trim().ToUpperInvariant()),
                        P("@Identification", request.Party.Identification.Trim()),
                        P("@Normalized", normalizedIdentification),
                        P("@VerificationDigit", request.Party.VerificationDigit?.Trim()),
                        P("@DisplayName", request.Party.DisplayName.Trim()),
                        P("@LegalName", request.Party.LegalName?.Trim()),
                        P("@FirstName", request.Party.FirstName?.Trim()),
                        P("@LastName", request.Party.LastName?.Trim()),
                        P("@ActorId", actor.ActorId), P("@Now", now)
                    ],
                    ct);
                await AddContactAsync(
                    connection, transaction, resolvedPartyId, "Email", request.Party.Email, now, ct);
                await AddContactAsync(
                    connection, transaction, resolvedPartyId, "Phone", request.Party.Phone, now, ct);
            }

            var existingCustomerId = await CustomerIdAsync(
                connection, transaction, resolvedPartyId, actor.BusinessId, ct);
            if (existingCustomerId is not null)
                throw new PartyConflictException("The Party already has the Customer role in this business.");
            var resolvedCustomerId = customerId;
            if (existingCustomerId is null)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT dbo.Customers
                      (CustomerId,PartyId,BusinessId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
                    VALUES (@CustomerId,@PartyId,@BusinessId,@RequiresElectronicInvoice,1,@ActorId,@Now);
                    """,
                    [
                        P("@CustomerId", resolvedCustomerId), P("@PartyId", resolvedPartyId),
                        P("@BusinessId", actor.BusinessId),
                        P("@RequiresElectronicInvoice", request.RequiresElectronicInvoice),
                        P("@ActorId", actor.ActorId), P("@Now", now)
                    ],
                    ct);
                if (existingPartyId is null || !await PartyHasSiteAsync(connection, transaction, resolvedPartyId, ct))
                    await InsertSiteAsync(
                        connection, transaction, actor, resolvedPartyId, siteId, request.PrimarySite, now, ct);
                if (request.Pricing is not null)
                    await InsertPricingAsync(
                        connection, transaction, actor, resolvedCustomerId, request.Pricing, now, ct);
                await ExecuteAsync(connection, transaction, """
                    DECLARE @Cursor BIGINT;
                    SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
                    FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
                    WHERE BusinessId=@BusinessId AND Stream=N'Customers';
                    INSERT dbo.PosSynchronizationOutboxMessages
                      (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                    VALUES(@NotificationId,@BusinessId,N'Customers',@Cursor,@Now);
                    """,
                    [P("@NotificationId", ids.NewId()), P("@BusinessId", actor.BusinessId), P("@Now", now)],
                    ct);
            }

            await ExecuteAsync(connection, transaction, """
                INSERT dbo.CustomerCreationReceipts(BusinessId,OperationId,CustomerId,CreatedAt)
                VALUES (@BusinessId,@OperationId,@CustomerId,@Now);
                """,
                [
                    P("@BusinessId", actor.BusinessId), P("@OperationId", request.OperationId),
                    P("@CustomerId", resolvedCustomerId), P("@Now", now)
                ],
                ct);
            await transaction.CommitAsync(ct);
            return await RequiredCustomerAsync(actor.TenantId, actor.BusinessId, resolvedCustomerId, ct);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            throw new PartyConflictException(
                "The Party already has the Customer role in this business, " +
                "or another unique Party value is already in use.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<CustomerDetail?> FindCustomerAsync(
        Guid tenantId,
        Guid businessId,
        Guid countryId,
        string identificationType,
        string normalizedIdentification,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.CustomerId
            FROM dbo.Parties p
            JOIN dbo.Customers c ON c.PartyId=p.PartyId AND c.BusinessId=@BusinessId
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            WHERE p.TenantId=@TenantId AND p.IdentificationCountryId=@CountryId
              AND p.IdentificationTypeCode=@IdentificationType
              AND p.NormalizedIdentification=@Normalized;
            """;
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId), P("@BusinessId", businessId), P("@CountryId", countryId),
            P("@IdentificationType", identificationType), P("@Normalized", normalizedIdentification)
        ]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid customerId
            ? await LoadCustomerAsync(connection, tenantId, businessId, customerId, ct)
            : null;
    }

    public async Task<CustomerDetail?> GetCustomerAsync(
        Guid tenantId, Guid businessId, Guid customerId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await LoadCustomerAsync(connection, tenantId, businessId, customerId, ct);
    }

    public async Task<CustomerPage> PageCustomersAsync(
        Guid tenantId,
        Guid businessId,
        int page,
        CustomerPageRequest request,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT_BIG(1)
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            WHERE c.BusinessId=@BusinessId
              AND (@Active IS NULL OR c.IsActive=@Active)
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%'
                   OR p.NormalizedIdentification LIKE @Search+N'%');

            SELECT c.CustomerId
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            WHERE c.BusinessId=@BusinessId
              AND (@Active IS NULL OR c.IsActive=@Active)
              AND (@Search IS NULL OR p.DisplayName LIKE N'%'+@Search+N'%'
                   OR p.NormalizedIdentification LIKE @Search+N'%')
            ORDER BY p.DisplayName,c.CustomerId
            OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId), P("@BusinessId", businessId), P("@Active", request.IsActive),
            P("@Search", request.Search?.Trim()), P("@Offset", (page - 1) * request.PageSize),
            P("@Take", request.PageSize)
        ]);
        var idsToLoad = new List<Guid>();
        long total;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            total = reader.GetInt64(0);
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct)) idsToLoad.Add(reader.GetGuid(0));
        }
        var customers = new List<CustomerDetail>(idsToLoad.Count);
        foreach (var id in idsToLoad)
        {
            var customer = await LoadCustomerAsync(connection, tenantId, businessId, id, ct);
            if (customer is not null) customers.Add(customer);
        }
        return new CustomerPage(customers, page, request.PageSize, checked((int)total));
    }

    public async Task<PartySiteDetail> AddSiteAsync(
        PartyActorIdentity actor,
        Guid customerId,
        Guid siteId,
        AddPartySiteRequest request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                ct);
        try
        {
            await ValidateScopeAndGeographyAsync(connection, transaction, actor, request.Site, ct);
            var receipt = await SiteReceiptAsync(
                connection, transaction, actor.BusinessId, request.OperationId, ct);
            if (receipt is not null)
            {
                if (receipt.Value.CustomerId != customerId)
                    throw new PartyConflictException(
                        "The operation ID was already used for another customer.");
                await transaction.CommitAsync(ct);
                return (await RequiredCustomerAsync(
                    actor.TenantId, actor.BusinessId, customerId, ct))
                    .Sites.Single(site => site.PartySiteId == receipt.Value.PartySiteId);
            }
            await using var partyCommand = connection.CreateCommand();
            partyCommand.Transaction = transaction;
            partyCommand.CommandText = """
                SELECT c.PartyId FROM dbo.Customers c
                JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
                WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId;
                """;
            partyCommand.Parameters.AddRange(
            [
                P("@TenantId", actor.TenantId), P("@BusinessId", actor.BusinessId),
                P("@CustomerId", customerId)
            ]);
            var partyValue = await partyCommand.ExecuteScalarAsync(ct);
            if (partyValue is not Guid partyId)
                throw new PartyForbiddenException("Customer does not belong to the authenticated business.");
            await InsertSiteAsync(connection, transaction, actor, partyId, siteId, request.Site, now, ct);
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT dbo.PartySiteCreationReceipts
                  (BusinessId,OperationId,CustomerId,PartySiteId,CreatedAt)
                VALUES (@BusinessId,@OperationId,@CustomerId,@PartySiteId,@Now);
                """,
                [
                    P("@BusinessId", actor.BusinessId),
                    P("@OperationId", request.OperationId),
                    P("@CustomerId", customerId),
                    P("@PartySiteId", siteId),
                    P("@Now", now)
                ],
                ct);
            await transaction.CommitAsync(ct);
            return (await RequiredCustomerAsync(actor.TenantId, actor.BusinessId, customerId, ct))
                .Sites.Single(site => site.PartySiteId == siteId);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            throw new PartyConflictException("The site code or primary site is already in use.");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public Task<IReadOnlyCollection<CountryItem>> CountriesAsync(
        bool includeInactive, CancellationToken ct) =>
        QueryAsync(
            """
            WITH RankedCountries AS
            (
                SELECT country.CountryId,country.Code,country.Name,country.IsActive,
                       ROW_NUMBER() OVER
                       (
                           PARTITION BY UPPER(LTRIM(RTRIM(country.Name)))
                           ORDER BY
                             CASE WHEN EXISTS(SELECT 1 FROM dbo.Parties party WHERE party.IdentificationCountryId=country.CountryId)
                                    OR EXISTS(SELECT 1 FROM dbo.PartySites site WHERE site.CountryId=country.CountryId)
                                  THEN 0 ELSE 1 END,
                             CASE WHEN country.Code LIKE '[A-Z][A-Z]' THEN 0 ELSE 1 END,
                             country.Code
                       ) AS Position
                FROM dbo.Countries country
                WHERE @IncludeInactive=1 OR country.IsActive=1
            )
            SELECT CountryId,Code,Name,IsActive
            FROM RankedCountries WHERE Position=1 ORDER BY Name;
            """,
            [P("@IncludeInactive", includeInactive)],
            reader => new CountryItem(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)),
            ct);

    public Task<IReadOnlyCollection<AdministrativeDivisionItem>> DivisionsAsync(
        Guid countryId, bool includeInactive, CancellationToken ct) =>
        QueryAsync(
            """
            SELECT AdministrativeDivisionId,CountryId,Code,Name,DivisionType,IsActive
            FROM dbo.AdministrativeDivisions
            WHERE CountryId=@ParentId AND (@IncludeInactive=1 OR IsActive=1) ORDER BY Name;
            """,
            [P("@ParentId", countryId), P("@IncludeInactive", includeInactive)],
            reader => new AdministrativeDivisionItem(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)),
            ct);

    public Task<IReadOnlyCollection<CityItem>> CitiesAsync(
        Guid divisionId, bool includeInactive, CancellationToken ct) =>
        QueryAsync(
            """
            SELECT CityId,AdministrativeDivisionId,Code,Name,IsActive
            FROM dbo.Cities
            WHERE AdministrativeDivisionId=@ParentId AND (@IncludeInactive=1 OR IsActive=1) ORDER BY Name;
            """,
            [P("@ParentId", divisionId), P("@IncludeInactive", includeInactive)],
            reader => new CityItem(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4)),
            ct);

    public Task<IReadOnlyCollection<GeographyHierarchyItem>> GeographyHierarchyAsync(
        bool includeInactive, CancellationToken ct) =>
        QueryAsync(
            """
            SELECT CountryId AS Id,CAST(NULL AS uniqueidentifier) AS ParentId,N'Country' AS [Level],Code,Name,IsActive
            FROM dbo.Countries WHERE @IncludeInactive=1 OR IsActive=1
            UNION ALL
            SELECT AdministrativeDivisionId,CountryId,N'Division',Code,Name,IsActive
            FROM dbo.AdministrativeDivisions WHERE @IncludeInactive=1 OR IsActive=1
            UNION ALL
            SELECT CityId,AdministrativeDivisionId,N'City',Code,Name,IsActive
            FROM dbo.Cities WHERE @IncludeInactive=1 OR IsActive=1
            ORDER BY [Level],Name;
            """,
            [P("@IncludeInactive", includeInactive)],
            reader => new GeographyHierarchyItem(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)), ct);

    public async Task<CountryItem> CreateCountryAsync(
        PartyActorIdentity actor, Guid id, SaveCountryRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteMasterAsync(
            """
            INSERT dbo.Countries(CountryId,Code,Name,IsActive,CreatedAt)
            VALUES(@Id,@Code,@Name,@Active,@Now);
            """,
            [P("@Id", id), P("@Code", request.Code.Trim().ToUpperInvariant()), P("@Name", request.Name.Trim()),
             P("@Active", request.IsActive), P("@Now", now)], ct);
        return new CountryItem(id, request.Code.Trim().ToUpperInvariant(), request.Name.Trim(), request.IsActive);
    }

    public async Task<AdministrativeDivisionItem> CreateDivisionAsync(
        PartyActorIdentity actor, Guid id, SaveAdministrativeDivisionRequest request,
        DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteMasterAsync(
            """
            INSERT dbo.AdministrativeDivisions
              (AdministrativeDivisionId,CountryId,Code,Name,DivisionType,IsActive,CreatedAt)
            SELECT @Id,CountryId,@Code,@Name,@Type,@Active,@Now
            FROM dbo.Countries WHERE CountryId=@CountryId AND IsActive=1;
            IF @@ROWCOUNT=0 THROW 51031,'Country not found or inactive.',1;
            """,
            [
                P("@Id", id), P("@CountryId", request.CountryId),
                P("@Code", request.Code.Trim().ToUpperInvariant()), P("@Name", request.Name.Trim()),
                P("@Type", request.DivisionType.Trim()), P("@Active", request.IsActive), P("@Now", now)
            ],
            ct);
        return new AdministrativeDivisionItem(
            id, request.CountryId, request.Code.Trim().ToUpperInvariant(), request.Name.Trim(),
            request.DivisionType.Trim(), request.IsActive);
    }

    public async Task<CityItem> CreateCityAsync(
        PartyActorIdentity actor, Guid id, SaveCityRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteMasterAsync(
            """
            INSERT dbo.Cities(CityId,AdministrativeDivisionId,Code,Name,IsActive,CreatedAt)
            SELECT @Id,AdministrativeDivisionId,@Code,@Name,@Active,@Now
            FROM dbo.AdministrativeDivisions
            WHERE AdministrativeDivisionId=@DivisionId AND IsActive=1;
            IF @@ROWCOUNT=0 THROW 51032,'Administrative division not found or inactive.',1;
            """,
            [
                P("@Id", id), P("@DivisionId", request.AdministrativeDivisionId),
                P("@Code", request.Code.Trim().ToUpperInvariant()), P("@Name", request.Name.Trim()),
                P("@Active", request.IsActive), P("@Now", now)
            ],
            ct);
        return new CityItem(
            id, request.AdministrativeDivisionId, request.Code.Trim().ToUpperInvariant(),
            request.Name.Trim(), request.IsActive);
    }

    public async Task<CountryItem> UpdateCountryAsync(PartyActorIdentity actor, Guid id, SaveCountryRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteMasterAsync("""
            UPDATE dbo.Countries SET Code=@Code,Name=@Name,IsActive=@Active,UpdatedAt=@Now WHERE CountryId=@Id;
            IF @@ROWCOUNT=0 THROW 51033,'Country not found.',1;
            """, [P("@Id",id),P("@Code",request.Code.Trim().ToUpperInvariant()),P("@Name",request.Name.Trim()),P("@Active",request.IsActive),P("@Now",now)], ct);
        return new(id,request.Code.Trim().ToUpperInvariant(),request.Name.Trim(),request.IsActive);
    }

    public async Task<AdministrativeDivisionItem> UpdateDivisionAsync(PartyActorIdentity actor, Guid id, SaveAdministrativeDivisionRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteMasterAsync("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Countries WHERE CountryId=@CountryId) THROW 51031,'Country not found.',1;
            UPDATE dbo.AdministrativeDivisions SET CountryId=@CountryId,Code=@Code,Name=@Name,DivisionType=@Type,IsActive=@Active,UpdatedAt=@Now WHERE AdministrativeDivisionId=@Id;
            IF @@ROWCOUNT=0 THROW 51034,'Administrative division not found.',1;
            """, [P("@Id",id),P("@CountryId",request.CountryId),P("@Code",request.Code.Trim().ToUpperInvariant()),P("@Name",request.Name.Trim()),P("@Type",request.DivisionType.Trim()),P("@Active",request.IsActive),P("@Now",now)], ct);
        return new(id,request.CountryId,request.Code.Trim().ToUpperInvariant(),request.Name.Trim(),request.DivisionType.Trim(),request.IsActive);
    }

    public async Task<CityItem> UpdateCityAsync(PartyActorIdentity actor, Guid id, SaveCityRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteMasterAsync("""
            IF NOT EXISTS(SELECT 1 FROM dbo.AdministrativeDivisions WHERE AdministrativeDivisionId=@DivisionId) THROW 51032,'Administrative division not found.',1;
            UPDATE dbo.Cities SET AdministrativeDivisionId=@DivisionId,Code=@Code,Name=@Name,IsActive=@Active,UpdatedAt=@Now WHERE CityId=@Id;
            IF @@ROWCOUNT=0 THROW 51035,'City not found.',1;
            """, [P("@Id",id),P("@DivisionId",request.AdministrativeDivisionId),P("@Code",request.Code.Trim().ToUpperInvariant()),P("@Name",request.Name.Trim()),P("@Active",request.IsActive),P("@Now",now)], ct);
        return new(id,request.AdministrativeDivisionId,request.Code.Trim().ToUpperInvariant(),request.Name.Trim(),request.IsActive);
    }

    private async Task ExecuteMasterAsync(string sql, SqlParameter[] parameters, CancellationToken ct)
    {
        try
        {
            await using var connection = connections.Create();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new PartyConflictException("A master record with the same code already exists.");
        }
        catch (SqlException exception) when (exception.Number is 51031 or 51032 or 51033 or 51034 or 51035)
        {
            throw new PartyValidationException(exception.Message);
        }
    }

    private async Task<CustomerDetail> RequiredCustomerAsync(
        Guid tenantId, Guid businessId, Guid customerId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await LoadCustomerAsync(connection, tenantId, businessId, customerId, ct)
            ?? throw new InvalidOperationException("The customer was not persisted.");
    }

    private static async Task<CustomerDetail?> LoadCustomerAsync(
        SqlConnection connection, Guid tenantId, Guid businessId, Guid customerId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.CustomerId,p.PartyId,c.BusinessId,p.PartyType,p.IdentificationTypeCode,
              p.Identification,p.NormalizedIdentification,p.VerificationDigit,p.DisplayName,
              p.LegalName,p.FirstName,p.LastName,
              (SELECT TOP(1) Value FROM dbo.PartyContacts x
                 WHERE x.PartyId=p.PartyId AND x.ContactType=N'Email' AND x.IsActive=1
                 ORDER BY x.IsPrimary DESC,x.CreatedAt),
              (SELECT TOP(1) Value FROM dbo.PartyContacts x
                 WHERE x.PartyId=p.PartyId AND x.ContactType=N'Phone' AND x.IsActive=1
                 ORDER BY x.IsPrimary DESC,x.CreatedAt),
              ps.PriceListId,ps.PriceChannelId,c.RequiresElectronicInvoice,c.IsActive
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId AND b.TenantId=@TenantId
            LEFT JOIN dbo.CustomerPricingSettings ps ON ps.CustomerId=c.CustomerId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId;

            SELECT s.PartySiteId,s.Code,s.Name,co.CountryId,co.Code,co.Name,
              d.AdministrativeDivisionId,d.Code,d.Name,ci.CityId,ci.Code,ci.Name,
              s.AddressLine,s.Neighborhood,s.PostalCode,s.Email,s.Phone,s.GoogleMapsUrl,s.GooglePlaceId,s.Latitude,s.Longitude,s.IsPrimary,s.IsActive,s.RowVersion
            FROM dbo.PartySites s
            JOIN dbo.Customers c ON c.PartyId=s.PartyId AND c.CustomerId=@CustomerId
            JOIN dbo.Countries co ON co.CountryId=s.CountryId
            JOIN dbo.AdministrativeDivisions d ON d.AdministrativeDivisionId=s.AdministrativeDivisionId
            JOIN dbo.Cities ci ON ci.CityId=s.CityId
            WHERE c.BusinessId=@BusinessId ORDER BY s.IsPrimary DESC,s.Name;
            """;
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId), P("@BusinessId", businessId), P("@CustomerId", customerId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var header = new object?[18];
        reader.GetValues(header);
        var sites = new List<PartySiteDetail>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            sites.Add(new PartySiteDetail(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetString(5),
                reader.GetGuid(6), reader.GetString(7), reader.GetString(8),
                reader.GetGuid(9), reader.GetString(10), reader.GetString(11),
                reader.GetString(12), NullString(reader, 13), NullString(reader, 14),
                NullString(reader, 15), NullString(reader, 16), reader.GetBoolean(21), reader.GetBoolean(22),
                NullString(reader, 17), NullString(reader, 18),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                Convert.ToBase64String((byte[])reader[23])));
        return new CustomerDetail(
            (Guid)header[0]!, (Guid)header[1]!, (Guid)header[2]!, (string)header[3]!,
            header[4] as string, header[5] as string, header[6] as string,
            header[7] as string, header[8] as string, header[9] as string,
            header[10] as string, header[11] as string, header[12] as string, header[13] as string,
            header[14] is DBNull ? null : (Guid?)header[14],
            header[15] is DBNull ? null : (Guid?)header[15],
            (bool)header[17]!, sites, (bool)header[16]!);
    }

    private static async Task ValidateScopeAndGeographyAsync(
        SqlConnection connection, SqlTransaction transaction, PartyActorIdentity actor,
        PartySiteInput site, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.Businesses
              WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
              THROW 51030,'Business is outside the authenticated tenant.',1;
            IF NOT EXISTS (
              SELECT 1 FROM dbo.Cities ci
              JOIN dbo.AdministrativeDivisions d ON d.AdministrativeDivisionId=ci.AdministrativeDivisionId
              JOIN dbo.Countries co ON co.CountryId=d.CountryId
              WHERE ci.CityId=@CityId AND d.AdministrativeDivisionId=@DivisionId
                AND co.CountryId=@CountryId AND ci.IsActive=1 AND d.IsActive=1 AND co.IsActive=1)
              THROW 51033,'The geographic hierarchy is invalid or inactive.',1;
            """;
        command.Parameters.AddRange(
        [
            P("@BusinessId", actor.BusinessId), P("@TenantId", actor.TenantId),
            P("@CountryId", site.CountryId), P("@DivisionId", site.AdministrativeDivisionId),
            P("@CityId", site.CityId)
        ]);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number is 51030)
        {
            throw new PartyForbiddenException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 51033)
        {
            throw new PartyValidationException(exception.Message);
        }
    }

    private static async Task<(Guid CustomerId, string NormalizedIdentification)?> ReceiptAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid operationId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.CustomerId,p.NormalizedIdentification
            FROM dbo.CustomerCreationReceipts r
            JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE r.BusinessId=@BusinessId AND r.OperationId=@OperationId;
            """;
        command.Parameters.AddRange([P("@BusinessId", businessId), P("@OperationId", operationId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private static async Task<(Guid CustomerId, Guid PartySiteId)?> SiteReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid operationId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CustomerId,PartySiteId
            FROM dbo.PartySiteCreationReceipts WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND OperationId=@OperationId;
            """;
        command.Parameters.AddRange(
        [
            P("@BusinessId", businessId),
            P("@OperationId", operationId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? (reader.GetGuid(0), reader.GetGuid(1))
            : null;
    }

    private static async Task<Guid?> PartyIdAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid countryId,
        string type, string normalized, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT PartyId FROM dbo.Parties WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND IdentificationCountryId=@CountryId
              AND IdentificationTypeCode=@Type AND NormalizedIdentification=@Normalized;
            """;
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId), P("@CountryId", countryId),
            P("@Type", type.Trim().ToUpperInvariant()), P("@Normalized", normalized)
        ]);
        return await command.ExecuteScalarAsync(ct) as Guid?;
    }

    private static async Task<Guid?> CustomerIdAsync(
        SqlConnection connection, SqlTransaction transaction, Guid partyId, Guid businessId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CustomerId FROM dbo.Customers WITH (UPDLOCK,HOLDLOCK)
            WHERE PartyId=@PartyId AND BusinessId=@BusinessId;
            """;
        command.Parameters.AddRange([P("@PartyId", partyId), P("@BusinessId", businessId)]);
        return await command.ExecuteScalarAsync(ct) as Guid?;
    }

    private async Task AddContactAsync(
        SqlConnection connection, SqlTransaction transaction, Guid partyId,
        string type, string? value, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        var normalized = type == "Email"
            ? trimmed.ToUpperInvariant()
            : string.Concat(trimmed.Where(char.IsDigit));
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.PartyContacts
              (PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
            VALUES(@Id,@PartyId,@Type,@Value,@Normalized,1,1,@Now);
            """,
            [
                P("@Id", ids.NewId()), P("@PartyId", partyId), P("@Type", type),
                P("@Value", trimmed), P("@Normalized", normalized), P("@Now", now)
            ],
            ct);
    }
    private static async Task<bool> PartyHasSiteAsync(
        SqlConnection connection, SqlTransaction transaction, Guid partyId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.PartySites WITH(UPDLOCK,HOLDLOCK)
                WHERE PartyId=@PartyId AND IsActive=1
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@PartyId", partyId);
        return (bool)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("Could not verify the Party site."));
    }


    private static Task InsertSiteAsync(
        SqlConnection connection, SqlTransaction transaction, PartyActorIdentity actor,
        Guid partyId, Guid siteId, PartySiteInput site, DateTimeOffset now, CancellationToken ct) =>
        ExecuteAsync(connection, transaction, """
            INSERT dbo.PartySites
              (PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
               AddressLine,Neighborhood,PostalCode,Email,Phone,GoogleMapsUrl,GooglePlaceId,Latitude,Longitude,IsPrimary,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@SiteId,@PartyId,@Code,@Name,@CountryId,@DivisionId,@CityId,
               @Address,@Neighborhood,@PostalCode,@Email,@Phone,@GoogleMapsUrl,@GooglePlaceId,@Latitude,@Longitude,
               CASE WHEN EXISTS(SELECT 1 FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1)
                    THEN 0 ELSE @Primary END,1,@ActorId,@Now);
            """,
            [
                P("@SiteId", siteId), P("@PartyId", partyId), P("@Code", site.Code.Trim().ToUpperInvariant()),
                P("@Name", site.Name.Trim()), P("@CountryId", site.CountryId),
                P("@DivisionId", site.AdministrativeDivisionId), P("@CityId", site.CityId),
                P("@Address", site.AddressLine.Trim()), P("@Neighborhood", site.Neighborhood?.Trim()),
                P("@PostalCode", site.PostalCode?.Trim()), P("@Email", site.Email?.Trim()),
                P("@Phone", site.Phone?.Trim()), P("@GoogleMapsUrl", site.GoogleMapsUrl?.Trim()),
                P("@GooglePlaceId", site.GooglePlaceId?.Trim()), P("@Latitude", site.Latitude),
                P("@Longitude", site.Longitude), P("@Primary", site.IsPrimary),
                P("@ActorId", actor.ActorId), P("@Now", now)
            ],
            ct);

    private static async Task InsertPricingAsync(
        SqlConnection connection, SqlTransaction transaction, PartyActorIdentity actor,
        Guid customerId, CustomerPricingInput pricing, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF @PriceListId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.PriceLists WHERE PriceListId=@PriceListId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51034,'Price list is outside the customer business.',1;
            IF @PriceChannelId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@PriceChannelId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51035,'Price channel is outside the customer business.',1;
            INSERT dbo.CustomerPricingSettings
              (CustomerId,PriceListId,PriceChannelId,UpdatedBy,UpdatedAt)
            VALUES(@CustomerId,@PriceListId,@PriceChannelId,@ActorId,@Now);
            """;
        command.Parameters.AddRange(
        [
            P("@CustomerId", customerId), P("@BusinessId", actor.BusinessId),
            P("@PriceListId", pricing.PriceListId), P("@PriceChannelId", pricing.PriceChannelId),
            P("@ActorId", actor.ActorId), P("@Now", now)
        ]);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number is 51034 or 51035)
        {
            throw new PartyValidationException(exception.Message);
        }
    }

    private async Task<IReadOnlyCollection<T>> QueryAsync<T>(
        string sql, SqlParameter[] parameters, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) values.Add(map(reader));
        return values;
    }

    private static async Task ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction, string sql,
        SqlParameter[] parameters, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string? NullString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static SqlParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);
}
