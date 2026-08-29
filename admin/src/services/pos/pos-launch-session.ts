export type PosLaunchHealth = {
  status: string;
  identityReady: boolean;
};

export function usesEnrolledPosRuntime(health: PosLaunchHealth) {
  return health.status !== "EnrollmentRequired";
}

export function installedPosLaunchDestination(health: PosLaunchHealth | null) {
  return health && !usesEnrolledPosRuntime(health)
    ? "/login?redirect=%2Fpos"
    : "/pos";
}
