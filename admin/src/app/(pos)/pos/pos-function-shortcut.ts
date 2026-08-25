const functionKey = /^F(?:[1-9]|1[0-2])$/;

export function resolvePosFunctionShortcut(key: string, code: string): string {
  if (functionKey.test(code)) return code;
  return key;
}
