import type {
  InvoiceOrdersResponse,
} from "@/services/orders/commerce-orders-client";
import type { PosPrintableReceipt } from "./pos-edge-client";
import { resolvePosPrintRoute, type PosPrintRoute } from "./pos-print-routing";

export type PosOrderPrintRoute = PosPrintRoute;

export type OrderInvoiceSequenceProgress = {
  total: number;
  processed: number;
  completed: number;
  failed: number;
  currentOrderId: string;
  currentOrderNumber: string;
  phase: "invoicing" | "printing" | "completed";
  printed?: boolean;
  printError?: string | null;
};

type OrderInvoiceSequenceOptions = {
  invoiceOne: (orderId: string, idempotencyKey: string) => Promise<InvoiceOrdersResponse>;
  printOne?: (receipts: PosPrintableReceipt[]) => Promise<void>;
  onProgress?: (progress: OrderInvoiceSequenceProgress) => void;
};

export function resolvePosOrderPrintRoute(
  edgeSessionToken: string | null,
): PosOrderPrintRoute {
  return resolvePosPrintRoute(edgeSessionToken);
}

export function orderReceiptsFromEmission<T>(
  results: ReadonlyArray<{ receipt?: T | null }>,
): T[] {
  return results.flatMap((result) => result.receipt ? [result.receipt] : []);
}

export function orderReceiptsForPrinting<T extends { creditAcknowledgement?: unknown }>(
  receipts: readonly T[],
  includeCreditAcknowledgement: boolean,
): T[] {
  return receipts.map(receipt => includeCreditAcknowledgement
    ? { ...receipt }
    : { ...receipt, creditAcknowledgement: undefined });
}

export function orderInvoiceIdempotencyKey(batchKey: string, orderId: string) {
  const suffix = `:${orderId}`;
  return `${batchKey.slice(0, Math.max(0, 100 - suffix.length))}${suffix}`;
}

export async function invoiceOrdersInSequence(
  orderIds: string[],
  batchIdempotencyKey: string,
  options: OrderInvoiceSequenceOptions,
): Promise<InvoiceOrdersResponse> {
  const results: InvoiceOrdersResponse["results"] = [];
  const creditValidationIssues: NonNullable<InvoiceOrdersResponse["creditValidationIssues"]> = [];
  const printErrors: string[] = [];
  let operationId = "";
  let completed = 0;
  let failed = 0;
  let processed = 0;
  let printed = 0;
  let replayed = false;

  for (const orderId of orderIds) {
    options.onProgress?.({
      total: orderIds.length,
      processed,
      completed,
      failed,
      currentOrderId: orderId,
      currentOrderNumber: orderId,
      phase: "invoicing",
    });
    const response = await options.invoiceOne(
      orderId,
      orderInvoiceIdempotencyKey(batchIdempotencyKey, orderId),
    );
    operationId ||= response.operationId;
    replayed ||= response.isReplay;
    if (response.creditValidationIssues?.length) {
      creditValidationIssues.push(...response.creditValidationIssues);
      break;
    }

    results.push(...response.results);
    completed += response.completedCount;
    failed += response.failedCount;
    processed += response.completedCount + response.failedCount;
    const result = response.results[0];
    const orderNumber = result?.orderNumber ?? orderId;
    let printError: string | null = null;
    const receipts = orderReceiptsFromEmission(response.results);
    if (options.printOne && receipts.length > 0) {
      options.onProgress?.({
        total: orderIds.length,
        processed: processed - response.completedCount - response.failedCount,
        completed: completed - response.completedCount,
        failed: failed - response.failedCount,
        currentOrderId: orderId,
        currentOrderNumber: orderNumber,
        phase: "printing",
      });
      try {
        await options.printOne(receipts);
        printed += receipts.length;
      } catch (error) {
        printError = error instanceof Error ? error.message : "No fue posible imprimir el documento.";
        printErrors.push(`${orderNumber}: ${printError}`);
      }
    }
    options.onProgress?.({
      total: orderIds.length,
      processed,
      completed,
      failed,
      currentOrderId: orderId,
      currentOrderNumber: orderNumber,
      phase: "completed",
      printed: options.printOne !== undefined && receipts.length > 0 && printError === null,
      printError,
    });
  }

  return {
    operationId,
    status: creditValidationIssues.length > 0
      ? "CreditRejected"
      : failed === 0 && processed === orderIds.length
        ? "Completed"
        : completed === 0
          ? "Failed"
          : "PartiallyCompleted",
    requestedCount: orderIds.length,
    completedCount: completed,
    failedCount: failed,
    isReplay: replayed,
    results,
    printStatus: options.printOne
      ? printErrors.length > 0
        ? "Failed"
        : printed > 0
          ? "Sent"
          : "NotRequired"
      : "NotRequired",
    printError: printErrors.length > 0 ? printErrors.join(" · ") : null,
    creditValidationIssues: creditValidationIssues.length > 0 ? creditValidationIssues : null,
  };
}
