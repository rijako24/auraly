import type {
  PosPrintTemplateFormat,
  PosPrinterConfiguration,
} from "./pos-edge-client";

export function completeInstalledPrinterConfiguration(
  configuration: PosPrinterConfiguration,
  installedPrinters: string[],
): PosPrinterConfiguration {
  const onlyInstalledPrinter = installedPrinters.length === 1
    ? installedPrinters[0]
    : null;
  const configuredPrinter = (documentType: "SalesInvoice" | "Order", format: PosPrintTemplateFormat) =>
    configuration.templateRoutes?.find((route) => route.documentType === documentType && route.format === format)?.printerName ??
    (format === "Receipt"
      ? configuration.receiptPrinterName
      : configuration.letterPrinterName) ??
    onlyInstalledPrinter;
  const posPrinterName = configuration.posPrinterName ??
    configuredPrinter("SalesInvoice", configuration.posOutputFormat ?? "Receipt");
  const orderPrinterName = configuration.orderPrinterName ??
    configuredPrinter("Order", configuration.orderOutputFormat ?? "HalfLetter");
  const templateRoutes = [...configuration.templateRoutes ?? []];
  for (const documentType of ["SalesInvoice", "SalesReceipt"] as const) {
    if (!templateRoutes.some(route => route.documentType === documentType && route.format === configuration.posOutputFormat))
      templateRoutes.push({ documentType, format: configuration.posOutputFormat, printerName: posPrinterName });
  }
  if (!templateRoutes.some(route => route.documentType === "Order" && route.format === configuration.orderOutputFormat))
    templateRoutes.push({ documentType: "Order", format: configuration.orderOutputFormat, printerName: orderPrinterName });
  return {
    ...configuration,
    templateRoutes,
    posPrinterName,
    orderPrinterName,
  };
}

export function readPosEdgeProblem(
  raw: string,
  fallback: string,
): { detail: string; code?: string } {
  try {
    const problem = JSON.parse(raw) as {
      detail?: string;
      title?: string;
      code?: string;
      errors?: Record<string, string[]>;
    };
    const validation = Object.values(problem.errors ?? {}).flat().find(Boolean);
    return {
      detail: problem.detail || validation || problem.title || raw || fallback,
      code: problem.code || problem.title,
    };
  } catch {
    return { detail: raw || fallback };
  }
}
