using System.Text.Json;
using Auraly.BuildingBlocks.Application.Outbox;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Domain.Sales;
using Auraly.Fiscal.Core;

namespace Auraly.Application.Sales;

public sealed record OfflineSaleLine(
    PosCatalogProduct Product,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal TaxAmount);

public sealed record ConfirmOfflineSaleCommand(
    UserId UserId,
    DocumentId DocumentId,
    RegisterContext Register,
    AuralyDocumentNumberAssignment DocumentNumber,
    FiscalNumberAssignment FiscalNumber,
    DateTimeOffset IssuedAt,
    string SupplierTaxId,
    string CustomerIdentification,
    FiscalTechnicalKey TechnicalKey,
    FiscalEnvironment Environment,
    string QrValidationUrl,
    IReadOnlyCollection<OfflineSaleLine> Lines);

public sealed record ConfirmedOfflineSale(
    SalesInvoice Invoice,
    ConfirmedSale Contract,
    OutboxMessage OutboxMessage);

public sealed class ConfirmOfflineSaleService(IPermissionAuthorizer authorizer)
{
    public ConfirmedOfflineSale Confirm(ConfirmOfflineSaleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        authorizer.Demand(
            command.Register.TenantId,
            command.UserId,
            CommercePermissionCodes.SalesCreate);

        if (command.Lines.Count == 0)
        {
            throw new InvalidOperationException("An offline sale requires at least one line.");
        }

        if (command.Lines.Any(line => line.Discount > 0))
        {
            authorizer.Demand(
                command.Register.TenantId,
                command.UserId,
                CommercePermissionCodes.SalesDiscount);
        }

        var invoice = new SalesInvoice(
            command.DocumentId,
            command.Register.TenantId,
            command.Register.BusinessId,
            command.Register.WarehouseId,
            command.Register.RegisterId);

        foreach (var line in command.Lines)
        {
            if (!line.Product.IsActive)
            {
                throw new InvalidOperationException(
                    $"Product '{line.Product.ProductCode}' is not available for sale.");
            }

            invoice.AddLine(new SalesInvoiceLine(
                line.Product.ProductId,
                line.Product.Name,
                line.Quantity,
                line.UnitPrice,
                line.Discount,
                line.TaxAmount));
        }

        var taxes = command.Lines
            .GroupBy(line => line.Product.TaxCode, StringComparer.Ordinal)
            .Select(group => new FiscalTaxAmount(group.Key, group.Sum(line => line.TaxAmount)))
            .ToArray();
        var cufe = CufeCalculator.Calculate(
            new CufeInput(
                command.FiscalNumber.FullNumber,
                command.IssuedAt,
                invoice.UntaxedAmount,
                invoice.PayableAmount,
                command.SupplierTaxId,
                command.CustomerIdentification,
                command.TechnicalKey,
                command.Environment,
                taxes),
            command.QrValidationUrl);
        var snapshot = new ImmutableFiscalSnapshot(
            command.FiscalNumber.FullNumber,
            command.FiscalNumber.Prefix,
            command.FiscalNumber.Consecutive,
            command.FiscalNumber.AuthorizationNumber,
            command.IssuedAt,
            command.CustomerIdentification,
            invoice.UntaxedAmount,
            invoice.TaxAmount,
            invoice.PayableAmount,
            cufe.Cufe,
            cufe.QrPayload);

        invoice.ConfirmOffline(command.DocumentNumber, snapshot);

        var contract = new ConfirmedSale(
            command.Register.TenantId,
            command.Register.BusinessId,
            command.Register.WarehouseId,
            command.Register.RegisterId,
            command.DocumentId,
            command.DocumentNumber.FullNumber,
            command.FiscalNumber.FullNumber,
            cufe.Cufe,
            invoice.PayableAmount,
            command.IssuedAt);
        var outbox = new OutboxMessage(
            Guid.NewGuid(),
            command.Register.TenantId,
            "sales.invoice.confirmed",
            JsonSerializer.Serialize(contract),
            command.IssuedAt);

        return new ConfirmedOfflineSale(invoice, contract, outbox);
    }
}
