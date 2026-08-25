import { posApprovalClient } from "@/services/pos/pos-approval-client";
import { pushApplicationServerKeyMatches } from "@/lib/pos-approval-push-key";

function applicationServerKey(value: string) {
  const padding = "=".repeat((4 - value.length % 4) % 4);
  const raw = window.atob((value + padding).replace(/-/g, "+").replace(/_/g, "/"));
  return Uint8Array.from(raw, (character) => character.charCodeAt(0));
}

export async function ensurePosApprovalPushSubscription() {
  if (!("serviceWorker" in navigator) || !("PushManager" in window) || Notification.permission !== "granted") return false;
  const registration = await navigator.serviceWorker.ready;
  await registration.update().catch(() => undefined);
  const { publicKey } = await posApprovalClient.pushPublicKey();
  const serverKey = applicationServerKey(publicKey);
  let subscription = await registration.pushManager.getSubscription();
  if (subscription && !pushApplicationServerKeyMatches(
    subscription.options.applicationServerKey,
    serverKey,
  )) {
    await subscription.unsubscribe();
    subscription = null;
  }
  if (!subscription) {
    subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: serverKey,
    });
  }
  const json = subscription.toJSON();
  if (!json.endpoint || !json.keys?.p256dh || !json.keys.auth) throw new Error("El navegador no entregó una suscripción push completa.");
  await posApprovalClient.savePushSubscription({ endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth });
  return true;
}
