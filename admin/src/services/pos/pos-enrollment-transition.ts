export function shouldCompletePosEnrollment(
  edgeStatus: string,
  initialSessionAvailable: boolean,
  localUserAvailable: boolean,
): boolean {
  return initialSessionAvailable && !localUserAvailable && edgeStatus !== "EnrollmentRequired";
}

export function isPosPreparationPending(edgeStatus: string): boolean {
  return edgeStatus === "IdentitySynchronizing" ||
    edgeStatus === "Synchronizing";
}

type PosEnrollmentTransitionClient<THealth, TSession> = {
  completeEnrollment: () => Promise<unknown>;
  openWorkSession: () => Promise<TSession>;
  health: () => Promise<THealth>;
};

type RedeemedPosEdgeClient = {
  health: () => Promise<unknown>;
};

type RedeemedPosEdgeHealth<TClient> = TClient extends {
  health: () => Promise<infer THealth>;
} ? THealth : never;

/**
 * Reconnects the browser only after the enrolled Edge host has restarted.
 * Retiring the previous local user session before waiting prevents requests
 * from the old enrollment from racing the new host.
 */
export async function connectRedeemedPosEdge<TClient extends RedeemedPosEdgeClient>(
  retirePreviousSession: () => void,
  waitForRestart: () => Promise<unknown>,
  createClient: () => TClient,
): Promise<{
  client: TClient;
  health: RedeemedPosEdgeHealth<TClient>;
}> {
  retirePreviousSession();
  await waitForRestart();
  const client = createClient();
  const health = await client.health() as RedeemedPosEdgeHealth<TClient>;
  return { client, health };
}

/**
 * Finishes the browser-to-Edge handoff and always returns a health snapshot
 * taken after the local user and work sessions exist. The caller must never
 * make readiness decisions with the pre-enrollment snapshot.
 */
export async function completePendingPosEnrollment<
  THealth,
  TSession,
>(
  client: PosEnrollmentTransitionClient<THealth, TSession>,
): Promise<{
  health: THealth;
  session: TSession;
}> {
  await client.completeEnrollment();
  const session = await client.openWorkSession();
  const health = await client.health();
  return { health, session };
}
