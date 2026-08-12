export function shouldIncludeExecutionContext(path: string): boolean {
  return !path.replace(/^\/+/, "").startsWith("auth/");
}