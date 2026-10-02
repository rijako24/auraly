using System.Data;
using System.Text.Json;
using Auraly.Application.Expenses;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    public async Task<ExpenseResolution> ResolveAsync(ExpenseUserIdentity user,
        ConfirmExpenseRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        return await ResolveAsync(connection, null, user, request, ct);
    }

    private static async Task<ExpenseResolution> ResolveAsync(SqlConnection connection, SqlTransaction? transaction,
        ExpenseUserIdentity user, ConfirmExpenseRequest request, CancellationToken ct)
    {
        if (request.Lines is not { Count: >= 1 and <= 100 })
            throw new ExpenseValidationException("El gasto requiere entre 1 y 100 líneas.");
        await using var command = new SqlCommand("""
            SELECT s.Name,s.PurchaseEvidencePolicy FROM dbo.Suppliers s
            JOIN dbo.Businesses b ON b.TenantId=s.TenantId AND b.BusinessId=@BusinessId
            WHERE s.SupplierId=@SupplierId AND s.TenantId=@TenantId AND s.IsActive=1;
            SELECT l.LineNumber,a.AccountId,a.Code,a.Name,c.ExpenseConceptId,
              cc.CostCenterId,cc.Name,l.Description,l.TaxExclusiveAmount,t.TaxProfileId,t.Name,t.Rate,
              l.TaxTreatment,l.WithholdingConceptCode
            FROM OPENJSON(@Lines) WITH(LineNumber int,ExpenseAccountId uniqueidentifier,
              ConceptId uniqueidentifier,CostCenterId uniqueidentifier,Description nvarchar(300),
              TaxExclusiveAmount decimal(19,4),TaxProfileId uniqueidentifier,
              TaxTreatment nvarchar(32),WithholdingConceptCode nvarchar(32)) l
            JOIN dbo.AccountingAccounts a ON a.AccountId=l.ExpenseAccountId AND a.TenantId=@TenantId
              AND a.IsActive=1 AND a.AllowsPosting=1 AND a.AccountType=N'Expense'
            LEFT JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=l.ConceptId AND c.BusinessId=@BusinessId AND c.IsActive=1
            LEFT JOIN dbo.AccountingCostCenters cc ON cc.CostCenterId=l.CostCenterId AND cc.BusinessId=@BusinessId AND cc.IsActive=1
            LEFT JOIN dbo.TaxProfiles t ON t.TaxProfileId=l.TaxProfileId AND t.TenantId=@TenantId AND t.IsActive=1 AND t.DianTaxCode=N'01'
            WHERE (l.ConceptId IS NULL OR c.ExpenseConceptId IS NOT NULL)
              AND (l.CostCenterId IS NULL OR cc.CostCenterId IS NOT NULL)
              AND (l.TaxProfileId IS NULL OR t.TaxProfileId IS NOT NULL)
              AND (l.WithholdingConceptCode IS NULL OR EXISTS(
                SELECT 1 FROM dbo.WithholdingRules r WHERE r.BusinessId=@BusinessId
                  AND r.Direction=N'Purchase' AND r.Moment=N'Accrual' AND r.IsActive=1
                  AND r.ConceptCode=l.WithholdingConceptCode
                  AND NOT EXISTS(SELECT 1 FROM dbo.WithholdingRules newer WHERE newer.RuleId=r.RuleId AND newer.Version>r.Version))
                OR EXISTS(SELECT 1 FROM dbo.ExpenseConcepts configured WHERE configured.BusinessId=@BusinessId
                  AND configured.IsActive=1 AND configured.WithholdingConceptCode=l.WithholdingConceptCode))
            ORDER BY l.LineNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            request.Lines.Select((line, index) => new { LineNumber = index + 1, line.ExpenseAccountId,
                line.ConceptId, line.CostCenterId, Description = line.Description.Trim(), line.TaxExclusiveAmount,
                line.TaxProfileId, line.TaxTreatment,
                WithholdingConceptCode = string.IsNullOrWhiteSpace(line.WithholdingConceptCode) ? null : line.WithholdingConceptCode.Trim().ToUpperInvariant() }));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ExpenseValidationException("El proveedor no está activo o no pertenece al tenant.");
        var name = reader.GetString(0);
        var policy = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (!PurchaseEvidenceTypes.AllowedFor(policy).Contains(request.PurchaseEvidenceType) ||
            request.PurchaseEvidenceType is not (PurchaseEvidenceTypes.SupplierElectronicInvoice or
                PurchaseEvidenceTypes.BuyerElectronicSupportDocument or PurchaseEvidenceTypes.InternalReceiptVoucher))
            throw new ExpenseValidationException("El respaldo elegido no está permitido para este gasto y proveedor.");
        await reader.NextResultAsync(ct);
        var lines = new List<ExpenseLineSnapshot>();
        while (await reader.ReadAsync(ct))
        {
            var basis = reader.GetDecimal(8);
            var rate = reader.IsDBNull(11) ? 0 : reader.GetDecimal(11);
            var vat = decimal.Round(basis * rate / 100m, 4, MidpointRounding.AwayFromZero);
            var treatment = reader.GetString(12);
            if (vat > 0 && request.PurchaseEvidenceType == PurchaseEvidenceTypes.InternalReceiptVoucher &&
                treatment == PurchasingTaxTreatments.DeductibleInputVat)
                throw new ExpenseValidationException("Un comprobante interno no admite IVA descontable. Selecciona mayor valor del gasto o un respaldo fiscal válido.");
            lines.Add(new(reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), basis,
                reader.IsDBNull(9) ? null : reader.GetGuid(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                rate, vat, treatment, reader.IsDBNull(13) ? null : reader.GetString(13)));
        }
        if (lines.Count != request.Lines.Count)
            throw new ExpenseValidationException("Revisa las líneas: la cuenta, plantilla, centro, impuesto o concepto tributario no existe, está inactivo o pertenece a otra empresa.");
        return new(name, policy, lines);
    }
}
