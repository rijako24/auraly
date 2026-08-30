export type PosBarcodeCapture =
  | { valid: true; code: string; quantity: number }
  | { valid: false; message: string };

type PosCaptureEnterEvent = {
  key: string;
  preventDefault: () => void;
  currentTarget: { form: { requestSubmit: () => void } | null };
};

export function submitPosCaptureOnEnter(event: PosCaptureEnterEvent): boolean {
  if (event.key !== "Enter") return false;
  event.preventDefault();
  event.currentTarget.form?.requestSubmit();
  return true;
}

export function parsePosBarcodeCapture(raw: string): PosBarcodeCapture {
  const value = raw.trim();
  if (!value) return { valid: false, message: "Escribe o escanea un código." };
  if (!value.includes("*")) return { valid: true, code: value, quantity: 1 };
  const match = /^(\d+(?:[.,]\d+)?)\s*\*\s*([^*]+?)\s*$/.exec(value);
  if (!match) return { valid: false, message: "Usa el formato cantidad*código, por ejemplo 3*7701234567890." };
  const quantity = Number(match[1].replace(",", "."));
  if (!Number.isFinite(quantity) || quantity <= 0)
    return { valid: false, message: "La cantidad antes del asterisco debe ser mayor que cero." };
  return { valid: true, code: match[2].trim(), quantity };
}
