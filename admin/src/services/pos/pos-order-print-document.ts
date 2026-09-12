import type { PosPrintableReceipt } from "./pos-edge-client";

export type PrintableOrderDocument = {
  orderId: string;
  orderNumber: string;
  createdAt: string;
  customerName?: string | null;
  customerIdentification?: string | null;
  total: number;
  lines: Array<{
    productCode?: string | null;
    productName: string;
    quantity: number;
    unitPrice: number;
    discountAmount: number;
    lineTotal: number;
  }>;
};

export function toPrintableOrder(
  order: PrintableOrderDocument,
  context: { businessName?: string | null; warehouseName?: string | null },
): PosPrintableReceipt {
  return {
    documentId: order.orderId,
    documentType: "Order",
    documentNumber: order.orderNumber,
    fiscalNumber: null,
    issuedAt: order.createdAt,
    customerIdentification: order.customerIdentification ?? "",
    customerName: order.customerName ?? "Cliente",
    lines: order.lines.map((line) => ({
      productCode: line.productCode ?? "",
      description: line.productName,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      discount: line.discountAmount,
      tax: 0,
      total: line.lineTotal,
      taxCode: "",
      taxRate: 0,
    })),
    payments: [],
    untaxedAmount: order.total,
    taxAmount: 0,
    payableAmount: order.total,
    cufe: null,
    qrPayload: null,
    fiscalStatus: "NotApplicable",
    withholdingTotal: 0,
    netPayableAmount: order.total,
    withholdings: [],
    businessName: context.businessName ?? null,
    warehouseName: context.warehouseName ?? null,
  };
}
