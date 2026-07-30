export function buildLoginRedirect(pathname: string, search = ""): string {
  const target = `${pathname || "/"}${search}`;
  return `/login?redirect=${encodeURIComponent(
    target.startsWith("/") ? target : "/",
  )}`;
}
