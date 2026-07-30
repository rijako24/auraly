import { getBackendUrl } from "./backend-url";

const COMMERCE_PATH_PREFIX = "commerce/";

export function getBackendRequestUrl(
  path: string,
  searchParams = "",
): string {
  const normalizedPath = path.replace(/^\/+/, "");
  const commerceBackend = process.env.AURALY_COMMERCE_API_URL?.trim();
  const suffix = searchParams ? `?${searchParams}` : "";

  if (
    commerceBackend &&
    (normalizedPath === "health" ||
      normalizedPath.startsWith(COMMERCE_PATH_PREFIX))
  ) {
    const root = commerceBackend.replace(/\/+$/, "");
    const upstreamPath =
      normalizedPath === "health"
        ? "health"
        : `api/${normalizedPath}`;
    return `${root}/${upstreamPath}${suffix}`;
  }

  return `${getBackendUrl()}/${normalizedPath}${suffix}`;
}
