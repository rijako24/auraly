using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Returns;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Returns;
using Auraly.Domain.Returns;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReturnStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : ISalesReturnStore
{
    public async Task<SalesReturnAcceptance> AcceptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesReturnRequest request,
        CancellationToken cancellationToken)
    {
        var requestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptAttemptAsync(
                    user, idempotencyKey, request, requestHash, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<SalesReturnAcceptance> AcceptAttemptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesReturnRequest request,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await TryReplayAsync(connection, transaction, user.BusinessId,
                request.ReturnId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            await using (var reason = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM dbo.BusinessReasons WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND ReasonType=N'SalesReturn'
                  AND Code=@Code AND IsActive=1;
                """, connection, transaction))
            {
                reason.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                reason.Parameters.AddWithValue("@Code", request.ReasonCode);
                if (Convert.ToInt64(await reason.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new SalesReturnValidationException("El motivo de devolución no está activo en esta sede.");
            }

            decimal? dispatchReturnTotal = request.DispatchId is not null
                ? await ValidateDispatchReturnAsync(connection, transaction, user, request, cancellationToken) : null;
            var original = await LoadOriginalAsync(
                connection, transaction, user, request, cancellationToken);
            var originalLines = await LoadOriginalLinesAsync(
                connection, transaction, request.OriginalDocumentId,
                request.Lines.Select(line => line.OriginalLineNumber).ToArray(), cancellationToken);
            var lines = new List<SalesReturnLineSnapshot>(request.Lines.Count);
            var lineNumber = 0;
            var coversEveryRequestedBalance = true;
            foreach (var requested in request.Lines.OrderBy(line => line.OriginalLineNumber))
            {
                if (!originalLines.TryGetValue(requested.OriginalLineNumber, out var source))
                    throw new SalesReturnValidationException(
                        $"No se encontró la línea {requested.OriginalLineNumber} de la venta original.");
                if (requested.InventoryDisposition == ReturnInventoryDispositions.NotReturned &&
                    !source.IsRestaurantCheckout)
                    throw new SalesReturnValidationException(
                        "Solo una línea facturada desde una mesa admite cancelación sin retorno a inventario.");
                SalesReturnAmounts amounts;
                try
                {
                    amounts = SalesReturnAmountCalculator.Calculate(
                        source.Quantity, source.DiscountAmount, source.UntaxedAmount,
                        source.TaxAmount, source.LineTotal, source.ReturnedQuantity,
                        source.ReturnedDiscount, source.ReturnedUntaxed, source.ReturnedTax,
                        source.ReturnedTotal, requested.Quantity);
                }
                catch (ArgumentException exception)
                {
                    throw new SalesReturnConflictException(exception.Message);
                }
                coversEveryRequestedBalance &= requested.Quantity == source.Quantity - source.ReturnedQuantity;
                lines.Add(new SalesReturnLineSnapshot(
                    ++lineNumber, requested.OriginalLineNumber, source.ProductId,
                    source.Description, requested.Quantity, source.UnitPrice,
                    amounts.DiscountAmount, source.TaxCode, source.TaxRate,
                    amounts.UntaxedAmount, amounts.TaxAmount, amounts.LineTotal, source.RecognizedUnitCost,
                    requested.InventoryDisposition));
            }
            if (request.ReturnScopeCode == SalesReturnScopes.FullCancellation)
            {
                var availableLineCount = await CountAvailableLinesAsync(
                    connection, transaction, request.OriginalDocumentId, cancellationToken);
                if (!coversEveryRequestedBalance || lines.Count != availableLineCount)
                    throw new SalesReturnConflictException(
                        "La anulación total debe devolver el saldo completo de todas las líneas disponibles.");
            }
            var charges = await LoadOriginalChargesAsync(connection, transaction,
                request.OriginalDocumentId, request.ReturnedChargeIds, cancellationToken);
            var untaxed = lines.Sum(line => line.UntaxedAmount) +
                charges.Sum(charge => charge.InvoicedUntaxedAmount);
            var tax = lines.Sum(line => line.TaxAmount) +
                charges.Sum(charge => charge.InvoicedTaxAmount);
            var total = lines.Sum(line => line.LineTotal);
            total += charges.Sum(charge => charge.InvoicedAmount);
            if (untaxed + tax > original.UnroundedOutstanding)
                throw new SalesReturnConflictException(
                    "El valor seleccionado supera el saldo pendiente de la venta original.");
            var returnedUnrounded = untaxed + tax;
            total += SalesReturnAmountCalculator.AllocatePaymentRounding(
                original.OriginalUnrounded, original.OriginalRounding,
                original.OriginalUnrounded - original.UnroundedOutstanding,
                original.OriginalRounding - original.RemainingRounding,
                returnedUnrounded);
            if (total <= 0) throw new SalesReturnValidationException(
                "El valor de la devolución debe ser mayor que cero.");
            if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
                original.CustomerId is null)
                throw new SalesReturnValidationException(
                    "El abono a cartera requiere identificar al cliente de la venta original.");
            if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
                (original.ReceivableOutstanding <= 0 || total > original.ReceivableOutstanding))
                throw new SalesReturnValidationException(
                    "El abono a cartera requiere saldo pendiente suficiente en la cuenta por cobrar de la venta.");

            if (dispatchReturnTotal is { } expectedReturn && total != expectedReturn)
                throw new SalesReturnConflictException("El importe de la devolución cambió desde la entrega. Revisa el documento antes de conciliar.");
            var settlement = new RefundSettlementContext(null, null, null, null);
            decimal? receivableReversal = null;
            if(request.EconomicResolution == ReturnEconomicResolutions.ReverseUncollected)
            {
                if(original.ReceivableOutstanding<0)
                    throw new SalesReturnConflictException("La cartera y sus reservas requieren conciliación antes de devolver la venta.");
                // Keep the existing credit-first return policy. Only the
                // remainder cancels logical tenders that were never collected.
                receivableReversal=decimal.Min(total,original.ReceivableOutstanding);
            }
            IReadOnlyList<UncollectedPaymentReversal>? uncollectedReversals = request.EconomicResolution == ReturnEconomicResolutions.ReverseUncollected
                ? total==receivableReversal ? [] : await AllocateUncollectedReversalAsync(
                    connection, transaction, request, total-receivableReversal!.Value, cancellationToken)
                : null;
            if (request.EconomicResolution == ReturnEconomicResolutions.Refund ||
                request.WorkSessionId is not null)
            {
                request = request with
                {
                    WorkSessionId = await ResolveOpenWorkSessionAsync(
                        connection, transaction, user, request.WorkSessionId, cancellationToken)
                };
            }
            if (request.EconomicResolution == ReturnEconomicResolutions.Refund)
            {
                settlement = await ValidateRefundAsync(
                    connection, transaction, user, request, total, cancellationToken);
                request = request with
                {
                    OriginalPaymentNumber = settlement.OriginalPaymentNumber,
                    BankAccountId = settlement.BankAccountId
                };
            }
            var now = timeProvider.GetUtcNow();
            var number = await AllocateNumberAsync(
                connection, transaction, user.TenantId, user.BusinessId, now, cancellationToken);
            var sequence = await SqlOperationalDocumentAllocator.AllocateSequenceAsync(
                connection, transaction, user.BusinessId, now, cancellationToken);
            var payload = new SalesReturnDocumentPayload(
                user.TenantId, user.BusinessId, request.ReturnId, request.WarehouseId,
                request.OriginalDocumentId, user.UserId, number.FullNumber, number.SeriesId,
                number.Prefix, number.SeriesCode, number.Consecutive, request.ReturnedAt,
                request.EconomicResolution, request.RefundMethodCode, "1",
                request.ReasonDescription, original.CustomerId, original.CustomerIdentification,
                untaxed, tax, total, lines, request.WorkSessionId,
                request.OriginalPaymentNumber, request.ReasonCode, request.Notes,
                request.ReturnScopeCode, settlement.CardFranchiseCode,
                settlement.ApprovalNumber, request.BankAccountId,
                request.SettlementReference, request.SettlementNotes, charges,
                DispatchId: request.DispatchId, UncollectedPaymentReversals: uncollectedReversals,
                ReceivableReversalAmount: receivableReversal,OriginalDispatchPaymentId:request.OriginalDispatchPaymentId);
            var payloadJson = SalesReturnContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
            var movementId = ids.NewId();
            await InsertReturnAsync(connection, transaction, request, user, number,
                original, settlement, requestHash, idempotencyKey, untaxed, tax, total, receivableReversal, now,
                cancellationToken);
            await InsertLinesAsync(connection, transaction, request.ReturnId,
                request.OriginalDocumentId, lines, cancellationToken);
            await InsertChargesAsync(connection, transaction, request.ReturnId,
                request.OriginalDocumentId, charges, cancellationToken);
            await InsertJobAsync(connection, transaction, request.ReturnId,
                user.BusinessId, movementId, sequence, payloadJson, payloadHash, now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SalesReturnAcceptance(request.ReturnId, movementId,
                number.FullNumber, "Accepted", sequence, false, total,
                request.RefundMethodCode, request.WorkSessionId);
        }
        catch (SalesReturnConflictException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new SalesReturnConflictException(
                "El número, la identidad o la clave de la devolución ya están en uso.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<SalesReturnAcceptance?> TryReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid returnId, string idempotencyKey, byte[] requestHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.ReturnId,r.DocumentNumber,r.Status,r.PayloadHash,
                   j.ProcessingSequence,j.JobId,r.TotalAmount,
                   r.RefundMethodCode,r.WorkSessionId
            FROM dbo.SalesReturns r WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=r.ReturnId AND j.DocumentType=N'SalesReturn'
            WHERE r.BusinessId=@BusinessId
              AND (r.ReturnId=@ReturnId OR r.IdempotencyKey=@IdempotencyKey);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReturnId", returnId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash))
            throw new SalesReturnConflictException(
                "La identidad o clave de la devolución ya se usó con otros datos.");
        return new SalesReturnAcceptance(reader.GetGuid(0), reader.GetGuid(5),
            reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true,
            reader.GetDecimal(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8));
    }

    private static async Task<OriginalSale> LoadOriginalAsync(
        SqlConnection connection, SqlTransaction transaction,
        SalesReturnUserIdentity user, ConfirmSalesReturnRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WITH (HOLDLOCK)
                           WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51200,'La sede no pertenece a la empresa autenticada.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses WITH (HOLDLOCK)
                           WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1)
              THROW 51201,'Selecciona una bodega de venta válida para la devolución.',1;
            -- Dispatch assignment, delivery and return acceptance use the same
            -- aggregate lock order. Never change a captured manifest behind it.
            DECLARE @ActiveDispatch uniqueidentifier,@DispatchSource uniqueidentifier,@Delivery nvarchar(24),@Collection nvarchar(16);
            SELECT @ActiveDispatch=dispatch.DispatchId,@DispatchSource=source.DispatchSourceDocumentId,
              @Delivery=delivery.DeliveryStatus,@Collection=sale.CollectionMode
            FROM dbo.DispatchSourceDocuments source WITH(READCOMMITTEDLOCK,FORCESEEK(IX_DispatchSourceDocuments_Source(SourceDocumentId)))
            JOIN dbo.Dispatches dispatch WITH(UPDLOCK,HOLDLOCK) ON source.DispatchId=dispatch.DispatchId
            JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
            LEFT JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.IsCurrent=1
            WHERE source.SourceDocumentId=@OriginalDocumentId AND dispatch.BusinessId=@BusinessId
              AND dispatch.TenantId=@TenantId AND dispatch.Status NOT IN(N'Cancelled',N'Closed')
            OPTION(FORCE ORDER,LOOP JOIN);
            -- An absent assignment must not take a serializable range lock
            -- before the sale: assignment holds that sale before inserting its
            -- source. Lock the sale, then detect an assignment that won the race.
            DECLARE @LockedSale uniqueidentifier;
            SELECT @LockedSale=DocumentId FROM dbo.SalesDocuments WITH(UPDLOCK,HOLDLOCK,INDEX(PK_SalesDocuments))
              WHERE DocumentId=@OriginalDocumentId AND BusinessId=@BusinessId;
            IF @ActiveDispatch IS NULL AND EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments source WITH(READCOMMITTEDLOCK)
              JOIN dbo.Dispatches dispatch WITH(READCOMMITTEDLOCK) ON dispatch.DispatchId=source.DispatchId
              WHERE source.SourceDocumentId=@OriginalDocumentId AND dispatch.BusinessId=@BusinessId
                AND dispatch.TenantId=@TenantId AND dispatch.Status NOT IN(N'Cancelled',N'Closed'))
              THROW 51202,N'La venta fue asignada a un despacho mientras se preparaba la devolución. Revisa su entrega antes de continuar.',1;
            IF @ActiveDispatch IS NOT NULL AND @FromDispatch=0
            BEGIN
              IF @Delivery IS NULL OR @Collection=N'Dispatch'
                THROW 51202,N'La venta está asignada a un despacho. Registra la devolución en su entrega; si aún es borrador, retira primero la venta del despacho.',1;
              IF EXISTS(SELECT 1 FROM OPENJSON(@RequestedLines) WITH(OriginalLineNumber int,Quantity decimal(19,6),InventoryDisposition nvarchar(24)) requested
                LEFT JOIN dbo.DispatchLines assigned ON assigned.DispatchSourceDocumentId=@DispatchSource AND assigned.SourceLineNumber=requested.OriginalLineNumber
                LEFT JOIN dbo.SalesDocumentLines original ON original.DocumentId=@OriginalDocumentId AND original.LineNumber=requested.OriginalLineNumber
                LEFT JOIN dbo.DispatchDeliveryReturns captured ON captured.DispatchSourceDocumentId=@DispatchSource AND captured.OriginalLineNumber=requested.OriginalLineNumber
                OUTER APPLY(SELECT SUM(prior.Quantity) Quantity FROM dbo.SalesReturnLines prior
                  WHERE prior.OriginalDocumentId=@OriginalDocumentId AND prior.OriginalLineNumber=requested.OriginalLineNumber) already
                WHERE assigned.DispatchLineId IS NULL OR requested.Quantity>
                  CASE WHEN @Delivery=N'NotDelivered' THEN assigned.AssignedQuantity ELSE COALESCE(captured.Quantity,0) END
                    -(COALESCE(already.Quantity,0)-(original.Quantity-assigned.AssignedQuantity))
                  OR requested.InventoryDisposition<>CASE WHEN @Delivery=N'NotDelivered' THEN N'Sellable' ELSE captured.InventoryDisposition END)
                THROW 51202,N'El reembolso administrativo debe corresponder a la mercancía registrada como devuelta en la entrega y aún no reembolsada.',1;
            END;
            SELECT d.CustomerId,d.CustomerIdentification,
                   COALESCE((SELECT SUM(available.AvailableAmount) FROM dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
                     CROSS APPLY accounting.ObligationAvailability(r.BusinessId,N'Receivable',r.ReceivableId,r.OutstandingAmount,NULL) available
                     WHERE r.BusinessId=d.BusinessId AND r.SourceDocumentId=d.DocumentId
                       AND r.SourceDocumentType=d.DocumentType
                       AND r.Status IN(N'Open',N'PartiallyPaid')),0),
                   d.UntaxedAmount+d.TaxAmount,
                   d.PayableAmount-d.UntaxedAmount-d.TaxAmount,
                   d.UntaxedAmount+d.TaxAmount-COALESCE(prior.Unrounded,0),
                   d.PayableAmount-d.UntaxedAmount-d.TaxAmount-
                     COALESCE(prior.Rounding,0),
                   CAST(CASE WHEN @IsRefund=1 AND EXISTS(SELECT 1 FROM accounting.SalePaymentPostingFacts(@TenantId,@BusinessId,d.DocumentId) payment
                     WHERE payment.EffectivePaymentReady=0) THEN 1 ELSE 0 END AS bit)
            FROM dbo.SalesDocuments d WITH (UPDLOCK,HOLDLOCK)
            OUTER APPLY (
              SELECT SUM(r.UntaxedAmount+r.TaxAmount) Unrounded,
                     SUM(r.TotalAmount-r.UntaxedAmount-r.TaxAmount) Rounding
              FROM dbo.SalesReturns r WITH(UPDLOCK,HOLDLOCK)
              WHERE r.OriginalDocumentId=d.DocumentId
            ) prior
            WHERE d.DocumentId=@OriginalDocumentId AND d.BusinessId=@BusinessId
              AND d.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
              AND d.ProcessingStatus=N'Completed';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@OriginalDocumentId", request.OriginalDocumentId);
        command.Parameters.AddWithValue("@IsRefund", request.EconomicResolution == ReturnEconomicResolutions.Refund);
        command.Parameters.AddWithValue("@FromDispatch", request.DispatchId is not null);
        command.Parameters.Add("@RequestedLines",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Lines);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new SalesReturnValidationException(
                    "No se encontró la venta original procesada en esta sede.");
            if (request.EconomicResolution == ReturnEconomicResolutions.Refund && reader.GetBoolean(7))
                throw new SalesReturnConflictException("La corrección del recaudo debe terminar de procesarse antes de reembolsar la venta.");
            return new OriginalSale(
                reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1),
                reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
                reader.GetDecimal(5), reader.GetDecimal(6));
        }
        catch (SqlException exception) when (exception.Number is 51200 or 51201)
        {
            throw new SalesReturnValidationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number == 51202)
        {
            throw new SalesReturnConflictException(exception.Message);
        }
    }

    private static async Task<IReadOnlyDictionary<int, OriginalLine>> LoadOriginalLinesAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid originalDocumentId, IReadOnlyCollection<int> originalLineNumbers,
        CancellationToken cancellationToken)
    {
        if (originalLineNumbers.Count == 0 ||
            originalLineNumbers.Distinct().Count() != originalLineNumbers.Count)
            throw new SalesReturnValidationException("Las líneas seleccionadas no son válidas.");
        const string sql = """
            WITH Requested AS
            (
                SELECT DISTINCT TRY_CONVERT(int,[value]) LineNumber FROM OPENJSON(@LineNumbers)
            ),
            Returned AS
            (
                SELECT r.OriginalLineNumber,SUM(r.Quantity) Quantity,
                  SUM(r.DiscountAmount) DiscountAmount,SUM(r.UntaxedAmount) UntaxedAmount,
                  SUM(r.TaxAmount) TaxAmount,SUM(r.LineTotal) LineTotal
                FROM dbo.SalesReturnLines r WITH (UPDLOCK,HOLDLOCK)
                WHERE r.OriginalDocumentId=@DocumentId
                GROUP BY r.OriginalLineNumber
            ),
            Costs AS
            (
                SELECT m.LineNumber,MAX(m.RecognizedUnitCost) RecognizedUnitCost
                FROM dbo.InventoryMovements m
                WHERE m.DocumentId=@DocumentId
                  AND m.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
                  AND m.MovementType=N'Sale'
                GROUP BY m.LineNumber
            )
            SELECT l.LineNumber,l.ProductId,l.Description,l.Quantity,l.UnitPrice,l.DiscountAmount,
                   l.TaxCode,l.TaxRate,l.UntaxedAmount,l.TaxAmount,l.LineTotal,
                   COALESCE(returned.Quantity,0),COALESCE(returned.DiscountAmount,0),
                   COALESCE(returned.UntaxedAmount,0),COALESCE(returned.TaxAmount,0),
                   COALESCE(returned.LineTotal,0),COALESCE(l.UnitCostSnapshot,cost.RecognizedUnitCost,0),
                   CAST(CASE WHEN restaurant.DocumentId IS NULL THEN 0 ELSE 1 END AS bit)
            FROM dbo.SalesDocumentLines l WITH (UPDLOCK,HOLDLOCK)
            JOIN Requested requested ON requested.LineNumber=l.LineNumber
            LEFT JOIN Returned returned ON returned.OriginalLineNumber=l.LineNumber
            LEFT JOIN Costs cost ON cost.LineNumber=l.LineNumber
            LEFT JOIN sales.RestaurantCheckoutLines restaurant
              ON restaurant.DocumentId=l.DocumentId
             AND restaurant.LineNumber=l.LineNumber
            WHERE l.DocumentId=@DocumentId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", originalDocumentId);
        command.Parameters.AddWithValue("@LineNumbers", JsonSerializer.Serialize(originalLineNumbers));
        var values = new Dictionary<int, OriginalLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(reader.GetInt32(0), new OriginalLine(reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
                reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13),
                reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16),
                reader.GetBoolean(17)));
        return values;
    }

    private static async Task<RefundSettlementContext> ValidateRefundAsync(
        SqlConnection connection, SqlTransaction transaction, SalesReturnUserIdentity user,
        ConfirmSalesReturnRequest request, decimal requestedAmount,
        CancellationToken cancellationToken)
    {
        if (request.RefundMethodCode == SalesReturnRefundMethods.Cash)
        {
            return new(null, null, null, null);
        }

        if (request.RefundMethodCode == SalesReturnRefundMethods.Transfer)
        {
            await using var bank = new SqlCommand("""
                DECLARE @AccountingEnabled bit=CASE WHEN EXISTS(
                  SELECT 1 FROM dbo.AccountingTenantSettings settings
                  INNER JOIN dbo.Businesses business ON business.TenantId=settings.TenantId
                  WHERE business.BusinessId=@BusinessId AND settings.Status=N'Ready') THEN 1 ELSE 0 END;
                SELECT CASE
                  WHEN @AccountingEnabled=0 AND @BankAccountId IS NULL THEN 1
                  WHEN @AccountingEnabled=1 AND EXISTS(
                    SELECT 1 FROM accounting.BankAccounts b WITH(UPDLOCK,HOLDLOCK)
                    INNER JOIN dbo.Businesses business ON business.TenantId=b.TenantId
                    CROSS APPLY accounting.BankPostingAccount(b.TenantId,b.BankAccountId) account
                    WHERE b.BankAccountId=@BankAccountId AND business.BusinessId=@BusinessId
                      AND b.IsActive=1 AND account.AccountId IS NOT NULL) THEN 1
                  ELSE 0 END;
                """, connection, transaction);
            bank.Parameters.AddWithValue("@BankAccountId", (object?)request.BankAccountId ?? DBNull.Value);
            bank.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            if (Convert.ToInt64(await bank.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new SalesReturnValidationException("La contabilidad activa requiere una cuenta bancaria de salida activa y un auxiliar único para esta sede.");
            return new(null, request.BankAccountId, null, null);
        }

        if(request.OriginalDispatchPaymentId is not null)
            return await ValidateDispatchCardRefundAsync(connection,transaction,user,request,requestedAmount,cancellationToken);
        await using var card = new SqlCommand("""
            SELECT p.EffectiveMethodCode,p.EffectiveAmount-COALESCE(reversed.Amount,0),
                   p.EffectiveCardFranchiseCode,p.EffectiveApprovalNumber
            FROM dbo.SalesDocuments sale
            CROSS APPLY accounting.SalePaymentPostingFacts(@TenantId,@BusinessId,sale.DocumentId) p
            OUTER APPLY
            (
                SELECT SUM(s.TotalAmount) Amount
                FROM dbo.SalesReturns s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.OriginalDocumentId=p.DocumentId
                  AND s.OriginalPaymentNumber=p.PaymentNumber
                  AND s.EconomicResolution=N'Refund'
            ) reversed
            WHERE sale.DocumentId=@DocumentId AND sale.CollectionMode=N'Session'
              AND p.PaymentNumber=@PaymentNumber AND p.EffectivePaymentReady=1;
            """, connection, transaction);
        card.Parameters.AddWithValue("@DocumentId", request.OriginalDocumentId);
        card.Parameters.AddWithValue("@TenantId", user.TenantId);
        card.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        card.Parameters.AddWithValue("@PaymentNumber", request.OriginalPaymentNumber!.Value);
        await using var reader = await card.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            reader.GetString(0) != request.RefundMethodCode)
            throw new SalesReturnValidationException(
                "La reversión debe corresponder a un pago original de la misma tarjeta.");
        if (reader.GetDecimal(1) < requestedAmount)
            throw new SalesReturnValidationException(
                "El valor supera el saldo disponible del pago original para reversar.");
        return new(request.OriginalPaymentNumber, null,
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid businessId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await SqlOperationalDocumentAllocator.AllocateExistingNumberAsync(
                connection, transaction, tenantId, businessId, AuralyDocumentTypes.SalesReturn, now, cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 51730)
        {
            throw new SalesReturnValidationException(
                "Esta sede no tiene una serie activa para devoluciones de venta.", exception);
        }
        catch (SqlException exception) when (exception.Number == 51731)
        {
            throw new SalesReturnValidationException("Se agotó la serie de devoluciones de venta.", exception);
        }
    }

    private static async Task InsertReturnAsync(
        SqlConnection connection, SqlTransaction transaction, ConfirmSalesReturnRequest request,
        SalesReturnUserIdentity user, AuralyDocumentNumberAssignment number, OriginalSale original,
        RefundSettlementContext settlement, byte[] requestHash, string idempotencyKey,
        decimal untaxed, decimal tax, decimal total, decimal? receivableReversal,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SalesReturns
              (ReturnId,BusinessId,WarehouseId,WorkSessionId,OriginalDocumentId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,
               ReturnedAt,ReturnScopeCode,EconomicResolution,RefundMethodCode,OriginalPaymentNumber,
               CardFranchiseCode,ApprovalNumber,BankAccountId,RefundReference,RefundNotes,CorrectionCode,
               ReasonCode,ReasonDescription,Notes,
               CustomerId,CustomerIdentification,UntaxedAmount,TaxAmount,TotalAmount,Status,
               CreatedByUserId,AcceptedAt,ReceivableReversalAmount,OriginalDispatchPaymentId)
            VALUES(@Id,@BusinessId,@WarehouseId,@WorkSessionId,@OriginalId,@SeriesId,@Number,@Prefix,@SeriesCode,
               @Consecutive,@Key,@Hash,@ReturnedAt,@ReturnScopeCode,@Resolution,@Method,@PaymentNumber,
               @CardFranchiseCode,@ApprovalNumber,@BankAccountId,@RefundReference,@RefundNotes,N'1',@ReasonCode,@Reason,@Notes,@CustomerId,
               @CustomerIdentification,@Untaxed,@Tax,@Total,N'Accepted',@UserId,@Now,@ReceivableReversal,@DispatchPayment);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.ReturnId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@WorkSessionId", (object?)request.WorkSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@OriginalId", request.OriginalDocumentId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value=requestHash;
        command.Parameters.AddWithValue("@ReturnedAt", request.ReturnedAt);
        command.Parameters.AddWithValue("@ReturnScopeCode", request.ReturnScopeCode);
        command.Parameters.AddWithValue("@Resolution", request.EconomicResolution);
        command.Parameters.AddWithValue("@Method", (object?)request.RefundMethodCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@PaymentNumber", (object?)request.OriginalPaymentNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@DispatchPayment",(object?)request.OriginalDispatchPaymentId??DBNull.Value);
        command.Parameters.AddWithValue("@CardFranchiseCode", (object?)settlement.CardFranchiseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@ApprovalNumber", (object?)settlement.ApprovalNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@BankAccountId", (object?)request.BankAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("@RefundReference", (object?)request.SettlementReference ?? DBNull.Value);
        command.Parameters.AddWithValue("@RefundNotes", (object?)request.SettlementNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReasonCode", request.ReasonCode);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@Reason", request.ReasonDescription);
        command.Parameters.AddWithValue("@CustomerId", (object?)original.CustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@CustomerIdentification", original.CustomerIdentification);
        AddDecimal(command,"@Untaxed",untaxed,19,4); AddDecimal(command,"@Tax",tax,19,4);
        AddDecimal(command,"@Total",total,19,4);
        command.Parameters.Add("@ReceivableReversal",SqlDbType.Decimal).Value=(object?)receivableReversal??DBNull.Value;
        command.Parameters["@ReceivableReversal"].Precision=19;
        command.Parameters["@ReceivableReversal"].Scale=4;
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountAvailableLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid originalDocumentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.SalesDocumentLines l WITH(UPDLOCK,HOLDLOCK)
            OUTER APPLY
            (
                SELECT COALESCE(SUM(rl.Quantity),0) AS ReturnedQuantity
                FROM dbo.SalesReturnLines rl WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.SalesReturns r WITH(UPDLOCK,HOLDLOCK) ON r.ReturnId=rl.ReturnId
                WHERE rl.OriginalDocumentId=l.DocumentId
                  AND rl.OriginalLineNumber=l.LineNumber
                  AND r.Status IN(N'Accepted',N'Processed')
            ) returned
            WHERE l.DocumentId=@DocumentId
              AND l.Quantity-returned.ReturnedQuantity>0;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", originalDocumentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<Guid> ResolveOpenWorkSessionAsync(
        SqlConnection connection, SqlTransaction transaction, SalesReturnUserIdentity user,
        Guid? requestedWorkSessionId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT WorkSessionId
            FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND UserId=@UserId
              AND Status=N'Open' AND Kind=N'Normal' AND (@RequestedId IS NULL OR WorkSessionId=@RequestedId)
              AND ((@DeviceId IS NULL AND DeviceId IS NULL)
                OR (@DeviceId IS NOT NULL AND DeviceId=@DeviceId));
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@RequestedId", (object?)requestedWorkSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@DeviceId", (object?)user.DeviceId ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid workSessionId
            ? workSessionId
            : throw new SalesReturnValidationException(
                "La devolución requiere una sesión de trabajo abierta para el usuario actual.");
    }

    private static async Task<IReadOnlyList<SalesReturnChargeSnapshot>> LoadOriginalChargesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid originalDocumentId,
        IReadOnlyCollection<Guid>? requestedIds, CancellationToken cancellationToken)
    {
        if (requestedIds is not { Count: > 0 }) return [];
        if (requestedIds.Count > 10 || requestedIds.Any(id => id == Guid.Empty) ||
            requestedIds.Distinct().Count() != requestedIds.Count)
            throw new SalesReturnValidationException("Los cargos seleccionados para devolver no son válidos.");
        await using var command = new SqlCommand("""
            DECLARE @LockedExpenseCount int;
            SELECT @LockedExpenseCount=COUNT(*) FROM dbo.Expenses WITH(UPDLOCK,HOLDLOCK)
            WHERE SourceInvoiceId=@DocumentId AND ExpenseId IN (
              SELECT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@Ids));
            DECLARE @LockedSource uniqueidentifier;
            SELECT @LockedSource=source.SourceDocumentId FROM dbo.Expenses expense
            JOIN dbo.AccountingSourceDocuments source WITH(UPDLOCK,HOLDLOCK)
              ON source.SourceDocumentId=expense.ExpenseId AND source.SourceDocumentType=N'Expense'
              AND source.BusinessId=expense.BusinessId
            WHERE expense.SourceInvoiceId=@DocumentId AND expense.ExpenseId IN(
              SELECT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@Ids))
            ORDER BY source.SourceDocumentId;
            WITH Requested AS (
              SELECT DISTINCT TRY_CONVERT(uniqueidentifier,[value]) AppliedChargeId
              FROM OPENJSON(@Ids)
            )
            SELECT charge.AppliedChargeId,charge.ChargeId,charge.Code,charge.Name,charge.Amount,
                   charge.InvoicedAmount,charge.ExpenseAmount,charge.InvoicedUntaxedAmount,
                   charge.InvoicedTaxAmount,charge.TaxCode,charge.TaxRate,
                   charge.SupplierUntaxedAmount,charge.SupplierVatAmount,
                   charge.SupplierId,charge.ExpenseAccountId,charge.CostCenterId
            FROM dbo.DocumentProcessingPayloads payload WITH(UPDLOCK,HOLDLOCK)
            CROSS APPLY OPENJSON(payload.PayloadJson,N'$.charges') WITH(
              AppliedChargeId uniqueidentifier N'$.appliedChargeId',ChargeId uniqueidentifier N'$.chargeId',
              Code nvarchar(32) N'$.code',Name nvarchar(120) N'$.name',Amount decimal(19,4) N'$.amount',
              InvoicedAmount decimal(19,4) N'$.invoicedAmount',ExpenseAmount decimal(19,4) N'$.expenseAmount',
              InvoicedUntaxedAmount decimal(19,4) N'$.invoicedUntaxedAmount',InvoicedTaxAmount decimal(19,4) N'$.invoicedTaxAmount',
              TaxCode nvarchar(16) N'$.taxCode',TaxRate decimal(9,6) N'$.taxRate',
              SupplierUntaxedAmount decimal(19,4) N'$.supplierUntaxedAmount',SupplierVatAmount decimal(19,4) N'$.supplierVatAmount',
              SupplierId uniqueidentifier N'$.supplier.supplierId',ExpenseAccountId uniqueidentifier N'$.expenseAccountId',
              CostCenterId uniqueidentifier N'$.costCenterId') charge
            JOIN Requested requested ON requested.AppliedChargeId=charge.AppliedChargeId
            LEFT JOIN dbo.SalesReturnCharges returned WITH(UPDLOCK,HOLDLOCK)
              ON returned.AppliedChargeId=charge.AppliedChargeId
            JOIN dbo.Expenses expense WITH(UPDLOCK,HOLDLOCK)
              ON expense.ExpenseId=charge.AppliedChargeId AND expense.SourceInvoiceId=payload.DocumentId
              AND expense.BusinessId=payload.BusinessId
            WHERE payload.DocumentId=@DocumentId
              AND payload.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
              AND returned.AppliedChargeId IS NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries entry
                CROSS APPLY purchasing.ReceiptCostVoucherLines(entry.EntryId) cost
                WHERE entry.SourceDocumentId=charge.AppliedChargeId AND entry.SourceDocumentType=N'Expense'
                  AND cost.AssignedFunctionalAmount>0)
              AND (expense.Status=N'Processed' OR (expense.Status=N'Cancelled' AND charge.InvoicedAmount>0));
            """, connection, transaction);
        command.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(requestedIds));
        command.Parameters.AddWithValue("@DocumentId", originalDocumentId);
        var values = new List<SalesReturnChargeSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.GetDecimal(8), reader.GetString(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetDecimal(12), reader.GetGuid(13), reader.GetGuid(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15)));
        if (values.Count != requestedIds.Count)
            throw new SalesReturnConflictException("Uno o más cargos ya fueron devueltos, tienen una anulación en proceso, costos distribuidos en compras o no pertenecen a la factura.");
        return values;
    }

    private static async Task InsertLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid returnId,
        Guid originalId, IReadOnlyList<SalesReturnLineSnapshot> lines,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SalesReturnLines
              (ReturnId,OriginalDocumentId,LineNumber,OriginalLineNumber,ProductId,
               DescriptionSnapshot,Quantity,UnitPrice,DiscountAmount,TaxCode,TaxRate,
               UntaxedAmount,TaxAmount,LineTotal,RecognizedUnitCost,InventoryDisposition)
            SELECT @ReturnId,@OriginalId,input.LineNumber,input.OriginalLineNumber,input.ProductId,
               input.Description,input.Quantity,input.UnitPrice,input.DiscountAmount,input.TaxCode,input.TaxRate,
               input.UntaxedAmount,input.TaxAmount,input.LineTotal,input.RecognizedUnitCost,input.InventoryDisposition
            FROM OPENJSON(@Lines) WITH(LineNumber int,OriginalLineNumber int,ProductId uniqueidentifier,
               Description nvarchar(300),Quantity decimal(19,6),UnitPrice decimal(19,4),
               DiscountAmount decimal(19,4),TaxCode nvarchar(16),TaxRate decimal(9,6),
               UntaxedAmount decimal(19,4),TaxAmount decimal(19,4),LineTotal decimal(19,4),
               RecognizedUnitCost decimal(19,6),InventoryDisposition nvarchar(24)) input;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ReturnId", returnId);
        command.Parameters.AddWithValue("@OriginalId", originalId);
        command.Parameters.AddWithValue("@Lines", JsonSerializer.Serialize(lines));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != lines.Count)
            throw new DBConcurrencyException("Las líneas de la devolución no se guardaron completamente.");
    }

    private static async Task InsertChargesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid returnId, Guid originalId,
        IReadOnlyList<SalesReturnChargeSnapshot> charges, CancellationToken cancellationToken)
    {
        if (charges.Count == 0) return;
        await using var command = new SqlCommand("""
            INSERT dbo.SalesReturnCharges(ReturnId,OriginalDocumentId,AppliedChargeId,ChargeId,Code,Name,
              Amount,InvoicedAmount,ExpenseAmount,InvoicedUntaxedAmount,InvoicedTaxAmount,
              TaxCode,TaxRate,SupplierUntaxedAmount,SupplierVatAmount,SupplierId,ExpenseAccountId,CostCenterId)
            SELECT @ReturnId,@OriginalId,input.AppliedChargeId,input.ChargeId,input.Code,input.Name,
              input.Amount,input.InvoicedAmount,input.ExpenseAmount,input.InvoicedUntaxedAmount,input.InvoicedTaxAmount,
              input.TaxCode,input.TaxRate,input.SupplierUntaxedAmount,input.SupplierVatAmount,input.SupplierId,input.ExpenseAccountId,input.CostCenterId
            FROM OPENJSON(@Charges) WITH(
              AppliedChargeId uniqueidentifier,ChargeId uniqueidentifier,Code nvarchar(32),Name nvarchar(120),
              Amount decimal(19,4),InvoicedAmount decimal(19,4),ExpenseAmount decimal(19,4),
              InvoicedUntaxedAmount decimal(19,4),InvoicedTaxAmount decimal(19,4),SupplierUntaxedAmount decimal(19,4),
              TaxCode nvarchar(16),TaxRate decimal(9,6),SupplierVatAmount decimal(19,4),SupplierId uniqueidentifier,ExpenseAccountId uniqueidentifier,CostCenterId uniqueidentifier) input;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ReturnId", returnId);
        command.Parameters.AddWithValue("@OriginalId", originalId);
        command.Parameters.AddWithValue("@Charges", JsonSerializer.Serialize(charges));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != charges.Count)
            throw new DBConcurrencyException("Los cargos de la devolución no se guardaron completamente.");
    }

    private static async Task InsertJobAsync(
        SqlConnection connection, SqlTransaction transaction, Guid returnId, Guid businessId,
        Guid movementId, long sequence, string payload, byte[] payloadHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'SalesReturn',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'SalesReturn',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentId", returnId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@PayloadHash",SqlDbType.Binary,32).Value=payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision; parameter.Scale=scale; parameter.Value=value;
    }

    private sealed record OriginalSale(Guid? CustomerId,string CustomerIdentification,
        decimal ReceivableOutstanding,decimal OriginalUnrounded,decimal OriginalRounding,
        decimal UnroundedOutstanding,decimal RemainingRounding);
    private sealed record RefundSettlementContext(
        int? OriginalPaymentNumber,
        Guid? BankAccountId,
        string? CardFranchiseCode,
        string? ApprovalNumber);
    private sealed record OriginalLine(Guid ProductId,string Description,decimal Quantity,
        decimal UnitPrice,decimal DiscountAmount,string TaxCode,decimal TaxRate,
        decimal UntaxedAmount,decimal TaxAmount,decimal LineTotal,decimal ReturnedQuantity,
        decimal ReturnedDiscount,decimal ReturnedUntaxed,decimal ReturnedTax,decimal ReturnedTotal,
        decimal RecognizedUnitCost,bool IsRestaurantCheckout);
}
