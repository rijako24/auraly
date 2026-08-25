"use client";

import { Bell, Check, KeyRound, Loader2, ShieldCheck, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ScrollArea } from "@/components/ui/scroll-area";
import { cn } from "@/lib/utils";
import { ensurePosApprovalPushSubscription } from "@/lib/pos-approval-push";
import { posApprovalClient, type PosApprovalRequest, type SupervisorCredentialStatus } from "@/services/pos/pos-approval-client";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function NotificationsDropdown({ className }: { className?: string }) {
  const user = useAuthStore((state) => state.user);
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const canApprove = Boolean(
    user?.permissions.includes("pos.approvals.read") &&
    user.permissions.includes("pos.approvals.authorize"),
  );
  const canReceivePush = Boolean(user?.permissions.includes("pos.approvals.receive_notifications"));
  const [dropdownOpen,setDropdownOpen]=useState(false);
  const [requests, setRequests] = useState<PosApprovalRequest[]>([]);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [credentialOpen, setCredentialOpen] = useState(false);
  const [credential, setCredential] = useState("");
  const [credentialConfirmation, setCredentialConfirmation] = useState("");
  const [savingCredential, setSavingCredential] = useState(false);
  const [credentialValidity, setCredentialValidity] = useState<"once"|"8"|"168"|"always">("always");
  const [credentialStatus, setCredentialStatus] = useState<SupervisorCredentialStatus | null>(null);
  const [notificationPermission, setNotificationPermission] = useState<NotificationPermission>(() => typeof Notification === "undefined" ? "denied" : Notification.permission);
  const [activatingNotifications, setActivatingNotifications] = useState(false);
  const [backgroundPushState, setBackgroundPushState] = useState<"idle"|"checking"|"active"|"error">("idle");
  const [portalReady, setPortalReady] = useState(false);
  const knownIds = useRef(new Set<string>());

  useEffect(() => setPortalReady(true), []);
  useEffect(()=>{if(new URLSearchParams(window.location.search).has("posApproval"))setDropdownOpen(true)},[]);
  useEffect(() => {
    if (!canReceivePush || !businessId || typeof Notification === "undefined") return;
    setNotificationPermission(Notification.permission);
    if (Notification.permission !== "granted") {
      setBackgroundPushState("idle");
      return;
    }
    let active = true;
    setBackgroundPushState("checking");
    void ensurePosApprovalPushSubscription()
      .then((subscribed) => {
        if (active) setBackgroundPushState(subscribed ? "active" : "error");
      })
      .catch((caught) => {
        if (!active) return;
        setBackgroundPushState("error");
        setError(caught instanceof Error ? caught.message : "No fue posible registrar este teléfono para alertas cerradas.");
      });
    return () => { active = false; };
  }, [businessId, canReceivePush]);

  const refresh = useCallback(async (notify = false) => {
    if (!canApprove || !businessId) return;
    try {
      const pending = await posApprovalClient.pending();
      if (notify && typeof Notification !== "undefined" && Notification.permission === "granted") {
        for (const request of pending) {
          if (!knownIds.current.has(request.approvalRequestId)) {
            const notification = new Notification("Auraly · autorización POS", {
              body: `${request.requestedByName} solicita autorización para una acción protegida.`,
              tag: request.approvalRequestId,
            });
            notification.onclick = () => { window.focus(); notification.close(); };
          }
        }
      }
      knownIds.current = new Set(pending.map((request) => request.approvalRequestId));
      setRequests(pending);
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible cargar las autorizaciones.");
    }
  }, [businessId, canApprove]);

  useEffect(() => {
    if (!canApprove || !businessId) {
      setRequests([]);
      return;
    }
    let active = true;
    let dispose: (() => void) | undefined;
    const refreshVisible = () => {
      if (document.visibilityState === "visible") void refresh(true);
    };
    const receiveServiceWorkerMessage = (event: MessageEvent<{ type?: string }>) => {
      if (event.data?.type === "auraly:pos-approvals-changed") void refresh(true);
    };
    const fallback = window.setInterval(refreshVisible, 15_000);
    window.addEventListener("focus", refreshVisible);
    document.addEventListener("visibilitychange", refreshVisible);
    navigator.serviceWorker?.addEventListener("message", receiveServiceWorkerMessage);
    void refresh(false)
      .then(() => posApprovalClient.subscribe(() => active && void refresh(true)))
      .then((stop) => { dispose = stop; })
      .catch((caught) => {
        if (active) setError(caught instanceof Error ? caught.message : "No fue posible conectar las autorizaciones.");
      });
    return () => {
      active = false;
      window.clearInterval(fallback);
      window.removeEventListener("focus", refreshVisible);
      document.removeEventListener("visibilitychange", refreshVisible);
      navigator.serviceWorker?.removeEventListener("message", receiveServiceWorkerMessage);
      dispose?.();
    };
  }, [businessId, canApprove, refresh]);

  async function decide(request: PosApprovalRequest, approve: boolean) {
    setBusyId(request.approvalRequestId);
    setError(null);
    try {
      await posApprovalClient.decide(request.approvalRequestId, approve);
      setRequests((current) => current.filter(
        (item) => item.approvalRequestId !== request.approvalRequestId));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible responder la solicitud.");
    } finally {
      setBusyId(null);
    }
  }

  async function activateNotifications() {
    if (typeof Notification === "undefined") return;
    setActivatingNotifications(true);
    setError(null);
    try {
      const permission = await Notification.requestPermission();
      setNotificationPermission(permission);
      if (permission !== "granted") {
        setError("Debes permitir las notificaciones para recibir aprobaciones con Auraly cerrada.");
        return;
      }
      const subscribed = await ensurePosApprovalPushSubscription();
      if (!subscribed) throw new Error("Este dispositivo no permite notificaciones en segundo plano. Instala Auraly en la pantalla de inicio e inténtalo de nuevo.");
      setBackgroundPushState("active");
    } catch (caught) {
      setBackgroundPushState("error");
      setError(caught instanceof Error ? caught.message : "No fue posible activar las notificaciones.");
    } finally {
      setActivatingNotifications(false);
    }
  }

  async function saveCredential() {
    if (credential.length < 6 || credential !== credentialConfirmation) return;
    setSavingCredential(true);
    setError(null);
    try {
      await posApprovalClient.configureCredential(
        credential,
        credentialValidity === "once" || credentialValidity === "always"
          ? null
          : Number(credentialValidity) as 8|168,
        credentialValidity === "once",
      );
      setCredentialStatus(await posApprovalClient.credentialStatus());
      setCredential("");
      setCredentialConfirmation("");
      setCredentialOpen(false);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "No fue posible guardar la credencial.");
    } finally {
      setSavingCredential(false);
    }
  }

  async function openCredential() {
    setCredentialOpen(true);
    try { setCredentialStatus(await posApprovalClient.credentialStatus()); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "No fue posible consultar la credencial."); }
  }

  async function revokeCredential() {
    if (!window.confirm("¿Revocar ahora la credencial secundaria? Dejará de autorizar inmediatamente.")) return;
    setSavingCredential(true);
    try { await posApprovalClient.revokeCredential(); setCredentialStatus({isConfigured:false,createdAt:null,validUntil:null}); }
    catch (caught) { setError(caught instanceof Error ? caught.message : "No fue posible revocar la credencial."); }
    finally { setSavingCredential(false); }
  }

  return (
    <>
      <DropdownMenu open={dropdownOpen} onOpenChange={setDropdownOpen}>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon" className={cn("relative h-9 w-9", className)} aria-label="Notificaciones">
            <Bell className="h-4 w-4" />
            {requests.length > 0 && (
              <Badge variant="destructive" className="absolute -right-1 -top-1 h-5 min-w-5 rounded-full px-1.5 text-[10px]">
                {requests.length > 99 ? "99+" : requests.length}
              </Badge>
            )}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent className="w-[min(92vw,26rem)]" align="end" forceMount>
          <DropdownMenuLabel className="flex items-center justify-between gap-3">
            <span>Notificaciones</span>
            {canApprove && (
              <Button variant="ghost" size="sm" className="h-8 gap-1.5" onClick={() => void openCredential()}>
                <KeyRound className="h-3.5 w-3.5" /> Clave de supervisor
              </Button>
            )}
          </DropdownMenuLabel>
          <DropdownMenuSeparator />
          {canApprove && canReceivePush && notificationPermission === "default" && <div className="border-b p-2"><Button variant="outline" size="sm" className="w-full" disabled={activatingNotifications} onClick={()=>void activateNotifications()}>{activatingNotifications?<Loader2 className="mr-2 h-4 w-4 animate-spin"/>:<Bell className="mr-2 h-4 w-4"/>}Activar alertas de autorización</Button></div>}
          {canApprove && canReceivePush && notificationPermission === "denied" && <p className="border-b bg-amber-50 p-3 text-xs font-medium text-amber-900">Las notificaciones del sistema están bloqueadas. Habilita Auraly en Ajustes → Notificaciones del iPhone.</p>}
          {canApprove && canReceivePush && notificationPermission === "granted" && backgroundPushState === "checking" && <p className="border-b p-3 text-xs text-muted-foreground"><Loader2 className="mr-2 inline h-3.5 w-3.5 animate-spin"/>Verificando alertas con Auraly cerrada…</p>}
          {canApprove && canReceivePush && notificationPermission === "granted" && backgroundPushState === "active" && <p className="border-b bg-emerald-50 p-3 text-xs font-semibold text-emerald-800"><ShieldCheck className="mr-2 inline h-3.5 w-3.5"/>Alertas con Auraly cerrada: activas</p>}
          {canApprove && canReceivePush && notificationPermission === "granted" && backgroundPushState === "error" && <div className="border-b p-2"><Button variant="outline" size="sm" className="w-full" disabled={activatingNotifications} onClick={()=>void activateNotifications()}><Bell className="mr-2 h-4 w-4"/>Reactivar alertas en segundo plano</Button></div>}
          <ScrollArea className="max-h-[22rem]">
            {!canApprove ? (
              <div className="py-8 text-center text-sm text-muted-foreground">No hay notificaciones nuevas</div>
            ) : requests.length === 0 ? (
              <div className="py-8 text-center text-sm text-muted-foreground">
                <ShieldCheck className="mx-auto mb-2 h-6 w-6 text-emerald-600" />
                No hay autorizaciones pendientes
              </div>
            ) : (
              <div className="space-y-2 p-2">
                {requests.map((request) => (
                  <article key={request.approvalRequestId} className="rounded-xl border bg-card p-3 shadow-sm">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <p className="font-semibold">Acción protegida en Punto de venta</p>
                        <p className="mt-0.5 text-xs text-muted-foreground">Solicita: {request.requestedByName}</p>
                      </div>
                      <Badge variant="secondary">Pendiente</Badge>
                    </div>
                    <p className="mt-2 rounded-lg bg-muted/60 p-2 text-xs text-muted-foreground">
                      {describeContext(request)}
                    </p>
                    <div className="mt-3 grid grid-cols-2 gap-2">
                      <Button variant="destructive" size="sm" disabled={busyId === request.approvalRequestId} onClick={() => void decide(request, false)}>
                        <X className="mr-1.5 h-4 w-4" /> Rechazar
                      </Button>
                      <Button size="sm" className="bg-emerald-600 hover:bg-emerald-700" disabled={busyId === request.approvalRequestId} onClick={() => void decide(request, true)}>
                        {busyId === request.approvalRequestId ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : <Check className="mr-1.5 h-4 w-4" />}
                        Aprobar una vez
                      </Button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </ScrollArea>
          {error && <p className="border-t p-3 text-xs font-medium text-destructive">{error}</p>}
        </DropdownMenuContent>
      </DropdownMenu>

      {portalReady && canApprove && requests[0] && createPortal(
        <section
          role="alertdialog"
          aria-label="Solicitud de autorización de caja"
          className="fixed inset-x-3 bottom-[calc(4.75rem+env(safe-area-inset-bottom))] z-[100] max-h-[calc(100dvh-env(safe-area-inset-top)-env(safe-area-inset-bottom)-6rem)] overflow-y-auto overscroll-contain rounded-3xl border border-slate-200 bg-white p-4 shadow-[0_24px_80px_rgba(15,23,42,0.35)] md:hidden"
        >
          <div className="flex items-start gap-3">
            <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700">
              <ShieldCheck className="h-5 w-5" />
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex items-center justify-between gap-2">
                <p className="font-bold text-slate-950">Autorizar acción en caja</p>
                {requests.length > 1 && <Badge variant="secondary">+{requests.length - 1}</Badge>}
              </div>
              <p className="mt-0.5 text-sm text-slate-600">{requests[0].requestedByName}</p>
              <p className="mt-2 rounded-xl bg-slate-50 p-2 text-xs text-slate-600">
                {describeContext(requests[0])}
              </p>
            </div>
          </div>
          <div className="mt-4 grid grid-cols-2 gap-3">
            <Button
              variant="destructive"
              className="h-12 rounded-xl bg-red-600 text-base font-bold hover:bg-red-700"
              disabled={busyId === requests[0].approvalRequestId}
              onClick={() => void decide(requests[0], false)}
            >
              <X className="mr-2 h-5 w-5" /> Denegar
            </Button>
            <Button
              className="h-12 rounded-xl bg-emerald-600 text-base font-bold hover:bg-emerald-700"
              disabled={busyId === requests[0].approvalRequestId}
              onClick={() => void decide(requests[0], true)}
            >
              <Check className="mr-2 h-5 w-5" /> Aprobar
            </Button>
          </div>
          {error && <p className="mt-3 text-sm font-medium text-red-700">{error}</p>}
        </section>,
        document.body,
      )}

      {portalReady && canApprove && canReceivePush && requests.length === 0 && notificationPermission === "default" && createPortal(
        <section
          role="status"
          className="fixed inset-x-3 bottom-[calc(4.75rem+env(safe-area-inset-bottom))] z-[100] rounded-3xl border border-teal-200 bg-white p-4 shadow-[0_24px_80px_rgba(15,23,42,0.28)] md:hidden"
        >
          <div className="flex items-start gap-3">
            <span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700"><Bell className="h-5 w-5" /></span>
            <div className="min-w-0 flex-1"><p className="font-bold text-slate-950">Recibe aprobaciones con Auraly cerrada</p><p className="mt-1 text-sm text-slate-600">Activa una vez las notificaciones de este teléfono.</p></div>
          </div>
          <Button className="mt-4 h-11 w-full rounded-xl bg-teal-700 font-bold hover:bg-teal-800" disabled={activatingNotifications} onClick={()=>void activateNotifications()}>
            {activatingNotifications?<Loader2 className="mr-2 h-4 w-4 animate-spin"/>:<Bell className="mr-2 h-4 w-4"/>}Activar notificaciones
          </Button>
          {error && <p className="mt-3 text-sm font-medium text-red-700">{error}</p>}
        </section>,
        document.body,
      )}

      <Dialog open={credentialOpen} onOpenChange={setCredentialOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Credencial secundaria de supervisor</DialogTitle>
            <DialogDescription>
              Se usa únicamente cuando la autorización remota no está disponible. Al guardar, reemplaza y revoca la anterior.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            {credentialStatus?.isConfigured&&<div className="rounded-xl border bg-muted/40 p-3 text-sm"><strong>Credencial activa</strong><p className="text-muted-foreground">{credentialStatus.isOneTime?"Válida para una sola autorización":credentialStatus.validUntil?`Vence ${new Date(credentialStatus.validUntil).toLocaleString("es-CO")}`:"Sin vencimiento"}</p></div>}
            <Input type="password" autoComplete="new-password" minLength={6} maxLength={32} value={credential} onChange={(event) => setCredential(event.target.value)} placeholder="Nueva credencial" />
            <Input type="password" autoComplete="new-password" minLength={6} maxLength={32} value={credentialConfirmation} onChange={(event) => setCredentialConfirmation(event.target.value)} placeholder="Confirmar credencial" />
            <Select value={credentialValidity} onValueChange={value=>setCredentialValidity(value as "once"|"8"|"168"|"always")}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="once">Un solo uso</SelectItem><SelectItem value="8">Válida por 8 horas</SelectItem><SelectItem value="168">Válida por 1 semana</SelectItem><SelectItem value="always">Siempre, hasta revocarla</SelectItem></SelectContent></Select>
            {credentialConfirmation && credential !== credentialConfirmation && <p className="text-sm text-destructive">Las credenciales no coinciden.</p>}
          </div>
          <DialogFooter>
            {credentialStatus?.isConfigured&&<Button variant="destructive" className="sm:mr-auto" disabled={savingCredential} onClick={()=>void revokeCredential()}>Revocar actual</Button>}
            <Button variant="outline" onClick={() => setCredentialOpen(false)}>Cancelar</Button>
            <Button disabled={savingCredential || credential.length < 6 || credential !== credentialConfirmation} onClick={() => void saveCredential()}>
              {savingCredential && <Loader2 className="mr-2 h-4 w-4 animate-spin" />} {credentialStatus?.isConfigured ? "Reiniciar credencial" : "Guardar credencial"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function describeContext(request: PosApprovalRequest) {
  const source = request.deviceId ? `Caja ${request.deviceId.slice(0,8).toLocaleUpperCase("es-CO")} · ` : "";
  try {
    const context = JSON.parse(request.contextJson) as { action?: string; product?: string; discount?: number; total?: number };
    if (["RemoveLine", "OpenRemoveLine", "ConfirmRemoveLine"].includes(context.action ?? "")) return `${source}Eliminar ${context.product || "un producto"} de la venta.`;
    if (["RestartSale", "OpenRestartSale", "ConfirmRestartSale"].includes(context.action ?? "")) return `${source}Reiniciar la venta por completo${context.total ? ` · Total ${context.total}` : ""}.`;
    if (["Discount", "OpenDiscount", "ConfirmDiscount"].includes(context.action ?? "")) return `${source}Aplicar descuento a ${context.product || "un producto"}.`;
    if (["CloseWorkSession", "OpenWorkSessionClosure", "ConfirmWorkSessionClosure"].includes(context.action ?? "")) return `${source}Cerrar y conciliar la caja abierta.`;
  } catch { /* preserve generic description */ }
  return `${source}Permiso solicitado: ${request.permissionResource}`;
}
