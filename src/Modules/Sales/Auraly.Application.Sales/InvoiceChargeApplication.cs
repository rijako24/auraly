using Auraly.BuildingBlocks.Domain.Money;
using Auraly.Contracts.Sales;
using Auraly.Domain.Sales;

namespace Auraly.Application.Sales;

public static class InvoiceChargeApplication
{
    public const int MaximumChargesPerInvoice = 10;

    public static void ValidateSnapshot(decimal productTotal, IReadOnlyList<AppliedInvoiceCharge>? charges)
    {
        if (charges is null) return;
        if (charges.Any(charge => charge is null || charge.Supplier is null) ||
            charges.Count > MaximumChargesPerInvoice ||
            charges.Select(charge => charge.AppliedChargeId).Distinct().Count() != charges.Count)
            throw new InvoiceChargeValidationException("El documento contiene cargos repetidos o excede el límite.");
        foreach (var charge in charges)
        {
            var amounts = new[] { charge.Amount, charge.InvoicedAmount, charge.ExpenseAmount,
                charge.InvoicedUntaxedAmount, charge.InvoicedTaxAmount, charge.SupplierUntaxedAmount, charge.SupplierVatAmount };
            if (charge.AppliedChargeId == Guid.Empty || charge.ChargeId == Guid.Empty || charge.Version < 1 ||
                charge.Supplier.SupplierId == Guid.Empty || charge.InvoiceBase != productTotal ||
                amounts.Any(amount => amount < 0 || amount != MonetaryRounding.RoundLineAmount(amount)) ||
                charge.InvoicedAmount + charge.ExpenseAmount != charge.Amount ||
                (charge.InvoicedAmount != 0 && charge.ExpenseAmount != 0) ||
                charge.InvoicedUntaxedAmount + charge.InvoicedTaxAmount != charge.InvoicedAmount ||
                charge.SupplierUntaxedAmount + charge.SupplierVatAmount != charge.Amount)
                throw new InvoiceChargeValidationException("El cargo no coincide con los importes congelados de la factura.");
        }
    }

    public static void ValidateSnapshotReferences(decimal productTotal,
        IReadOnlyList<AppliedInvoiceCharge> charges, IReadOnlyList<InvoiceChargeDefinition> definitions)
    {
        ValidateSnapshot(productTotal, charges);
        var byVersion = definitions.ToDictionary(definition => (definition.ChargeId, definition.Version));
        foreach (var charge in charges)
        {
            if (!byVersion.TryGetValue((charge.ChargeId, charge.Version), out var definition))
                throw new InvoiceChargeValidationException("La versión del cargo no pertenece a esta sede.");
            var supplier = definition.Suppliers.SingleOrDefault(value =>
                value.SupplierId == charge.Supplier.SupplierId);
            var supplierMatches = supplier is not null &&
                charge.Supplier with { TaxResponsibilities = supplier.TaxResponsibilities } == supplier &&
                (charge.Supplier.TaxResponsibilities ?? []).SequenceEqual(
                    supplier.TaxResponsibilities ?? []);
            if (!supplierMatches ||
                charge.Code != definition.Code || charge.Name != definition.Name ||
                charge.ExpenseConceptId != definition.ExpenseConceptId ||
                charge.ExpenseAccountId != definition.ExpenseAccountId ||
                charge.CostCenterId != definition.CostCenterId ||
                charge.WithholdingConceptCode != definition.WithholdingConceptCode ||
                charge.TaxCode != definition.TaxCode || charge.TaxRate != definition.TaxRate)
                throw new InvoiceChargeValidationException("El cargo emitido difiere de su configuración versionada.");
        }
    }

    public static IReadOnlyList<AppliedInvoiceCharge> Calculate(decimal productTotal,
        IReadOnlyList<InvoiceChargeSelection> selections)
    {
        if (selections.Count > MaximumChargesPerInvoice ||
            selections.Select(x => x.AppliedChargeId).Distinct().Count() != selections.Count)
            throw new InvoiceChargeValidationException("Se admiten hasta diez cargos por factura.");
        return selections.Select(selection => Calculate(productTotal, selection)).ToArray();
    }

    public static AppliedInvoiceCharge Calculate(decimal productTotal, InvoiceChargeSelection selection)
    {
        var definition = selection.Definition;
        if (selection.AppliedChargeId == Guid.Empty || definition.ChargeId == Guid.Empty || definition.Version < 1 ||
            !definition.IsActive || definition.TaxRate is < 0 or > 100 || definition.PurchaseTaxRate is < 0 or > 100)
            throw new InvoiceChargeValidationException("La configuración del cargo no es válida.");
        var supplier = definition.Suppliers.SingleOrDefault(x => x.SupplierId == selection.SupplierId && x.IsActive)
            ?? throw new InvoiceChargeValidationException("Selecciona un proveedor activo habilitado para este cargo.");
        InvoiceChargeAmounts amounts;
        try
        {
            amounts = InvoiceChargeCalculation.Calculate(new(definition.CalculationMode, definition.Value,
                definition.InclusionMode, definition.InvoiceAmountLimit, definition.Ranges.Select(x =>
                    new InvoiceChargeRange(x.FromInclusive, x.ToExclusive, x.CalculationMode, x.Value)).ToArray()),
                productTotal, selection.ManualAmount);
        }
        catch (InvoiceChargeRuleException error) { throw new InvoiceChargeValidationException(error.Message); }
        var customerNet = MonetaryRounding.RoundLineAmount(amounts.InvoicedAmount / (1m + definition.TaxRate / 100m));
        var supplierNet = MonetaryRounding.RoundLineAmount(amounts.Amount / (1m + definition.PurchaseTaxRate / 100m));
        return new(selection.AppliedChargeId, definition.ChargeId, definition.Version, definition.Code,
            definition.Name, productTotal, amounts.Amount, amounts.InvoicedAmount, amounts.ExpenseAmount,
            customerNet, amounts.InvoicedAmount - customerNet, definition.TaxCode, definition.TaxRate,
            supplierNet, amounts.Amount - supplierNet, definition.ExpenseConceptId, definition.ExpenseAccountId,
            definition.CostCenterId, definition.WithholdingConceptCode, supplier, selection.ManualAmount);
    }
}
