type WorkspaceSynchronizationEnvelope = {
  type?: string;
  data?: { stream?: string; Stream?: string } | string;
};

export function isWorkspacePolicySynchronizationMessage(raw: string) {
  try {
    const envelope = JSON.parse(raw) as WorkspaceSynchronizationEnvelope;
    const data = typeof envelope.data === "string"
      ? JSON.parse(envelope.data) as { stream?: string; Stream?: string }
      : envelope.data;
    return envelope.type === "message"
      && (data?.stream ?? data?.Stream) === "Configuration";
  } catch {
    return false;
  }
}
