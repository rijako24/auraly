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
    public async Task EnsureAsync(SqlConnection connection,SqlTransaction transaction,Guid tenantId,Guid businessId,Guid orderId,CancellationToken token)
    {
        await using var sourceCommand=new SqlCommand("""
          SELECT b.TenantId,o.BusinessId,o.OrderId,CAST(o.CreatedAt AS date),o.CreatedAt,COALESCE(o.ExternalDocumentNumber,CONVERT(nvarchar(36),o.OrderId)),
            s.SellerId,COALESCE(NULLIF(sp.DisplayName,N''),NULLIF(sp.LegalName,N''),s.Code),o.CustomerId,
            COALESCE(NULLIF(cp.DisplayName,N''),NULLIF(cp.LegalName,N''),o.CustomerNameSnapshot),o.RouteId,o.Total,o.Status,o.RequiresStockReview
          FROM dbo.Orders o INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId AND b.TenantId=@TenantId
          INNER JOIN dbo.CommerceSellers s ON s.SellerId=o.SellerId INNER JOIN dbo.Parties sp ON sp.PartyId=s.PartyId
          INNER JOIN dbo.Customers customer ON customer.CustomerId=o.CustomerId INNER JOIN dbo.Parties cp ON cp.PartyId=customer.PartyId
          WHERE o.OrderId=@OrderId AND o.BusinessId=@BusinessId;
          """,connection,transaction);
        sourceCommand.Parameters.AddWithValue("@TenantId",tenantId);sourceCommand.Parameters.AddWithValue("@OrderId",orderId);sourceCommand.Parameters.AddWithValue("@BusinessId",businessId);
        CommercialOrderProjectionSource source;
        await using(var reader=await sourceCommand.ExecuteReaderAsync(token))
        {if(!await reader.ReadAsync(token))throw new InvalidOperationException("The order reporting source could not be captured.");
         var created=DateTime.SpecifyKind(reader.GetDateTime(4),DateTimeKind.Utc);
         source=new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),DateOnly.FromDateTime(reader.GetDateTime(3)),new DateTimeOffset(created),reader.GetString(5),reader.GetGuid(6),reader.GetString(7),reader.GetGuid(8),reader.GetString(9),reader.IsDBNull(10)?null:reader.GetGuid(10),reader.GetDecimal(11),reader.GetInt32(12),reader.GetBoolean(13));}
        var payload=JsonSerializer.Serialize(source,new JsonSerializerOptions(JsonSerializerDefaults.Web));var hash=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        await using var job=new SqlCommand("""
          IF NOT EXISTS(SELECT 1 FROM reporting.SalesReportingJobs WITH(UPDLOCK,HOLDLOCK) WHERE SourceDocumentId=@OrderId AND SourceDocumentType=N'SellerOrder' AND SourceVersion=1)
          INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
          VALUES(@JobId,@BusinessId,@OrderId,N'SellerOrder',1,@Hash,@Payload,N'Pending',0,@CreatedAt);
          """,connection,transaction);
        job.Parameters.AddWithValue("@JobId",ids.NewId());job.Parameters.AddWithValue("@BusinessId",businessId);job.Parameters.AddWithValue("@OrderId",orderId);
        job.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;job.Parameters.AddWithValue("@Payload",payload);job.Parameters.AddWithValue("@CreatedAt",timeProvider.GetUtcNow());
        await job.ExecuteNonQueryAsync(token);
    }
}
