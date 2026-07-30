export function buildBackendProxyHeaders(
  requestHeaders: Headers,
  accessToken?: string,
): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Type": requestHeaders.get("Content-Type") || "application/json",
  };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;

  for (const name of ["X-Business-Id", "Idempotency-Key", "X-Correlation-Id"]) {
    const value = requestHeaders.get(name);
    if (value) headers[name] = value;
  }

  return headers;
}
