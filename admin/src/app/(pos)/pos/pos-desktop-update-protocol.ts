export type DesktopUpdateStatusName =
  | "available"
  | "downloading"
  | "verifying"
  | "ready"
  | "deferred"
  | "error";

export type DesktopUpdateStatus = {
  type: "auraly-pos-update-status";
  status: DesktopUpdateStatusName;
  version: string | null;
  progress: number | null;
  message: string;
};

const statuses = new Set<DesktopUpdateStatusName>([
  "available",
  "downloading",
  "verifying",
  "ready",
  "deferred",
  "error",
]);

export function isDesktopUpdateStatus(value: unknown): value is DesktopUpdateStatus {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<DesktopUpdateStatus>;
  return (
    candidate.type === "auraly-pos-update-status" &&
    typeof candidate.status === "string" &&
    statuses.has(candidate.status as DesktopUpdateStatusName) &&
    typeof candidate.message === "string" &&
    (candidate.version === null || typeof candidate.version === "string") &&
    (candidate.progress === null || typeof candidate.progress === "number")
  );
}

export function desktopUpdateAction(action: "download" | "restart" | "later") {
  return `auraly-pos-update-${action}` as const;
}
