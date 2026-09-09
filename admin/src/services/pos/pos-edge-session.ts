export function isCurrentEdgeUserSession(
  requestSessionToken: string | null,
  currentSessionToken: string | null,
): boolean {
  return requestSessionToken !== null && requestSessionToken === currentSessionToken;
}
