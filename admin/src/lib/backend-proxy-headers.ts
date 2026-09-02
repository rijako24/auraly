export function buildBackendProxyHeaders(
  requestHeaders: Headers,
  accessToken?: string,
  authenticationClientId?: string,
): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Type": requestHeaders.get("Content-Type") || "application/json",
  };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  if (authenticationClientId) {
    headers["X-Auraly-Client-Id"] = authenticationClientId;
  }

  for (const name of [
    "X-Tenant-Id",
    "X-Business-Id",
    "Idempotency-Key",
    "X-Correlation-Id",
    "X-Auraly-Draft-Id",
    "X-Auraly-Approval-Id",
    "X-Auraly-Operation-Id",
  ]) {
    const value = requestHeaders.get(name);
    if (value) headers[name] = value;
  }

  return headers;
}
