using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Payments;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

/// <summary>Batch read adapter for the canonical tenant catalog, inside source acceptance.</summary>
internal static class SqlPaymentMethodResolver
{
    public static Task ValidateSaleAsync(SqlConnection connection, SqlTransaction transaction,
        Auraly.Contracts.Sales.PosSaleUploadRequest sale, CancellationToken cancellationToken)
    {
        var purposeError=Auraly.Contracts.Sales.OrderCollectionPaymentPolicy.Validate(sale.SourceOrderId,sale.CollectionMode);
        if(purposeError is not null) throw new Auraly.Application.Sales.PosSaleInvalidException(purposeError);
        if (sale.Payments.Any(payment => !PortfolioPaymentMethods.IsSupported(payment.MethodCode)))
            throw new Auraly.Application.Sales.PosSaleInvalidException("Selecciona un medio de dinero válido. La cartera se registra mediante Crédito cliente.");
        if (!PaymentMethodEvidence.IsValidSale(sale.Payments.Select(payment => new PaymentMethodEvidence(
                payment.PaymentMethodId, payment.PaymentMethodVersion, payment.PaymentMethodCode,
                payment.PaymentMethodName, payment.AccountId)), sale.Credit is { } credit ?
                new PaymentMethodEvidence(credit.PaymentMethodId,credit.PaymentMethodVersion,
                    credit.PaymentMethodCode,credit.PaymentMethodName,credit.AccountId) : null))
            throw new Auraly.Application.Sales.PosSaleInvalidException("La identidad de las formas de pago no es válida.");
        var selections = sale.Payments.Select(payment => new { payment.MethodCode, payment.PaymentMethodId })
            .Concat(sale.Credit?.PaymentMethodId is null ? [] : [new { MethodCode="Credit", sale.Credit.PaymentMethodId }]).ToArray();
        return selections.All(payment => payment.PaymentMethodId is null) ? Task.CompletedTask :
            ValidateSaleEvidenceAsync(connection, transaction, sale.TenantId,
                JsonSerializer.Serialize(selections), false, cancellationToken);
    }

    public static async Task ValidateOnlineSaleAsync(SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, IReadOnlyCollection<Auraly.Contracts.Sales.OnlineSalesPayment> payments,
        Auraly.Contracts.Sales.OnlineSalesCreditTerms? credit,
        CancellationToken cancellationToken)
    {
        var selections = payments.Select(payment => new { payment.MethodCode, payment.PaymentMethodId })
            .Concat(credit?.PaymentMethodId is null ? [] : [new { MethodCode="Credit", credit.PaymentMethodId }]).ToArray();
        if (selections.All(payment => payment.PaymentMethodId is null)) return;
        try
        {
            await ValidateSaleEvidenceAsync(connection, transaction, tenantId,
                JsonSerializer.Serialize(selections), true, cancellationToken);
        }
        catch (Auraly.Application.Sales.PosSaleInvalidException error)
        { throw new Auraly.Application.Sales.OnlineSalesDraftValidationException(error.Message); }
    }

