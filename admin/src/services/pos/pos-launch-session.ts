export type PosLaunchHealth = {
  status: string;
  identityReady: boolean;
};

export function usesEnrolledPosRuntime(health: PosLaunchHealth) {
  return health.status !== "EnrollmentRequired";
}

export function installedPosLaunchDestination(health: PosLaunchHealth | null) {
  void health;
  return "/login";
}

export function shouldFallbackToLocalPos(
  error: unknown,
  navigatorOnline: boolean,
): boolean {
  if (!navigatorOnline) return true;
  if (!(error instanceof Error)) return false;
  const statusCode = (error as Error & { statusCode?: unknown }).statusCode;
  if (typeof statusCode === "number") return statusCode >= 500;
  return error.name === "AbortError" || error.name === "TimeoutError" ||
    error.name === "TypeError" || /failed to fetch|network|conexi[oó]n/i.test(error.message);
}
