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
  const configuredPrinter = (format: PosPrintTemplateFormat) =>
    configuration.templateRoutes?.find((route) => route.format === format)?.printerName ??
    (format === "Receipt"
      ? configuration.receiptPrinterName
      : configuration.letterPrinterName) ??
    onlyInstalledPrinter;
  return {
    ...configuration,
    posPrinterName: configuration.posPrinterName ??
      configuredPrinter(configuration.posOutputFormat ?? "Receipt"),
    orderPrinterName: configuration.orderPrinterName ??
      configuredPrinter(configuration.orderOutputFormat ?? "HalfLetter"),
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
