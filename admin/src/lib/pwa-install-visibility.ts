export function shouldOfferPwaInstall(
  isAuthenticated: boolean,
  pathname: string,
): boolean {
  return isAuthenticated && (pathname === "/dashboard" || pathname.startsWith("/dashboard/"));
}
