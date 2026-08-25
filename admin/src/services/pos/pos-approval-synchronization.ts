type WebPubSubEnvelope = {
  type?: string;
  data?: { stream?: string; Stream?: string } | string;
};

export function shouldMaintainApprovalRealtimeConnection(
  visibilityState: DocumentVisibilityState,
) {
  return visibilityState === "visible";
}

export function isApprovalSynchronizationMessage(raw: string) {
  try {
    const envelope = JSON.parse(raw) as WebPubSubEnvelope;
    const data = typeof envelope.data === "string"
      ? JSON.parse(envelope.data) as { stream?: string; Stream?: string }
      : envelope.data;
    return envelope.type === "message" && (data?.stream ?? data?.Stream) === "Approvals";
  } catch {
    return false;
  }
}
