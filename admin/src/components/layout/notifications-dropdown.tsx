"use client";

import { Bell, Check, KeyRound, Loader2, ShieldCheck, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";

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
  const [credentialValidity, setCredentialValidity] = useState<"8"|"168"|"always">("always");
  const [credentialStatus, setCredentialStatus] = useState<SupervisorCredentialStatus | null>(null);
  const [notificationPermission, setNotificationPermission] = useState<NotificationPermission>(() => typeof Notification === "undefined" ? "denied" : Notification.permission);
  const knownIds = useRef(new Set<string>());

  useEffect(()=>{if(new URLSearchParams(window.location.search).has("posApproval"))setDropdownOpen(true)},[]);

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
    void refresh(false)
      .then(() => posApprovalClient.subscribe(() => active && void refresh(true)))
      .then((stop) => { dispose = stop; })
      .catch((caught) => {
        if (active) setError(caught instanceof Error ? caught.message : "No fue posible conectar las autorizaciones.");
      });
    return () => { active = false; dispose?.(); };
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

  async function saveCredential() {
    if (credential.length < 6 || credential !== credentialConfirmation) return;
    setSavingCredential(true);
    setError(null);
    try {
      await posApprovalClient.configureCredential(credential, credentialValidity === "always" ? null : Number(credentialValidity) as 8|168);
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
          {canApprove && canReceivePush && notificationPermission === "default" && <div className="border-b p-2"><Button variant="outline" size="sm" className="w-full" onClick={async()=>{const permission=await Notification.requestPermission();setNotificationPermission(permission);if(permission==="granted")await ensurePosApprovalPushSubscription()}}><Bell className="mr-2 h-4 w-4"/>Activar alertas de autorización</Button></div>}
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

      <Dialog open={credentialOpen} onOpenChange={setCredentialOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Credencial secundaria de supervisor</DialogTitle>
            <DialogDescription>
              Se usa únicamente cuando la autorización remota no está disponible. Al guardar, reemplaza y revoca la anterior.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            {credentialStatus?.isConfigured&&<div className="rounded-xl border bg-muted/40 p-3 text-sm"><strong>Credencial activa</strong><p className="text-muted-foreground">{credentialStatus.validUntil?`Vence ${new Date(credentialStatus.validUntil).toLocaleString("es-CO")}`:"Sin vencimiento"}</p></div>}
            <Input type="password" autoComplete="new-password" minLength={6} maxLength={32} value={credential} onChange={(event) => setCredential(event.target.value)} placeholder="Nueva credencial" />
            <Input type="password" autoComplete="new-password" minLength={6} maxLength={32} value={credentialConfirmation} onChange={(event) => setCredentialConfirmation(event.target.value)} placeholder="Confirmar credencial" />
            <Select value={credentialValidity} onValueChange={value=>setCredentialValidity(value as "8"|"168"|"always")}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="8">Válida por 8 horas</SelectItem><SelectItem value="168">Válida por 1 semana</SelectItem><SelectItem value="always">Siempre, hasta revocarla</SelectItem></SelectContent></Select>
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
    if (context.action === "RemoveLine") return `${source}Eliminar ${context.product || "un producto"} de la venta.`;
    if (context.action === "RestartSale") return `${source}Reiniciar la venta por completo${context.total ? ` · Total ${context.total}` : ""}.`;
    if (context.action === "Discount") return `${source}Aplicar descuento a ${context.product || "un producto"}.`;
    if (context.action === "CloseWorkSession") return `${source}Cerrar y conciliar la caja abierta.`;
  } catch { /* preserve generic description */ }
  return `${source}Permiso solicitado: ${request.permissionResource}`;
}
