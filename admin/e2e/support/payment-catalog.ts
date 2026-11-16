import type { PaymentMethodCatalogSnapshot } from "../../src/services/api/payment-methods";

// Incoming card clearing and outgoing corporate cards have different uses.
export function paymentCatalogFixture(names: Readonly<Record<string, string>> = {}, outgoing = false): PaymentMethodCatalogSnapshot {
  const labels = { Cash: "Efectivo", DebitCard: "Tarjeta débito", CreditCard: "Tarjeta crédito", Transfer: "Transferencia" };
  return { contractVersion: 2, forms: Object.entries(labels).map(([methodCode, name], index) => ({
    paymentMethodId: `10000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`,
    code: `FORM-${methodCode}`, name: names[methodCode] ?? name, methodCode, typeLabel: name, paymentTypeId: `type-${index}`,
    accountId: `account-${index}`, accountCode: `account-${index}`, accountName: name,
    bankAccountId: methodCode === "Transfer" || outgoing && methodCode !== "Cash" ? `bank-${methodCode}` : null,
    useForSales: !outgoing || methodCode === "Cash" || methodCode === "Transfer",
    useForReceipts: !outgoing || methodCode === "Cash" || methodCode === "Transfer",
    useForSupplierPayments: outgoing || methodCode === "Cash" || methodCode === "Transfer",
    useForPayroll: methodCode === "Cash" || methodCode === "Transfer",
    useForVouchers: true, isActive: true, sortOrder: index,
    version: 1, rowVersion: "AAAAAAAAAAE=",
  })) };
}
