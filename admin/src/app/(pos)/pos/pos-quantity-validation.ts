export type PosQuantityRules = {
  allowsFractionalSale: boolean;
  managesInventory: boolean;
  maximumQuantity?: number;
};

export type PosQuantityValidation =
  | { valid: true; quantity: number }
  | { valid: false; reason: "required" | "positive" | "whole-units" | "inventory-limit" };

export function validatePosQuantity(
  rawQuantity: string | number,
  rules: PosQuantityRules,
): PosQuantityValidation {
  if (rawQuantity === "") return { valid: false, reason: "required" };
  const quantity = typeof rawQuantity === "number" ? rawQuantity : Number(rawQuantity);
  if (!Number.isFinite(quantity) || quantity <= 0)
    return { valid: false, reason: "positive" };
  if (!rules.allowsFractionalSale && !Number.isInteger(quantity))
    return { valid: false, reason: "whole-units" };
  if (rules.managesInventory && rules.maximumQuantity !== undefined && quantity > rules.maximumQuantity)
    return { valid: false, reason: "inventory-limit" };
  return { valid: true, quantity };
}

export function blocksPosQuantityKey(key: string, rules: PosQuantityRules): boolean {
  if (["e", "E", "+", "-"].includes(key)) return true;
  return !rules.allowsFractionalSale && [".", ","].includes(key);
}

export function acceptsPosQuantityDraft(rawQuantity: string, rules: PosQuantityRules): boolean {
  if (rawQuantity === "") return true;
  return rules.allowsFractionalSale
    ? /^\d*(?:[.,]\d*)?$/.test(rawQuantity)
    : /^\d+$/.test(rawQuantity);
}
