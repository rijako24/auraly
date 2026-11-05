type WorkspaceSynchronizationEnvelope = {
  type?: string;
  data?: { stream?: string; Stream?: string } | string;
};

export function workspaceSynchronizationStream(raw: string): string | null {
  try {
    const envelope = JSON.parse(raw) as WorkspaceSynchronizationEnvelope;
    const data = typeof envelope.data === "string"
      ? JSON.parse(envelope.data) as { stream?: string; Stream?: string }
      : envelope.data;
    return envelope.type === "message" ? data?.stream ?? data?.Stream ?? null : null;
  } catch {
    return null;
  }
}

export function shouldReconnectWorkspacePolicy(status?: number) {
  return status !== 401 && status !== 403;
}

export function isWorkspacePolicySynchronizationMessage(raw: string) {
  return workspaceSynchronizationStream(raw) === "Configuration";
}
