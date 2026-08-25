using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSellerOrderReportingJobWriter(IAuralyIdGenerator ids,TimeProvider timeProvider)
{
    public async Task<long> EnsureAsync(SqlConnection connection,SqlTransaction transaction,Guid tenantId,Guid businessId,Guid orderId,CancellationToken token)
    {
        await using var sourceCommand=new SqlCommand("""
          SELECT b.TenantId,o.BusinessId,o.OrderId,CAST(o.CreatedAt AS date),o.CreatedAt,COALESCE(o.ExternalDocumentNumber,CONVERT(nvarchar(36),o.OrderId)),
            s.SellerId,COALESCE(NULLIF(sp.DisplayName,N''),NULLIF(sp.LegalName,N''),s.Code),o.CustomerId,
            COALESCE(NULLIF(cp.DisplayName,N''),NULLIF(cp.LegalName,N''),o.CustomerNameSnapshot),o.RouteId,o.Total,o.Status,o.RequiresStockReview,
            o.PartySiteId,o.RouteStopId,route.ZoneId,route.Name,zone.Name,o.CapturedOffline,o.Source,o.UpdatedAt
          FROM dbo.Orders o INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId AND b.TenantId=@TenantId
          INNER JOIN dbo.CommerceSellers s ON s.SellerId=o.SellerId INNER JOIN dbo.Parties sp ON sp.PartyId=s.PartyId
          INNER JOIN dbo.Customers customer ON customer.CustomerId=o.CustomerId INNER JOIN dbo.Parties cp ON cp.PartyId=customer.PartyId
          LEFT JOIN dbo.SalesRoutes route ON route.RouteId=o.RouteId LEFT JOIN dbo.SalesZones zone ON zone.ZoneId=route.ZoneId
          WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId;
          """,connection,transaction);
        sourceCommand.Parameters.AddWithValue("@TenantId",tenantId);sourceCommand.Parameters.AddWithValue("@OrderId",orderId);sourceCommand.Parameters.AddWithValue("@BusinessId",businessId);
        CommercialOrderProjectionSource source;
        await using(var reader=await sourceCommand.ExecuteReaderAsync(token))
        {if(!await reader.ReadAsync(token))throw new InvalidOperationException("The order reporting source could not be captured.");
         var created=DateTime.SpecifyKind(reader.GetDateTime(4),DateTimeKind.Utc);
         var status=reader.GetInt32(12);DateTimeOffset? changed=reader.IsDBNull(21)?null:new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(21),DateTimeKind.Utc));
         source=new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),DateOnly.FromDateTime(reader.GetDateTime(3)),new DateTimeOffset(created),reader.GetString(5),reader.GetGuid(6),reader.GetString(7),reader.GetGuid(8),reader.GetString(9),reader.IsDBNull(10)?null:reader.GetGuid(10),reader.GetDecimal(11),status,reader.GetBoolean(13),
            reader.IsDBNull(14)?null:reader.GetGuid(14),reader.IsDBNull(15)?null:reader.GetGuid(15),reader.IsDBNull(16)?null:reader.GetGuid(16),reader.IsDBNull(17)?null:reader.GetString(17),reader.IsDBNull(18)?null:reader.GetString(18),
            reader.GetInt32(20)==0?"Conversational":reader.GetInt32(20)==1?"PointOfSale":"SellerOrder",reader.GetBoolean(19),status is 2 or 3 or 4?changed:null,status==91?changed:null);}
        var payload=JsonSerializer.Serialize(source,new JsonSerializerOptions(JsonSerializerDefaults.Web));var hash=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        await using var job=new SqlCommand("""
          DECLARE @PriorVersion bigint,@PriorHash binary(32);
          SELECT TOP(1) @PriorVersion=SourceVersion,@PriorHash=SourcePayloadHash FROM reporting.SalesReportingJobs WITH(UPDLOCK,HOLDLOCK)
          WHERE SourceDocumentId=@OrderId AND SourceDocumentType=N'SellerOrder' ORDER BY SourceVersion DESC;
          IF @PriorHash=@Hash BEGIN SELECT @PriorVersion; RETURN; END;
          DECLARE @Version bigint=COALESCE(@PriorVersion,0)+1;
          INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
          VALUES(@JobId,@BusinessId,@OrderId,N'SellerOrder',@Version,@Hash,@Payload,N'Pending',0,@CreatedAt);
          SELECT @Version;
          """,connection,transaction);
        job.Parameters.AddWithValue("@JobId",ids.NewId());job.Parameters.AddWithValue("@BusinessId",businessId);job.Parameters.AddWithValue("@OrderId",orderId);
        job.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;job.Parameters.AddWithValue("@Payload",payload);job.Parameters.AddWithValue("@CreatedAt",timeProvider.GetUtcNow());
        return Convert.ToInt64(await job.ExecuteScalarAsync(token));
    }
}
