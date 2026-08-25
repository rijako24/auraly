const functionKey = /^F(?:[1-9]|1[0-2])$/;

const hardwareAliases: Readonly<Record<string, string>> = {
  // Some Windows keyboards expose the physical F2 position as the generic
  // "My Computer" application key while action-key mode is enabled.
  LaunchApplication1: "F2",
  LaunchApp1: "F2",
};

export function resolvePosFunctionShortcut(
  key: string,
  code: string,
  legacyKeyCode = 0,
): string {
  if (functionKey.test(code)) return code;
  if (functionKey.test(key)) return key;

  const hardwareAlias = hardwareAliases[code] ?? hardwareAliases[key];
  if (hardwareAlias) return hardwareAlias;

  if (legacyKeyCode >= 112 && legacyKeyCode <= 123) {
    return `F${legacyKeyCode - 111}`;
  }

  return "";
}

type PosFunctionKeyboardEvent = Pick<
  KeyboardEvent,
  "key" | "code" | "keyCode" | "preventDefault" | "stopImmediatePropagation"
>;

export function capturePosFunctionShortcut(
  event: PosFunctionKeyboardEvent,
  onShortcut: (shortcut: string) => void,
): boolean {
  const shortcut = resolvePosFunctionShortcut(event.key, event.code, event.keyCode);
  if (!functionKey.test(shortcut)) return false;

  event.preventDefault();
  event.stopImmediatePropagation();
  onShortcut(shortcut);
  return true;
}
