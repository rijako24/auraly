import type { FiscalHabilitationAttempt } from "./fiscal-configuration";

type WebPubSubEnvelope = {
  type?: string;
  data?: { stream?: string; Stream?: string } | string;
};

export function isFiscalStatusSynchronizationMessage(raw: string) {
  try {
    const envelope = JSON.parse(raw) as WebPubSubEnvelope;
    const data = typeof envelope.data === "string"
      ? JSON.parse(envelope.data) as { stream?: string; Stream?: string }
      : envelope.data;
    return envelope.type === "message"
      && (data?.stream ?? data?.Stream) === "FiscalStatus";
  } catch {
    return false;
  }
}

export function habilitationFeedbackKind(
  attempt: FiscalHabilitationAttempt | null,
): "idle" | "processing" | "failure" {
  if (!attempt) return "idle";
  return attempt.isTerminalFailure ? "failure" : "processing";
}
