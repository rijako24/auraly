import { getBackendUrl } from "./backend-url";

export function getBackendRequestUrl(
  path: string,
  searchParams = "",
): string {
  const normalizedPath = path.replace(/^\/+/, "");
  const suffix = searchParams ? `?${searchParams}` : "";
  const backend = getBackendUrl().replace(/\/+$/, "");

  if (normalizedPath === "health") {
    const root = backend.replace(/\/api$/i, "");
    return `${root}/health${suffix}`;
  }

  const isModuleVersioned = /^(commerce|pos)\/v\d+(?:\/|$)/i.test(normalizedPath);
  const versionedPath = isModuleVersioned ? normalizedPath : `v1/${normalizedPath}`;
  return `${backend}/${versionedPath}${suffix}`;
}