    private static async Task ValidateSaleEvidenceAsync(SqlConnection connection, SqlTransaction transaction,
        Guid tenantId, string payments, bool requireCurrent,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM OPENJSON(@Payments) WITH(
                PaymentMethodId uniqueidentifier,MethodCode nvarchar(32)) payment
              WHERE payment.PaymentMethodId IS NULL OR NOT EXISTS(
                SELECT 1 FROM reference.PaymentMethods form WITH(UPDLOCK,HOLDLOCK)
                JOIN reference.Options type ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method'
                WHERE form.TenantId=@Tenant AND form.PaymentMethodId=payment.PaymentMethodId
                  AND type.Code=payment.MethodCode
                  AND (@Current=0 OR form.IsActive=1 AND form.UseForSales=1 AND type.IsActive=1
                    AND EXISTS(
                      SELECT 1 FROM accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,form.AccountId) ready
                      WHERE ready.SourceType=N'PosPaymentMethod'))))
              THROW 51220,N'La forma de pago de la venta no coincide con la configuración autorizada.',1;
            UPDATE form SET TypeLockedAt=SYSUTCDATETIME()
            FROM reference.PaymentMethods form JOIN OPENJSON(@Payments) WITH(PaymentMethodId uniqueidentifier) input
              ON input.PaymentMethodId=form.PaymentMethodId
            WHERE form.TenantId=@Tenant AND form.TypeLockedAt IS NULL;
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",tenantId);
        command.Parameters.AddWithValue("@Current",requireCurrent);
        command.Parameters.AddWithValue("@Payments",payments);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch(SqlException error) when(error.Number==51220)
        { throw new Auraly.Application.Sales.PosSaleInvalidException(error.Message); }
    }
    public static async Task<IReadOnlyList<PaymentTender>> ResolveAsync(SqlConnection connection,
        SqlTransaction transaction, Guid tenantId, Guid businessId, string usage, IReadOnlyList<PaymentTender> tenders,
        CancellationToken cancellationToken)
    {
        if (tenders.All(tender => tender.PaymentMethodId is null)) return tenders;
        await using var command = new SqlCommand("""
            SELECT form.PaymentMethodId,form.Code,form.Name,type.Code,form.Version,account.BankAccountId
            FROM OPENJSON(@Ids) WITH(Id uniqueidentifier '$') input
            JOIN reference.PaymentMethods form WITH(UPDLOCK,HOLDLOCK) ON form.PaymentMethodId=input.Id AND form.TenantId=@Tenant
            JOIN reference.Options type WITH(HOLDLOCK) ON type.OptionId=form.PaymentTypeId AND type.CatalogCode=N'payment-method' AND type.IsActive=1
            JOIN dbo.AccountingAccounts account WITH(HOLDLOCK) ON account.TenantId=form.TenantId
              AND account.AccountId=form.AccountId AND account.IsActive=1 AND account.AllowsPosting=1
            CROSS APPLY accounting.PaymentMethodAccountReadiness(@Tenant,type.Code,account.AccountId) readiness
            WHERE form.IsActive=1 AND ((@Usage=N'Receipts' AND form.UseForReceipts=1)
              OR (@Usage=N'SupplierPayments' AND form.UseForSupplierPayments=1)
              OR (@Usage=N'Sales' AND form.UseForSales=1) OR (@Usage=N'Payroll' AND form.UseForPayroll=1))
              AND readiness.SourceType=CASE @Usage WHEN N'SupplierPayments' THEN N'SupplierPaymentMethod'
                WHEN N'Sales' THEN N'PosPaymentMethod' ELSE N'CustomerPaymentMethod' END;
            UPDATE form SET TypeLockedAt=SYSUTCDATETIME()
            FROM reference.PaymentMethods form JOIN OPENJSON(@Ids) WITH(Id uniqueidentifier '$') input
              ON input.Id=form.PaymentMethodId
            WHERE form.TenantId=@Tenant AND form.TypeLockedAt IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", tenantId);
        command.Parameters.AddWithValue("@Usage", usage);
        command.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(tenders.Select(tender => tender.PaymentMethodId).Distinct()));
        var forms = new Dictionary<Guid, (string Code,string Name,string Type,int Version,Guid? Bank)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                forms.Add(reader.GetGuid(0),(reader.GetString(1),reader.GetString(2),reader.GetString(3),
                    reader.GetInt32(4),reader.IsDBNull(5)?null:reader.GetGuid(5)));
            await reader.NextResultAsync(cancellationToken);
        }
        return tenders.Select(tender =>
        {
            if (tender.PaymentMethodId is not { } id || !forms.TryGetValue(id, out var form) ||
                form.Type != tender.MethodCode)
                throw new ArgumentException("La forma de pago cambió o no está habilitada. Revisa la selección.");
            return tender with { PaymentMethodVersion=form.Version, PaymentMethodCode=form.Code, PaymentMethodName=form.Name,
                AccountId=null, BankAccountId=form.Type=="Transfer"?form.Bank:null };
        }).ToArray();
    }
}
