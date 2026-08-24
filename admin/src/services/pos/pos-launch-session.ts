export type PosLaunchHealth = {
  status: string;
  identityReady: boolean;
};

export function clearInstalledPosUserSession(storage: Pick<Storage, "removeItem">) {
  storage.removeItem("auraly.pos.user-session");
}

export function installedPosLaunchDestination(health: PosLaunchHealth | null) {
  return health && health.status !== "EnrollmentRequired" && health.identityReady
    ? "/pos"
    : "/login?redirect=%2Fpos";
}
