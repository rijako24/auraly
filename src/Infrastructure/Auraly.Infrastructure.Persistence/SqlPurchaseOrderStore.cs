using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPurchaseOrderStore(SqlServerConnectionFactory connections, TimeProvider timeProvider) : IPurchaseOrderStore
{
    public async Task<PurchaseOrderPage> ListAsync(PurchasingUserIdentity user, string? search, string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var command=Procedure("purchasing.PurchaseOrdersList",connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId); command.Parameters.AddWithValue("@Search",(object?)search??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)status??DBNull.Value); command.Parameters.AddWithValue("@Offset",(page-1)*pageSize); command.Parameters.AddWithValue("@PageSize",pageSize);
        await using var reader=await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct); var total=reader.GetInt32(0); await reader.NextResultAsync(ct);
        var items=new List<PurchaseOrderListItem>();
        while(await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetFieldValue<DateTimeOffset>(5),reader.IsDBNull(6)?null:reader.GetFieldValue<DateTimeOffset>(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetFieldValue<DateTimeOffset>(9)));
        return new(items,page,pageSize,total,(int)Math.Ceiling(total/(double)pageSize));
    }

    public async Task<PurchaseOrderDetail?> GetAsync(PurchasingUserIdentity user,Guid id,CancellationToken ct)
    { await using var connection=connections.Create();await connection.OpenAsync(ct);return await ReadAsync(connection,user.BusinessId,id,false,ct); }

    public async Task<PurchaseOrderReceiptSource?> GetReceiptSourceAsync(PurchasingUserIdentity user,Guid id,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);var detail=await ReadAsync(connection,user.BusinessId,id,true,ct);
        if(detail is null||detail.DocumentNumber is null||detail.WarehouseId is null||detail.SupplierId is null)return null;
        return new(detail.PurchaseOrderId,detail.DocumentNumber,detail.Status,detail.WarehouseId.Value,detail.SupplierId.Value,detail.OrderedAt,detail.ExpectedAt,detail.CurrencyCode,detail.Notes,detail.Lines.Where(x=>x.RemainingQuantity>0).ToArray());
    }

    public async Task<PurchaseOrderDetail> SaveDraftAsync(PurchasingUserIdentity user,SavePurchaseOrderDraftRequest request,PurchaseOrderCalculation? calculation,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using var command=Procedure("purchasing.PurchaseOrderDraftSave",connection,transaction);
            AddCommon(command,user,request.PurchaseOrderId,request.WarehouseId,request.SupplierId,request.OrderedAt,request.ExpectedAt,request.CurrencyCode,request.Notes,calculation);
            command.Parameters.Add("@ExpectedRowVersion",SqlDbType.VarBinary,8).Value=(object?)DecodeToken(request.ConcurrencyToken)??DBNull.Value;
            command.Parameters.AddWithValue("@LinesJson",BuildLinesJson(request.Lines,calculation));command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
            await command.ExecuteNonQueryAsync(ct);await transaction.CommitAsync(ct);
        }
        catch(SqlException exception) when(exception.Number is>=51200 and<=51204)
        {await transaction.RollbackAsync(CancellationToken.None);if(exception.Number is 51203 or 51204)throw new PurchasingConflictException(exception.Message);throw new PurchasingValidationException(exception.Message);}
        catch{await transaction.RollbackAsync(CancellationToken.None);throw;}
        return(await GetAsync(user,request.PurchaseOrderId,ct))!;
    }

    public async Task DeleteDraftAsync(PurchasingUserIdentity user,Guid id,string concurrencyToken,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=Procedure("purchasing.PurchaseOrderDraftDelete",connection);
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@PurchaseOrderId",id);
        command.Parameters.Add("@RowVersion",SqlDbType.VarBinary,8).Value=DecodeToken(concurrencyToken)!;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch(SqlException exception) when(exception.Number==51204)
        { throw new PurchasingConflictException(exception.Message); }
    }

    public async Task<PurchaseOrderConfirmation> ConfirmAsync(PurchasingUserIdentity user,string idempotencyKey,ConfirmPurchaseOrderRequest request,PurchaseOrderCalculation calculation,CancellationToken ct)
    {
        var hash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{request,calculation}));
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using var command=Procedure("purchasing.PurchaseOrderConfirm",connection,transaction);
            AddCommon(command,user,request.PurchaseOrderId,request.WarehouseId,request.SupplierId,request.OrderedAt,request.ExpectedAt,request.CurrencyCode,request.Notes,calculation);
            command.Parameters.AddWithValue("@IdempotencyKey",idempotencyKey);command.Parameters.Add("@PayloadHash",SqlDbType.Binary,32).Value=hash;
            command.Parameters.Add("@DraftRowVersion",SqlDbType.VarBinary,8).Value=(object?)DecodeToken(request.DraftConcurrencyToken)??DBNull.Value;
            command.Parameters.AddWithValue("@LinesJson",BuildLinesJson(request.Lines,calculation));command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
            await using var reader=await command.ExecuteReaderAsync(ct);await reader.ReadAsync(ct);var result=new PurchaseOrderConfirmation(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetBoolean(3));await reader.CloseAsync();await transaction.CommitAsync(ct);return result;
        }
        catch(SqlException exception) when((exception.Number>=51200&&exception.Number<=51208)||exception.Number is 2601 or 2627)
        {await transaction.RollbackAsync(CancellationToken.None);if(exception.Number is 51204 or 51207 or 2601 or 2627)throw new PurchasingConflictException(exception.Message);throw new PurchasingValidationException(exception.Message);}
        catch{await transaction.RollbackAsync(CancellationToken.None);throw;}
    }

    public async Task CloseAsync(PurchasingUserIdentity user,Guid id,ClosePurchaseOrderRequest request,CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            await using var command=Procedure("purchasing.PurchaseOrderClose",connection,transaction);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@PurchaseOrderId",id);
            command.Parameters.Add("@RowVersion",SqlDbType.VarBinary,8).Value=DecodeToken(request.ConcurrencyToken)!;command.Parameters.AddWithValue("@Reason",request.Reason);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());await command.ExecuteNonQueryAsync(ct);await transaction.CommitAsync(ct);
        }
        catch(SqlException exception) when(exception.Number is>=51200 and<=51206){await transaction.RollbackAsync(CancellationToken.None);throw new PurchasingConflictException(exception.Message);}
        catch{await transaction.RollbackAsync(CancellationToken.None);throw;}
    }

    public async Task<IReadOnlyList<PurchaseOrderSuggestionInput>> SuggestionInputsAsync(
        PurchasingUserIdentity user, Guid warehouseId, Guid supplierId,
        IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        await using var connection=connections.Create();await connection.OpenAsync(ct);
        await using var command=Procedure("purchasing.PurchaseOrderSuggestionsGet",connection);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId",warehouseId);
        command.Parameters.AddWithValue("@SupplierId",supplierId);
        command.Parameters.AddWithValue("@ProductIdsJson",JsonSerializer.Serialize(productIds));
        try
        {
            await using var reader=await command.ExecuteReaderAsync(ct);
            var values=new List<PurchaseOrderSuggestionInput>();
            while(await reader.ReadAsync(ct))values.Add(new(reader.GetGuid(0),reader.GetDecimal(1),
                reader.GetDecimal(2),reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5),
                reader.GetString(6),reader.GetDecimal(7),reader.IsDBNull(8)?null:reader.GetFieldValue<DateTimeOffset>(8)));
            return values;
        }
        catch(SqlException exception) when(exception.Number==51220)
        {throw new PurchasingValidationException(exception.Message);}
    }

    private static async Task<PurchaseOrderDetail?> ReadAsync(SqlConnection connection,Guid businessId,Guid id,bool receiptOnly,CancellationToken ct)
    {
        await using var command=Procedure("purchasing.PurchaseOrderGet",connection);command.Parameters.AddWithValue("@BusinessId",businessId);command.Parameters.AddWithValue("@PurchaseOrderId",id);command.Parameters.AddWithValue("@ReceiptOnly",receiptOnly);
        await using var reader=await command.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return null;
        var header=new{Id=reader.GetGuid(0),Number=reader.IsDBNull(1)?null:reader.GetString(1),Status=reader.GetString(2),WarehouseId=reader.IsDBNull(3)?(Guid?)null:reader.GetGuid(3),Warehouse=reader.IsDBNull(4)?null:reader.GetString(4),SupplierId=reader.IsDBNull(5)?(Guid?)null:reader.GetGuid(5),Supplier=reader.IsDBNull(6)?null:reader.GetString(6),Ordered=reader.GetFieldValue<DateTimeOffset>(7),Expected=reader.IsDBNull(8)?(DateTimeOffset?)null:reader.GetFieldValue<DateTimeOffset>(8),Currency=reader.GetString(9),Notes=reader.IsDBNull(10)?null:reader.GetString(10),Net=reader.GetDecimal(11),Tax=reader.GetDecimal(12),Total=reader.GetDecimal(13),Updated=reader.GetFieldValue<DateTimeOffset>(14),Token=reader.GetString(15)};
        await reader.NextResultAsync(ct);var lines=new List<PurchaseOrderLine>();
        while(await reader.ReadAsync(ct))lines.Add(new(reader.GetGuid(0),reader.GetInt32(1),reader.GetGuid(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9),reader.GetDecimal(10),reader.GetString(11),reader.GetDecimal(12),reader.GetString(13),reader.GetDecimal(14),reader.GetDecimal(15),reader.GetDecimal(16),reader.GetString(17),reader.GetDecimal(18),reader.GetDecimal(19),reader.GetDecimal(20),reader.GetDecimal(21),reader.GetDecimal(22),reader.GetDecimal(23),reader.GetDecimal(24),reader.IsDBNull(25)?null:reader.GetFieldValue<DateTimeOffset>(25)));
        return new(header.Id,header.Number,header.Status,header.WarehouseId,header.Warehouse,header.SupplierId,header.Supplier,header.Ordered,header.Expected,header.Currency,header.Notes,header.Net,header.Tax,header.Total,header.Updated,header.Token,lines);
    }

    private static string BuildLinesJson(IReadOnlyCollection<PurchaseOrderLineRequest> source,PurchaseOrderCalculation? calculation)
    {
        if(calculation is null)return"[]";
        return JsonSerializer.Serialize(calculation.Lines.Select(line=>{var request=source.Single(x=>x.LineId==line.LineId);return new{line.LineId,line.LineNumber,line.ProductId,line.Description,line.OrderedQuantity,request.PresentationName,request.PresentationQuantity,request.UnitsPerPresentation,line.UnitCost,line.DiscountAmount,line.TaxCode,line.TaxRate,line.TaxTreatment,line.NetAmount,line.TaxAmount,line.LineTotal};}));
    }

    private static void AddCommon(SqlCommand command,PurchasingUserIdentity user,Guid id,Guid? warehouseId,Guid? supplierId,DateTimeOffset orderedAt,DateTimeOffset? expectedAt,string currencyCode,string? notes,PurchaseOrderCalculation? calculation)
    {
        command.Parameters.AddWithValue("@TenantId",user.TenantId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@PurchaseOrderId",id);command.Parameters.AddWithValue("@WarehouseId",(object?)warehouseId??DBNull.Value);command.Parameters.AddWithValue("@SupplierId",(object?)supplierId??DBNull.Value);command.Parameters.AddWithValue("@OrderedAt",orderedAt);command.Parameters.AddWithValue("@ExpectedAt",(object?)expectedAt??DBNull.Value);command.Parameters.AddWithValue("@CurrencyCode",currencyCode);command.Parameters.AddWithValue("@Notes",(object?)notes??DBNull.Value);AddDecimal(command,"@NetAmount",calculation?.NetAmount??0,19,4);AddDecimal(command,"@TaxAmount",calculation?.TaxAmount??0,19,4);AddDecimal(command,"@GrandTotal",calculation?.GrandTotal??0,19,4);
    }
    private static byte[]? DecodeToken(string? token){if(string.IsNullOrWhiteSpace(token))return null;try{return Convert.FromBase64String(token);}catch(FormatException exception){throw new PurchasingValidationException("ConcurrencyToken is invalid.",exception);}}
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale){var parameter=command.Parameters.Add(name,SqlDbType.Decimal);parameter.Precision=precision;parameter.Scale=scale;parameter.Value=value;}
    private static SqlCommand Procedure(string name,SqlConnection connection,SqlTransaction? transaction=null)=>new(name,connection,transaction){CommandType=CommandType.StoredProcedure};
}
