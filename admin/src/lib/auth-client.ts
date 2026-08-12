import { randomUUID } from "node:crypto";

export const AUTHENTICATION_CLIENT_ID_HEADER = "X-Auraly-Client-Id";

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function resolveAuthenticationClientId(
  current: string | undefined,
  generate: () => string = randomUUID,
): string {
  if (current && GUID_PATTERN.test(current)) return current;

  const generated = generate();
  if (!GUID_PATTERN.test(generated)) {
    throw new Error("The authentication client identifier is not a valid UUID.");
  }
  return generated;
}
