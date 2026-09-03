export type CashMovementKeyboardAction =
  | "native"
  | "next"
  | "previous"
  | "submit";

export function cashMovementKeyboardAction(input: {
  key: string;
  control: "select" | "textarea" | "field" | "button";
}): CashMovementKeyboardAction {
  if (input.control === "select" || input.control === "textarea" || input.control === "button")
    return "native";
  if (input.key === "ArrowDown") return "next";
  if (input.key === "ArrowUp") return "previous";
  if (input.key === "Enter") return "submit";
  return "native";
}
