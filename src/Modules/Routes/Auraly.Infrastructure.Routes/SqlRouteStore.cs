using System.Data;
using Auraly.Application.Routes;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Routes;
using Auraly.Domain.Routes;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Routes;

public sealed class SqlRouteStore(
    RoutesSqlConnectionFactory connections,
    IAuralyIdGenerator ids) : IRouteStore
{
    public async Task<SalesRoutePage> PageAsync(
        RouteActorIdentity actor, SalesRouteQuery query, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string source = """
            FROM dbo.SalesRoutes route
            INNER JOIN dbo.Businesses business ON business.BusinessId=route.BusinessId
            INNER JOIN dbo.CommerceSellers seller ON seller.SellerId=route.SellerId AND seller.BusinessId=route.BusinessId
            INNER JOIN dbo.Parties sellerParty ON sellerParty.PartyId=seller.PartyId
            LEFT JOIN dbo.SalesZones zone ON zone.ZoneId=route.ZoneId AND zone.BusinessId=route.BusinessId
            OUTER APPLY(SELECT COUNT(*) StopCount FROM dbo.SalesRouteStops stop WHERE stop.RouteId=route.RouteId AND stop.IsActive=1) stops
            OUTER APPLY(SELECT COUNT(*) ScheduleCount FROM dbo.SalesRouteSchedules schedule WHERE schedule.RouteId=route.RouteId AND schedule.IsActive=1) schedules
            WHERE route.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND (@Search IS NULL OR route.Code LIKE '%'+@Search+'%' OR route.Name LIKE '%'+@Search+'%'
                   OR seller.Code LIKE '%'+@Search+'%' OR sellerParty.DisplayName LIKE '%'+@Search+'%'
                   OR zone.Name LIKE '%'+@Search+'%')
              AND (@SellerId IS NULL OR route.SellerId=@SellerId)
              AND (@ZoneId IS NULL OR route.ZoneId=@ZoneId)
              AND (@IsActive IS NULL OR route.IsActive=@IsActive)
              AND (@DayOfWeek IS NULL OR EXISTS(SELECT 1 FROM dbo.SalesRouteSchedules daySchedule WHERE daySchedule.RouteId=route.RouteId AND daySchedule.DayOfWeek=@DayOfWeek AND daySchedule.IsActive=1))
              AND (@PreparationStatus IS NULL OR @PreparationStatus=CASE WHEN schedules.ScheduleCount>0 AND stops.StopCount>0 THEN N'Ready' ELSE N'Draft' END)
              AND (@ReadAll=1 OR EXISTS(
                    SELECT 1 FROM dbo.AppUsers currentUser
                    INNER JOIN dbo.CommerceSellers currentSeller ON currentSeller.PartyId=currentUser.PartyId AND currentSeller.BusinessId=@BusinessId AND currentSeller.IsActive=1
                    WHERE currentUser.UserId=@UserId AND currentUser.TenantId=@TenantId AND currentSeller.SellerId=route.SellerId))
            """;
        int total;
        await using (var count = new SqlCommand("SELECT COUNT(*) " + source, connection))
        {
            AddQuery(count, actor, query);
            total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        }
        await using var command = new SqlCommand("""
            SELECT route.RouteId,route.Code,route.Name,route.SellerId,sellerParty.DisplayName,
              route.ZoneId,zone.Name,route.IsActive,
              CASE WHEN schedules.ScheduleCount>0 AND stops.StopCount>0 THEN N'Ready' ELSE N'Draft' END,
              stops.StopCount,COALESCE(route.UpdatedAt,route.CreatedAt),route.RowVersion,
              (SELECT STRING_AGG(CONVERT(nvarchar(1),schedule.DayOfWeek),N',') WITHIN GROUP(ORDER BY schedule.DayOfWeek)
               FROM dbo.SalesRouteSchedules schedule WHERE schedule.RouteId=route.RouteId AND schedule.IsActive=1)
            """ + Environment.NewLine + source + Environment.NewLine + """
            ORDER BY route.Name,route.RouteId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        AddQuery(command, actor, query);
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", query.PageSize);
        var items = new List<SalesRouteListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetBoolean(7), reader.GetString(8), reader.GetInt32(9), ParseDays(reader.IsDBNull(12) ? null : reader.GetString(12)),
                reader.GetDateTimeOffset(10), Version(reader, 11)));
        return new(items, query.Page, query.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (decimal)query.PageSize));
    }

    public async Task<SalesRouteDetail?> GetAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT route.RouteId,route.BusinessId,route.Code,route.Name,route.SellerId,sellerParty.DisplayName,
              route.ZoneId,zone.Name,route.Notes,route.IsActive,route.CreatedAt,
              COALESCE(route.UpdatedAt,route.CreatedAt),route.RowVersion,
              CASE WHEN EXISTS(SELECT 1 FROM dbo.SalesRouteSchedules x WHERE x.RouteId=route.RouteId AND x.IsActive=1)
                         AND EXISTS(SELECT 1 FROM dbo.SalesRouteStops x WHERE x.RouteId=route.RouteId AND x.IsActive=1)
                   THEN N'Ready' ELSE N'Draft' END
            FROM dbo.SalesRoutes route
            INNER JOIN dbo.Businesses business ON business.BusinessId=route.BusinessId AND business.TenantId=@TenantId
            INNER JOIN dbo.CommerceSellers seller ON seller.SellerId=route.SellerId AND seller.BusinessId=route.BusinessId
            INNER JOIN dbo.Parties sellerParty ON sellerParty.PartyId=seller.PartyId
            LEFT JOIN dbo.SalesZones zone ON zone.ZoneId=route.ZoneId
            WHERE route.RouteId=@RouteId AND route.BusinessId=@BusinessId;

            SELECT RouteScheduleId,DayOfWeek,RunOrder,PlannedStartTime
            FROM dbo.SalesRouteSchedules
            WHERE RouteId=@RouteId AND IsActive=1 ORDER BY DayOfWeek;

            SELECT stop.RouteStopId,stop.CustomerId,stop.PartySiteId,stop.Sequence,party.DisplayName,
              party.Identification,site.Name,site.AddressLine,site.Neighborhood,city.Name,
              COALESCE(site.Phone,phone.Value),site.GoogleMapsUrl,site.Latitude,site.Longitude,
              stop.PlannedVisitTime,stop.VisitNote,stop.RowVersion
            FROM dbo.SalesRouteStops stop
            INNER JOIN dbo.Customers customer ON customer.CustomerId=stop.CustomerId AND customer.BusinessId=@BusinessId
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId
            INNER JOIN dbo.PartySites site ON site.PartySiteId=stop.PartySiteId AND site.PartyId=party.PartyId
            INNER JOIN dbo.Cities city ON city.CityId=site.CityId
            OUTER APPLY(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact
                        WHERE contact.PartyId=party.PartyId AND contact.ContactType=N'Phone' AND contact.IsActive=1
                        ORDER BY contact.IsPrimary DESC,contact.CreatedAt) phone
            WHERE stop.RouteId=@RouteId AND stop.IsActive=1 ORDER BY stop.Sequence;
            """, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RouteId", routeId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (!actor.Permissions.Contains(RoutePermissionCodes.ReadAll))
        {
            var sellerId = reader.GetGuid(4);
            if (!await UserOwnsSellerAsync(actor, sellerId, ct))
                return null;
        }

        var header = new
        {
            RouteId = reader.GetGuid(0), BusinessId = reader.GetGuid(1), Code = reader.GetString(2), Name = reader.GetString(3),
            SellerId = reader.GetGuid(4), SellerName = reader.GetString(5), ZoneId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
            ZoneName = reader.IsDBNull(7) ? null : reader.GetString(7), Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
            IsActive = reader.GetBoolean(9), CreatedAt = reader.GetDateTimeOffset(10), UpdatedAt = reader.GetDateTimeOffset(11),
            RowVersion = Version(reader, 12), PreparationStatus = reader.GetString(13)
        };
        var schedules = new List<SalesRouteSchedule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            schedules.Add(new(reader.GetGuid(0), reader.GetByte(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(3))));
        var stops = new List<SalesRouteStop>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            stops.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13), reader.IsDBNull(14) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(14)),
                reader.IsDBNull(15) ? null : reader.GetString(15), Version(reader, 16)));
        return new(header.RouteId, header.BusinessId, header.Code, header.Name, header.SellerId, header.SellerName,
            header.ZoneId, header.ZoneName, header.Notes, header.IsActive, header.PreparationStatus,
            schedules, stops, header.CreatedAt, header.UpdatedAt, header.RowVersion);
    }

    public async Task<RouteOptions> OptionsAsync(RouteActorIdentity actor, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT seller.SellerId,seller.Code,party.DisplayName
            FROM dbo.CommerceSellers seller
            INNER JOIN dbo.Businesses business ON business.BusinessId=seller.BusinessId AND business.TenantId=@TenantId
            INNER JOIN dbo.Parties party ON party.PartyId=seller.PartyId
            WHERE seller.BusinessId=@BusinessId AND seller.IsActive=1 ORDER BY party.DisplayName;
            SELECT zone.ZoneId,zone.Code,zone.Name,zone.IsActive,zone.RowVersion
            FROM dbo.SalesZones zone
            INNER JOIN dbo.Businesses business ON business.BusinessId=zone.BusinessId AND business.TenantId=@TenantId
            WHERE zone.BusinessId=@BusinessId ORDER BY zone.IsActive DESC,zone.Name;
            """, connection);
        AddScope(command, actor);
        var sellers = new List<RouteSellerOption>();
        var zones = new List<SalesZoneItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) sellers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct)) zones.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), Version(reader, 4)));
        return new(sellers, zones);
    }

    public async Task<RouteCandidatePage> CandidateSitesAsync(RouteActorIdentity actor, Guid? routeId, RouteCandidateQuery query, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string source = """
            FROM dbo.Customers customer
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId AND party.TenantId=@TenantId
            INNER JOIN dbo.PartySites site ON site.PartyId=party.PartyId AND site.IsActive=1
            INNER JOIN dbo.Cities city ON city.CityId=site.CityId
            WHERE customer.BusinessId=@BusinessId AND customer.IsActive=1 AND party.IsActive=1
              AND (@RouteId IS NULL OR EXISTS(SELECT 1 FROM dbo.SalesRoutes route WHERE route.RouteId=@RouteId AND route.BusinessId=@BusinessId))
              AND (@Search IS NULL OR party.DisplayName LIKE '%'+@Search+'%' OR party.Identification LIKE '%'+@Search+'%'
                   OR site.Name LIKE '%'+@Search+'%' OR site.AddressLine LIKE '%'+@Search+'%' OR site.Phone LIKE '%'+@Search+'%')
              AND (@CountryId IS NULL OR site.CountryId=@CountryId)
              AND (@DivisionId IS NULL OR site.AdministrativeDivisionId=@DivisionId)
              AND (@CityId IS NULL OR site.CityId=@CityId)
              AND (@Neighborhood IS NULL OR site.Neighborhood LIKE '%'+@Neighborhood+'%')
            """;
        int total;
        await using (var count = new SqlCommand("SELECT COUNT(*) " + source, connection))
        {
            AddCandidate(count, actor, routeId, query);
            total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        }
        await using var command = new SqlCommand("""
            SELECT customer.CustomerId,site.PartySiteId,party.DisplayName,party.Identification,site.Name,
              site.AddressLine,site.Neighborhood,city.Name,
              COALESCE(site.Phone,(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact
                                   WHERE contact.PartyId=party.PartyId AND contact.ContactType=N'Phone' AND contact.IsActive=1
                                   ORDER BY contact.IsPrimary DESC,contact.CreatedAt)),
              site.GoogleMapsUrl,site.Latitude,site.Longitude,
              CAST(CASE WHEN @RouteId IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.SalesRouteStops ownStop WHERE ownStop.RouteId=@RouteId AND ownStop.PartySiteId=site.PartySiteId AND ownStop.IsActive=1) THEN 1 ELSE 0 END AS bit),
              (SELECT TOP(1) otherRoute.Name FROM dbo.SalesRouteStops otherStop
               INNER JOIN dbo.SalesRoutes otherRoute ON otherRoute.RouteId=otherStop.RouteId AND otherRoute.BusinessId=@BusinessId AND otherRoute.IsActive=1
               WHERE otherStop.PartySiteId=site.PartySiteId AND otherStop.IsActive=1 AND otherRoute.RouteId<>@RouteId
                 AND EXISTS(SELECT 1 FROM dbo.SalesRouteSchedules candidateDay
                           INNER JOIN dbo.SalesRouteSchedules otherDay ON otherDay.DayOfWeek=candidateDay.DayOfWeek AND otherDay.RouteId=otherRoute.RouteId AND otherDay.IsActive=1
                           WHERE candidateDay.RouteId=@RouteId AND candidateDay.IsActive=1)
               ORDER BY otherRoute.Name)
            """ + Environment.NewLine + source + Environment.NewLine + """
            ORDER BY party.DisplayName,site.Name,site.PartySiteId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        AddCandidate(command, actor, routeId, query);
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", query.PageSize);
        var items = new List<RouteCandidateSite>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var conflict = reader.IsDBNull(13) ? null : reader.GetString(13);
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10), reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.GetBoolean(12), conflict is not null,
                conflict is null ? null : $"Already scheduled in route '{conflict}' on an overlapping day."));
        }
        return new(items, query.Page, query.PageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (decimal)query.PageSize));
    }

    public async Task<SalesZoneItem> CreateZoneAsync(RouteActorIdentity actor, Guid zoneId, string code, string name, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1)
              THROW 51701,'The business is outside the authenticated tenant.',1;
            INSERT dbo.SalesZones(ZoneId,BusinessId,Code,Name,IsActive,CreatedBy,CreatedAt)
            VALUES(@ZoneId,@BusinessId,@Code,@Name,1,@UserId,@Now);
            SELECT ZoneId,Code,Name,IsActive,RowVersion FROM dbo.SalesZones WHERE ZoneId=@ZoneId;
            """, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@ZoneId", zoneId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Now", now);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), Version(reader, 4));
        }
        catch (SqlException exception) { throw Translate(exception, "A zone with this code already exists in the business."); }
    }

    public async Task<RouteMutationResult> CreateAsync(RouteActorIdentity actor, Guid routeId, CreateSalesRouteRequest request,
        string code, string name, string? notes, IReadOnlyList<RouteScheduleInput> schedules, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await ValidateReferencesAndScheduleAsync(connection, transaction, actor, routeId, request.SellerId, request.ZoneId, schedules, ct);
            await using (var command = new SqlCommand("""
                INSERT dbo.SalesRoutes(RouteId,BusinessId,Code,Name,ZoneId,SellerId,Notes,IsActive,CreatedBy,CreatedAt)
                VALUES(@RouteId,@BusinessId,@Code,@Name,@ZoneId,@SellerId,@Notes,1,@UserId,@Now);
                """, connection, transaction))
            {
                AddScope(command, actor); AddRoute(command, routeId, code, name, request.SellerId, request.ZoneId, notes, now);
                await command.ExecuteNonQueryAsync(ct);
            }
            await ReplaceSchedulesAsync(connection, transaction, actor, routeId, schedules, now, ct);
            await transaction.CommitAsync(ct);
            return await MutationAsync(actor, routeId, ct);
        }
        catch (SqlException exception) { await SafeRollbackAsync(transaction, ct); throw Translate(exception, "A route with this code already exists in the business."); }
    }

    public async Task<RouteMutationResult> UpdateAsync(RouteActorIdentity actor, Guid routeId, UpdateSalesRouteRequest request,
        string code, string name, string? notes, IReadOnlyList<RouteScheduleInput> schedules, byte[] rowVersion, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await LockRouteAsync(connection, transaction, actor, routeId, rowVersion, ct);
            await ValidateReferencesAndScheduleAsync(connection, transaction, actor, routeId, request.SellerId, request.ZoneId, schedules, ct);
            await using (var command = new SqlCommand("""
                UPDATE dbo.SalesRoutes SET Code=@Code,Name=@Name,ZoneId=@ZoneId,SellerId=@SellerId,Notes=@Notes,
                  UpdatedBy=@UserId,UpdatedAt=@Now
                WHERE RouteId=@RouteId AND BusinessId=@BusinessId;
                """, connection, transaction))
            {
                AddScope(command, actor); AddRoute(command, routeId, code, name, request.SellerId, request.ZoneId, notes, now);
                await command.ExecuteNonQueryAsync(ct);
            }
            await ReplaceSchedulesAsync(connection, transaction, actor, routeId, schedules, now, ct);
            await ValidateExistingStopConflictsAsync(connection, transaction, actor, routeId, ct);
            await transaction.CommitAsync(ct);
            return await MutationAsync(actor, routeId, ct);
        }
        catch (SqlException exception) { await SafeRollbackAsync(transaction, ct); throw Translate(exception, "The route changed or conflicts with another active route."); }
    }

    public Task<RouteMutationResult> SetStatusAsync(RouteActorIdentity actor, Guid routeId, bool isActive, byte[] rowVersion, DateTimeOffset now, CancellationToken ct) =>
        MutateRouteAsync(actor, routeId, rowVersion, now, ct, async (connection, transaction) =>
        {
            if (isActive) await ValidateExistingStopConflictsAsync(connection, transaction, actor, routeId, ct);
            await using var command = new SqlCommand("UPDATE dbo.SalesRoutes SET IsActive=@State,UpdatedBy=@UserId,UpdatedAt=@Now WHERE RouteId=@RouteId AND BusinessId=@BusinessId;", connection, transaction);
            AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.AddWithValue("@State", isActive); command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(ct);
        });

    public Task<RouteMutationResult> AddStopsAsync(RouteActorIdentity actor, Guid routeId,
        IReadOnlyCollection<(Guid StopId, AddRouteStopItem Stop)> stops, byte[] rowVersion, DateTimeOffset now, CancellationToken ct) =>
        MutateRouteAsync(actor, routeId, rowVersion, now, ct, async (connection, transaction) =>
        {
            var sequence = await ScalarIntAsync(connection, transaction, "SELECT COALESCE(MAX(Sequence),0) FROM dbo.SalesRouteStops WITH(UPDLOCK,HOLDLOCK) WHERE RouteId=@RouteId AND IsActive=1;", actor, routeId, ct);
            foreach (var value in stops)
            {
                await ValidateStopAsync(connection, transaction, actor, routeId, value.Stop.CustomerId, value.Stop.PartySiteId, ct);
                await using var command = new SqlCommand("""
                    INSERT dbo.SalesRouteStops(RouteStopId,RouteId,CustomerId,PartySiteId,Sequence,PlannedVisitTime,VisitNote,IsActive,CreatedBy,CreatedAt)
                    VALUES(@StopId,@RouteId,@CustomerId,@PartySiteId,@Sequence,@PlannedVisitTime,@VisitNote,1,@UserId,@Now);
                    """, connection, transaction);
                AddScope(command, actor); command.Parameters.AddWithValue("@StopId", value.StopId); command.Parameters.AddWithValue("@RouteId", routeId);
                command.Parameters.AddWithValue("@CustomerId", value.Stop.CustomerId); command.Parameters.AddWithValue("@PartySiteId", value.Stop.PartySiteId);
                command.Parameters.AddWithValue("@Sequence", ++sequence); command.Parameters.AddWithValue("@PlannedVisitTime", value.Stop.PlannedVisitTime?.ToTimeSpan() ?? (object)DBNull.Value); command.Parameters.AddWithValue("@VisitNote", (object?)value.Stop.VisitNote ?? DBNull.Value); command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(ct);
            }
            await TouchAsync(connection, transaction, actor, routeId, now, ct);
        });

    public Task<RouteMutationResult> UpdateStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, UpdateRouteStopRequest request, string? visitNote, byte[] rowVersion, DateTimeOffset now, CancellationToken ct) =>
        MutateRouteAsync(actor, routeId, rowVersion, now, ct, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                UPDATE dbo.SalesRouteStops SET PlannedVisitTime=@PlannedVisitTime,VisitNote=@VisitNote,UpdatedBy=@UserId,UpdatedAt=@Now
                WHERE RouteStopId=@StopId AND RouteId=@RouteId AND IsActive=1;
                IF @@ROWCOUNT=0 THROW 51704,'The route stop does not exist.',1;
                """, connection, transaction);
            AddScope(command,actor);command.Parameters.AddWithValue("@RouteId",routeId);command.Parameters.AddWithValue("@StopId",stopId);
            command.Parameters.AddWithValue("@PlannedVisitTime",request.PlannedVisitTime?.ToTimeSpan()??(object)DBNull.Value);
            command.Parameters.AddWithValue("@VisitNote",(object?)visitNote??DBNull.Value);command.Parameters.AddWithValue("@Now",now);
            await command.ExecuteNonQueryAsync(ct);await TouchAsync(connection,transaction,actor,routeId,now,ct);
        });

    public Task<RouteMutationResult> RemoveStopAsync(RouteActorIdentity actor, Guid routeId, Guid stopId, byte[] rowVersion, DateTimeOffset now, CancellationToken ct) =>
        MutateRouteAsync(actor, routeId, rowVersion, now, ct, async (connection, transaction) =>
        {
            await using var command = new SqlCommand("""
                UPDATE dbo.SalesRouteStops SET IsActive=0,RemovedBy=@UserId,RemovedAt=@Now,UpdatedBy=@UserId,UpdatedAt=@Now
                WHERE RouteStopId=@StopId AND RouteId=@RouteId AND IsActive=1;
                IF @@ROWCOUNT=0 THROW 51704,'The route stop does not exist.',1;
                ;WITH ordered AS(SELECT RouteStopId,ROW_NUMBER() OVER(ORDER BY Sequence,RouteStopId) NewSequence FROM dbo.SalesRouteStops WHERE RouteId=@RouteId AND IsActive=1)
                UPDATE stop SET Sequence=ordered.NewSequence FROM dbo.SalesRouteStops stop INNER JOIN ordered ON ordered.RouteStopId=stop.RouteStopId;
                """, connection, transaction);
            AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.AddWithValue("@StopId", stopId); command.Parameters.AddWithValue("@Now", now);
            await command.ExecuteNonQueryAsync(ct); await TouchAsync(connection, transaction, actor, routeId, now, ct);
        });

    public Task<RouteMutationResult> ReorderStopsAsync(RouteActorIdentity actor, Guid routeId, IReadOnlyCollection<Guid> orderedStopIds, byte[] rowVersion, DateTimeOffset now, CancellationToken ct) =>
        MutateRouteAsync(actor, routeId, rowVersion, now, ct, async (connection, transaction) =>
        {
            var current = new List<Guid>();
            await using (var read = new SqlCommand("SELECT RouteStopId FROM dbo.SalesRouteStops WITH(UPDLOCK,HOLDLOCK) WHERE RouteId=@RouteId AND IsActive=1 ORDER BY Sequence;", connection, transaction))
            {
                read.Parameters.AddWithValue("@RouteId", routeId); await using var reader = await read.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) current.Add(reader.GetGuid(0));
            }
            IReadOnlyList<Guid> order;
            try { order = RouteRules.CompleteOrder(current, orderedStopIds); }
            catch (ArgumentException exception) { throw new RouteConflictException(exception.Message); }
            await using (var temporary = new SqlCommand("UPDATE dbo.SalesRouteStops SET Sequence=Sequence+1000000 WHERE RouteId=@RouteId AND IsActive=1;", connection, transaction))
            { temporary.Parameters.AddWithValue("@RouteId", routeId); await temporary.ExecuteNonQueryAsync(ct); }
            var sequence = 0;
            foreach (var stopId in order)
            {
                await using var update = new SqlCommand("UPDATE dbo.SalesRouteStops SET Sequence=@Sequence,UpdatedBy=@UserId,UpdatedAt=@Now WHERE RouteStopId=@StopId AND RouteId=@RouteId AND IsActive=1;", connection, transaction);
                update.Parameters.AddWithValue("@Sequence", ++sequence); update.Parameters.AddWithValue("@UserId", actor.UserId); update.Parameters.AddWithValue("@Now", now); update.Parameters.AddWithValue("@StopId", stopId); update.Parameters.AddWithValue("@RouteId", routeId);
                await update.ExecuteNonQueryAsync(ct);
            }
            await TouchAsync(connection, transaction, actor, routeId, now, ct);
        });

    private async Task<RouteMutationResult> MutateRouteAsync(RouteActorIdentity actor, Guid routeId, byte[] rowVersion, DateTimeOffset now, CancellationToken ct, Func<SqlConnection,SqlTransaction,Task> mutation)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await LockRouteAsync(connection, transaction, actor, routeId, rowVersion, ct);
            await mutation(connection, transaction); await transaction.CommitAsync(ct);
            return await MutationAsync(actor, routeId, ct);
        }
        catch (RouteConflictException) { await SafeRollbackAsync(transaction, ct); throw; }
        catch (SqlException exception) { await SafeRollbackAsync(transaction, ct); throw Translate(exception, "The route operation conflicts with its current state."); }
    }

    public async Task<IReadOnlyCollection<SalesRouteVisit>> VisitsAsync(RouteActorIdentity actor, Guid routeId, DateOnly date, CancellationToken ct)
    {
        await EnsureRouteAccessAsync(actor, routeId, ct);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT RouteVisitId,RouteStopId,VisitDate,Status,SkipReason,VisitObservation,OrderId,OccurredAt,RecordedBy
            FROM dbo.SalesRouteVisits
            WHERE BusinessId=@BusinessId AND RouteId=@RouteId AND VisitDate=@VisitDate
            ORDER BY OccurredAt,RouteVisitId;
            """, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RouteId", routeId);
        command.Parameters.Add("@VisitDate", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        var values = new List<SalesRouteVisit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            values.Add(ReadVisit(reader));
        return values;
    }

    public async Task<SalesRouteVisit> RecordVisitAsync(RouteActorIdentity actor, Guid routeId, RecordSalesRouteVisitRequest request, string? reason, string? observation, CancellationToken ct)
    {
        await EnsureRouteAccessAsync(actor, routeId, ct);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var existing = new SqlCommand("""
                SELECT RouteVisitId,RouteStopId,VisitDate,Status,SkipReason,OrderId,OccurredAt,RecordedBy
                FROM dbo.SalesRouteVisits WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND IdempotencyKey=@IdempotencyKey;
                """, connection, transaction))
            {
                AddScope(existing, actor);
                existing.Parameters.AddWithValue("@IdempotencyKey", request.IdempotencyKey);
                await using var reader = await existing.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var value = ReadVisit(reader);
                    if (value.RouteStopId != request.RouteStopId || value.VisitDate != request.VisitDate || value.Status != request.Status || value.OrderId != request.OrderId)
                        throw new RouteConflictException("The idempotency key was already used for a different route visit.");
                    await reader.DisposeAsync();
                    await transaction.CommitAsync(ct);
                    return value;
                }
            }

            var visitId = ids.NewId();
            var createdAt = DateTimeOffset.UtcNow;
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.SalesRouteStops WHERE RouteStopId=@RouteStopId AND RouteId=@RouteId AND IsActive=1)
                  THROW 51702,'The route stop is not active in this route.',1;
                IF @OrderId IS NOT NULL AND NOT EXISTS(
                    SELECT 1 FROM dbo.Orders orders
                    INNER JOIN dbo.SalesRouteStops stop ON stop.RouteStopId=@RouteStopId AND stop.CustomerId=orders.CustomerId
                    WHERE orders.OrderId=@OrderId AND orders.BusinessId=@BusinessId)
                  THROW 51702,'The order does not belong to this route customer.',1;
                INSERT dbo.SalesRouteVisits(RouteVisitId,BusinessId,RouteId,RouteStopId,VisitDate,Status,SkipReason,VisitObservation,OrderId,OccurredAt,RecordedBy,IdempotencyKey,CreatedAt)
                VALUES(@RouteVisitId,@BusinessId,@RouteId,@RouteStopId,@VisitDate,@Status,@SkipReason,@VisitObservation,@OrderId,@OccurredAt,@UserId,@IdempotencyKey,@CreatedAt);
                """, connection, transaction);
            AddScope(command, actor);
            command.Parameters.AddWithValue("@RouteVisitId", visitId);
            command.Parameters.AddWithValue("@RouteId", routeId);
            command.Parameters.AddWithValue("@RouteStopId", request.RouteStopId);
            command.Parameters.Add("@VisitDate", SqlDbType.Date).Value = request.VisitDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.AddWithValue("@Status", request.Status);
            command.Parameters.AddWithValue("@SkipReason", (object?)reason ?? DBNull.Value);
            command.Parameters.AddWithValue("@VisitObservation", (object?)observation ?? DBNull.Value);
            command.Parameters.AddWithValue("@OrderId", (object?)request.OrderId ?? DBNull.Value);
            command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
            command.Parameters.AddWithValue("@IdempotencyKey", request.IdempotencyKey);
            command.Parameters.AddWithValue("@CreatedAt", createdAt);
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return new(visitId, request.RouteStopId, request.VisitDate, request.Status, reason, request.OrderId, request.OccurredAt, actor.UserId, observation);
        }
        catch (RouteConflictException) { await SafeRollbackAsync(transaction, ct); throw; }
        catch (SqlException exception) { await SafeRollbackAsync(transaction, ct); throw Translate(exception, "This customer already has a route result for the selected date."); }
    }

    private async Task EnsureRouteAccessAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        var route = await GetAsync(actor, routeId, ct);
        if (route is null) throw new RouteNotFoundException("The route does not exist in the authenticated business.");
    }

    private async Task<bool> UserOwnsSellerAsync(RouteActorIdentity actor, Guid sellerId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.AppUsers currentUser
            INNER JOIN dbo.CommerceSellers seller ON seller.PartyId=currentUser.PartyId AND seller.BusinessId=@BusinessId AND seller.IsActive=1
            WHERE currentUser.UserId=@UserId AND currentUser.TenantId=@TenantId AND seller.SellerId=@SellerId;
            """, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@SellerId", sellerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    private static SalesRouteVisit ReadVisit(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(6) ? null : reader.GetGuid(6),
        reader.GetDateTimeOffset(7), reader.GetGuid(8), reader.IsDBNull(5) ? null : reader.GetString(5));

    private static async Task LockRouteAsync(SqlConnection connection, SqlTransaction transaction, RouteActorIdentity actor, Guid routeId, byte[] rowVersion, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.SalesRoutes route WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.Businesses business ON business.BusinessId=route.BusinessId AND business.TenantId=@TenantId WHERE route.RouteId=@RouteId AND route.BusinessId=@BusinessId)
              THROW 51704,'The route does not exist.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.SalesRoutes WHERE RouteId=@RouteId AND BusinessId=@BusinessId AND RowVersion=@RowVersion)
              THROW 51703,'The route was modified by another user.',1;
            """, connection, transaction);
        AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value=rowVersion;
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task ValidateReferencesAndScheduleAsync(SqlConnection connection, SqlTransaction transaction, RouteActorIdentity actor, Guid routeId, Guid sellerId, Guid? zoneId, IReadOnlyCollection<RouteScheduleInput> schedules, CancellationToken ct)
    {
        foreach (var schedule in schedules)
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.CommerceSellers WHERE SellerId=@SellerId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51702,'The seller is not active in this business.',1;
                IF @ZoneId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.SalesZones WHERE ZoneId=@ZoneId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51702,'The sales zone is not active in this business.',1;
                IF EXISTS(SELECT 1 FROM dbo.SalesRoutes otherRoute WITH(UPDLOCK,HOLDLOCK)
                          INNER JOIN dbo.SalesRouteSchedules otherSchedule ON otherSchedule.RouteId=otherRoute.RouteId AND otherSchedule.IsActive=1
                          WHERE otherRoute.BusinessId=@BusinessId AND otherRoute.SellerId=@SellerId AND otherRoute.IsActive=1
                            AND otherRoute.RouteId<>@RouteId AND otherSchedule.DayOfWeek=@DayOfWeek AND otherSchedule.RunOrder=@RunOrder)
                  THROW 51705,'The seller already has another route with the same day and run order.',1;
                """, connection, transaction);
            AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.AddWithValue("@SellerId", sellerId);
            command.Parameters.AddWithValue("@ZoneId", (object?)zoneId ?? DBNull.Value); command.Parameters.AddWithValue("@DayOfWeek", schedule.DayOfWeek); command.Parameters.AddWithValue("@RunOrder", schedule.RunOrder);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task ValidateStopAsync(SqlConnection connection, SqlTransaction transaction, RouteActorIdentity actor, Guid routeId, Guid customerId, Guid partySiteId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Customers customer INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId AND party.TenantId=@TenantId AND party.IsActive=1 INNER JOIN dbo.PartySites site ON site.PartyId=party.PartyId AND site.PartySiteId=@PartySiteId AND site.IsActive=1 WHERE customer.CustomerId=@CustomerId AND customer.BusinessId=@BusinessId AND customer.IsActive=1)
              THROW 51702,'The customer site is not active in this business.',1;
            IF EXISTS(SELECT 1 FROM dbo.SalesRouteStops WHERE RouteId=@RouteId AND PartySiteId=@PartySiteId AND IsActive=1)
              THROW 51706,'The customer site is already part of this route.',1;
            IF EXISTS(SELECT 1 FROM dbo.SalesRouteStops otherStop WITH(UPDLOCK,HOLDLOCK)
                      INNER JOIN dbo.SalesRoutes otherRoute ON otherRoute.RouteId=otherStop.RouteId AND otherRoute.BusinessId=@BusinessId AND otherRoute.IsActive=1
                      WHERE otherStop.PartySiteId=@PartySiteId AND otherStop.IsActive=1 AND otherRoute.RouteId<>@RouteId
                        AND EXISTS(SELECT 1 FROM dbo.SalesRouteSchedules candidateDay INNER JOIN dbo.SalesRouteSchedules otherDay ON otherDay.DayOfWeek=candidateDay.DayOfWeek AND otherDay.RouteId=otherRoute.RouteId AND otherDay.IsActive=1 WHERE candidateDay.RouteId=@RouteId AND candidateDay.IsActive=1))
              THROW 51706,'The customer site already belongs to an active route on an overlapping day.',1;
            """, connection, transaction);
        AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.AddWithValue("@CustomerId", customerId); command.Parameters.AddWithValue("@PartySiteId", partySiteId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ValidateExistingStopConflictsAsync(SqlConnection connection, SqlTransaction transaction, RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.SalesRouteStops candidateStop
                      INNER JOIN dbo.SalesRouteStops otherStop ON otherStop.PartySiteId=candidateStop.PartySiteId AND otherStop.IsActive=1 AND otherStop.RouteId<>@RouteId
                      INNER JOIN dbo.SalesRoutes otherRoute ON otherRoute.RouteId=otherStop.RouteId AND otherRoute.BusinessId=@BusinessId AND otherRoute.IsActive=1
                      WHERE candidateStop.RouteId=@RouteId AND candidateStop.IsActive=1
                        AND EXISTS(SELECT 1 FROM dbo.SalesRouteSchedules candidateDay INNER JOIN dbo.SalesRouteSchedules otherDay ON otherDay.DayOfWeek=candidateDay.DayOfWeek AND otherDay.RouteId=otherRoute.RouteId AND otherDay.IsActive=1 WHERE candidateDay.RouteId=@RouteId AND candidateDay.IsActive=1))
              THROW 51706,'The new calendar overlaps another active route containing the same customer site.',1;
            """, connection, transaction);
        AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); await command.ExecuteNonQueryAsync(ct);
    }

    private async Task ReplaceSchedulesAsync(SqlConnection connection, SqlTransaction transaction, RouteActorIdentity actor, Guid routeId, IReadOnlyCollection<RouteScheduleInput> schedules, DateTimeOffset now, CancellationToken ct)
    {
        await using (var deactivate = new SqlCommand("UPDATE dbo.SalesRouteSchedules SET IsActive=0,UpdatedBy=@UserId,UpdatedAt=@Now WHERE RouteId=@RouteId AND IsActive=1;", connection, transaction))
        { deactivate.Parameters.AddWithValue("@UserId", actor.UserId); deactivate.Parameters.AddWithValue("@Now", now); deactivate.Parameters.AddWithValue("@RouteId", routeId); await deactivate.ExecuteNonQueryAsync(ct); }
        foreach (var schedule in schedules)
        {
            await using var insert = new SqlCommand("INSERT dbo.SalesRouteSchedules(RouteScheduleId,RouteId,DayOfWeek,RunOrder,PlannedStartTime,IsActive,CreatedBy,CreatedAt) VALUES(@Id,@RouteId,@Day,@Order,@Time,1,@UserId,@Now);", connection, transaction);
            insert.Parameters.AddWithValue("@Id", ids.NewId()); insert.Parameters.AddWithValue("@RouteId", routeId); insert.Parameters.AddWithValue("@Day", schedule.DayOfWeek); insert.Parameters.AddWithValue("@Order", schedule.RunOrder); insert.Parameters.AddWithValue("@Time", schedule.PlannedStartTime?.ToTimeSpan() ?? (object)DBNull.Value); insert.Parameters.AddWithValue("@UserId", actor.UserId); insert.Parameters.AddWithValue("@Now", now);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<RouteMutationResult> MutationAsync(RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT route.RouteId,route.RowVersion,route.IsActive,
              CASE WHEN EXISTS(SELECT 1 FROM dbo.SalesRouteSchedules x WHERE x.RouteId=route.RouteId AND x.IsActive=1)
                     AND EXISTS(SELECT 1 FROM dbo.SalesRouteStops x WHERE x.RouteId=route.RouteId AND x.IsActive=1)
                   THEN N'Ready' ELSE N'Draft' END,
              (SELECT COUNT(1) FROM dbo.SalesRouteStops x WHERE x.RouteId=route.RouteId AND x.IsActive=1)
            FROM dbo.SalesRoutes route
            INNER JOIN dbo.Businesses business ON business.BusinessId=route.BusinessId AND business.TenantId=@TenantId
            WHERE route.RouteId=@RouteId AND route.BusinessId=@BusinessId;
            """, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@RouteId", routeId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new RouteNotFoundException("The route does not exist.");
        return new(reader.GetGuid(0), Version(reader, 1), reader.GetBoolean(2), reader.GetString(3), reader.GetInt32(4));
    }

    private static async Task TouchAsync(SqlConnection connection, SqlTransaction transaction, RouteActorIdentity actor, Guid routeId, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("UPDATE dbo.SalesRoutes SET UpdatedBy=@UserId,UpdatedAt=@Now WHERE RouteId=@RouteId AND BusinessId=@BusinessId;", connection, transaction);
        AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.AddWithValue("@Now", now); await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection, SqlTransaction transaction, string sql, RouteActorIdentity actor, Guid routeId, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection, transaction); AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", routeId); return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static void AddRoute(SqlCommand command, Guid routeId, string code, string name, Guid sellerId, Guid? zoneId, string? notes, DateTimeOffset now)
    { command.Parameters.AddWithValue("@RouteId", routeId); command.Parameters.AddWithValue("@Code", code); command.Parameters.AddWithValue("@Name", name); command.Parameters.AddWithValue("@SellerId", sellerId); command.Parameters.AddWithValue("@ZoneId", (object?)zoneId ?? DBNull.Value); command.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value); command.Parameters.AddWithValue("@Now", now); }
    private static void AddScope(SqlCommand command, RouteActorIdentity actor)
    {
        command.Parameters.AddWithValue("@TenantId", actor.TenantId);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.AddWithValue("@ReadAll", actor.Permissions.Contains(RoutePermissionCodes.ReadAll));
    }
    private static void AddQuery(SqlCommand command, RouteActorIdentity actor, SalesRouteQuery query)
    { AddScope(command, actor); command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value); command.Parameters.AddWithValue("@SellerId", (object?)query.SellerId ?? DBNull.Value); command.Parameters.AddWithValue("@ZoneId", (object?)query.ZoneId ?? DBNull.Value); command.Parameters.AddWithValue("@DayOfWeek", (object?)query.DayOfWeek ?? DBNull.Value); command.Parameters.AddWithValue("@IsActive", (object?)query.IsActive ?? DBNull.Value); command.Parameters.AddWithValue("@PreparationStatus", (object?)query.PreparationStatus ?? DBNull.Value); }
    private static void AddCandidate(SqlCommand command, RouteActorIdentity actor, Guid? routeId, RouteCandidateQuery query)
    { AddScope(command, actor); command.Parameters.AddWithValue("@RouteId", (object?)routeId ?? DBNull.Value); command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value); command.Parameters.AddWithValue("@CountryId", (object?)query.CountryId ?? DBNull.Value); command.Parameters.AddWithValue("@DivisionId", (object?)query.AdministrativeDivisionId ?? DBNull.Value); command.Parameters.AddWithValue("@CityId", (object?)query.CityId ?? DBNull.Value); command.Parameters.AddWithValue("@Neighborhood", (object?)query.Neighborhood ?? DBNull.Value); }
    private static IReadOnlyCollection<int> ParseDays(string? value) => string.IsNullOrWhiteSpace(value) ? Array.Empty<int>() : value.Split(',').Select(int.Parse).ToArray();
    private static string Version(SqlDataReader reader, int ordinal) => Convert.ToBase64String(reader.GetFieldValue<byte[]>(ordinal));
    private static async Task SafeRollbackAsync(SqlTransaction transaction, CancellationToken ct) { try { await transaction.RollbackAsync(ct); } catch (InvalidOperationException) { } }
    private static Exception Translate(SqlException exception, string fallback) => exception.Number switch
    { 51701 => new RouteForbiddenException(exception.Message), 51702 => new RouteValidationException(exception.Message), 51703 or 51705 or 51706 or 2601 or 2627 => new RouteConflictException(exception.Number is 2601 or 2627 ? fallback : exception.Message), 51704 => new RouteNotFoundException(exception.Message), _ => exception };
}
