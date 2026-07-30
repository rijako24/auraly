export type PosDiscountMode = "value" | "percentage";

export type PosDiscountCalculation = {
  discount: number;
  percentage: number;
  net: number;
  tax: number;
  total: number;
};

const roundMoney = (value: number) => Math.round(value * 100) / 100;

export function calculatePosDiscount(
  mode: PosDiscountMode,
  input: number,
  gross: number,
  taxRate: number,
): PosDiscountCalculation | null {
  if (
    !Number.isFinite(input) ||
    !Number.isFinite(gross) ||
    !Number.isFinite(taxRate) ||
    input < 0 ||
    gross < 0 ||
    taxRate < 0
  ) return null;

  if (mode === "percentage" && input > 100) return null;
  const discount = roundMoney(
    mode === "percentage" ? gross * input / 100 : input,
  );
  if (discount > gross) return null;

  const net = roundMoney(gross - discount);
  const tax = roundMoney(net * taxRate / 100);
  return {
    discount,
    percentage: gross === 0 ? 0 : roundMoney(discount * 100 / gross),
    net,
    tax,
    total: roundMoney(net + tax),
  };
}
