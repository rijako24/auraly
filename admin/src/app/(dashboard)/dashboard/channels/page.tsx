"use client";

import { useState } from "react";
import { CheckCircle2, CircleAlert, Eye, EyeOff, MessageCircle, Pencil, Plus, Radio, RefreshCw, ShieldCheck, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useAgents } from "@/hooks/use-agents";
import { useChannels, useCreateWhatsAppChannel, useDeactivateWhatsAppChannel, useUpdateWhatsAppChannel, useValidateWhatsAppChannel } from "@/hooks/use-channels";
import { type WhatsAppChannel, type WhatsAppChannelPayload, type WhatsAppConnectionStatus } from "@/services/api/channels";
import { useBusinessContextStore } from "@/stores/business-context-store";

const emptyForm: WhatsAppChannelPayload = { agentId: "", phoneNumber: "", whatsAppPhoneNumberId: "", whatsAppBusinessAccountId: "", accessToken: "", isActive: true };

export default function ChannelsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const channels = useChannels();
  const agents = useAgents();
  const createChannel = useCreateWhatsAppChannel();
  const updateChannel = useUpdateWhatsAppChannel();
  const deactivateChannel = useDeactivateWhatsAppChannel();
  const validateChannel = useValidateWhatsAppChannel();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<WhatsAppChannel | null>(null);
  const [form, setForm] = useState<WhatsAppChannelPayload>(emptyForm);
  const [showToken, setShowToken] = useState(false);
  const [statuses, setStatuses] = useState<Record<string, WhatsAppConnectionStatus>>({});

  if (!businessId) return <div className="space-y-4"><Header onAdd={() => undefined} disabled /><p className="text-muted-foreground">Selecciona un negocio para administrar sus canales.</p></div>;
  if (channels.isLoading || agents.isLoading) return <PageLoading />;
  if (channels.isError || agents.isError) return <PageError onRetry={() => { channels.refetch(); agents.refetch(); }} />;

  const openCreate = () => { setEditing(null); setForm({ ...emptyForm, agentId: agents.data?.find((x) => x.isActive)?.agentId ?? "" }); setShowToken(false); setDialogOpen(true); };
  const openEdit = (channel: WhatsAppChannel) => { setEditing(channel); setForm({ agentId: channel.agentId, phoneNumber: channel.phoneNumber, whatsAppPhoneNumberId: channel.whatsAppPhoneNumberId, whatsAppBusinessAccountId: channel.whatsAppBusinessAccountId, accessToken: "", isActive: channel.isActive }); setShowToken(false); setDialogOpen(true); };

  const checkConnection = async (channelId: string) => {
    try {
      const status = await validateChannel.mutateAsync(channelId);
      setStatuses((current) => ({ ...current, [channelId]: status }));
      if (status.isConnected) toast.success("Conexion con Meta validada");
      else toast.error(status.message);
      return status;
    } catch (error) { toast.error(getErrorMessage(error)); return null; }
  };

  const save = async () => {
    if (!form.agentId || !form.phoneNumber.trim() || !form.whatsAppPhoneNumberId.trim() || !form.whatsAppBusinessAccountId.trim() || (!editing && !form.accessToken?.trim())) {
      toast.error("Completa agente, numero, Phone Number ID, WABA ID y token."); return;
    }
    try {
      const saved = editing
        ? await updateChannel.mutateAsync({ channelId: editing.businessWhatsAppNumberId, payload: { ...form, accessToken: form.accessToken?.trim() || null } })
        : await createChannel.mutateAsync(form);
      setDialogOpen(false);
      toast.success(editing ? "Canal actualizado" : "Canal creado. Validando con Meta...");
      await checkConnection(saved.businessWhatsAppNumberId);
    } catch (error) { toast.error(getErrorMessage(error)); }
  };

  const remove = async (channel: WhatsAppChannel) => {
    if (!window.confirm(`¿Desactivar el canal ${channel.phoneNumber}?`)) return;
    try { await deactivateChannel.mutateAsync(channel.businessWhatsAppNumberId); toast.success("Canal desactivado"); }
    catch (error) { toast.error(getErrorMessage(error)); }
  };

  const activeCount = channels.data?.filter((x) => x.isActive).length ?? 0;
  const connectedCount = Object.values(statuses).filter((x) => x.isConnected).length;
  return <div className="space-y-6">
    <Header onAdd={openCreate} />
    <div className="grid gap-3 sm:grid-cols-3">
      <Summary label="Canales configurados" value={channels.data?.length ?? 0} icon={<Radio className="h-4 w-4" />} />
      <Summary label="Canales activos" value={activeCount} icon={<MessageCircle className="h-4 w-4" />} />
      <Summary label="Validados en esta sesion" value={connectedCount} icon={<ShieldCheck className="h-4 w-4" />} />
    </div>
    {(channels.data ?? []).length === 0 ? <EmptyState onAdd={openCreate} /> : <div className="grid gap-4 lg:grid-cols-2">{channels.data?.map((channel) => <ChannelCard key={channel.businessWhatsAppNumberId} channel={channel} status={statuses[channel.businessWhatsAppNumberId]} checking={validateChannel.isPending && validateChannel.variables === channel.businessWhatsAppNumberId} onCheck={() => checkConnection(channel.businessWhatsAppNumberId)} onEdit={() => openEdit(channel)} onRemove={() => remove(channel)} />)}</div>}
    <Dialog open={dialogOpen} onOpenChange={setDialogOpen}><DialogContent className="sm:max-w-xl"><DialogHeader><DialogTitle>{editing ? "Editar canal de WhatsApp" : "Conectar WhatsApp"}</DialogTitle><DialogDescription>Asocia el numero con el agente que respondera. El token se guarda en el servidor y nunca vuelve a mostrarse.</DialogDescription></DialogHeader>
      <div className="grid gap-4 py-2 sm:grid-cols-2">
        <Field label="Agente" wide><Select value={form.agentId} onValueChange={(agentId) => setForm({ ...form, agentId })}><SelectTrigger><SelectValue placeholder="Selecciona un agente" /></SelectTrigger><SelectContent>{agents.data?.map((agent) => <SelectItem key={agent.agentId} value={agent.agentId}>{agent.name}{!agent.isActive ? " (inactivo)" : ""}</SelectItem>)}</SelectContent></Select></Field>
        <Field label="Numero de telefono"><Input placeholder="+57 300 123 4567" value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} /></Field>
        <Field label="Phone Number ID"><Input placeholder="123456789012345" value={form.whatsAppPhoneNumberId} onChange={(e) => setForm({ ...form, whatsAppPhoneNumberId: e.target.value })} /></Field>
        <Field label="WABA ID"><Input placeholder="123456789012345" value={form.whatsAppBusinessAccountId} onChange={(e) => setForm({ ...form, whatsAppBusinessAccountId: e.target.value })} /></Field>
        <Field label={editing ? "Token (dejar vacio para conservar)" : "Token de acceso"} wide><div className="relative"><Input className="pr-10" type={showToken ? "text" : "password"} autoComplete="new-password" placeholder={editing ? "••••••••••••••••" : "EAAB..."} value={form.accessToken ?? ""} onChange={(e) => setForm({ ...form, accessToken: e.target.value })} /><Button type="button" variant="ghost" size="icon" className="absolute right-0 top-0" onClick={() => setShowToken((x) => !x)}>{showToken ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}</Button></div></Field>
        <div className="flex items-center justify-between rounded-lg border p-3 sm:col-span-2"><div><p className="text-sm font-medium">Canal activo</p><p className="text-xs text-muted-foreground">Permite recibir y responder conversaciones.</p></div><Switch checked={form.isActive} onCheckedChange={(isActive) => setForm({ ...form, isActive })} /></div>
      </div><DialogFooter><Button variant="outline" onClick={() => setDialogOpen(false)}>Cancelar</Button><Button onClick={save} disabled={createChannel.isPending || updateChannel.isPending}>{editing ? "Guardar cambios" : "Conectar y validar"}</Button></DialogFooter>
    </DialogContent></Dialog>
  </div>;
}

function Header({ onAdd, disabled = false }: { onAdd: () => void; disabled?: boolean }) { return <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between"><div><h1 className="text-2xl font-semibold tracking-tight">Canales de atención</h1><p className="text-muted-foreground">Conecta los puntos de contacto y asigna el agente que atendera cada uno.</p></div><Button onClick={onAdd} disabled={disabled}><Plus className="mr-2 h-4 w-4" />Conectar canal</Button></div>; }
function Summary({ label, value, icon }: { label: string; value: number; icon: React.ReactNode }) { return <Card><CardContent className="flex items-center justify-between p-4"><div><p className="text-2xl font-semibold">{value}</p><p className="text-xs text-muted-foreground">{label}</p></div><div className="rounded-full bg-muted p-2 text-muted-foreground">{icon}</div></CardContent></Card>; }
function EmptyState({ onAdd }: { onAdd: () => void }) { return <Card><CardContent className="flex flex-col items-center gap-4 py-14 text-center"><div className="rounded-full bg-emerald-500/10 p-4"><MessageCircle className="h-8 w-8 text-emerald-600" /></div><div><h2 className="font-semibold">Conecta tu primer canal</h2><p className="mt-1 max-w-md text-sm text-muted-foreground">Empieza con WhatsApp Cloud API. Al guardar comprobaremos el numero y la cuenta directamente con Meta.</p></div><Button onClick={onAdd}><Plus className="mr-2 h-4 w-4" />Conectar WhatsApp</Button></CardContent></Card>; }
function ChannelCard({ channel, status, checking, onCheck, onEdit, onRemove }: { channel: WhatsAppChannel; status?: WhatsAppConnectionStatus; checking: boolean; onCheck: () => void; onEdit: () => void; onRemove: () => void }) { return <Card><CardHeader><div className="flex items-start justify-between gap-3"><div className="flex gap-3"><div className="rounded-xl bg-emerald-500/10 p-2.5"><MessageCircle className="h-5 w-5 text-emerald-600" /></div><div><CardTitle className="text-base">WhatsApp</CardTitle><CardDescription>{channel.phoneNumber}</CardDescription></div></div><Badge variant={channel.isActive ? "default" : "secondary"}>{channel.isActive ? "Activo" : "Inactivo"}</Badge></div></CardHeader><CardContent className="space-y-4"><div className="grid grid-cols-2 gap-3 rounded-lg bg-muted/40 p-3 text-sm"><Info label="Agente" value={channel.agentName} /><Info label="Token" value={channel.hasAccessToken ? "Configurado" : "Pendiente"} /><Info label="Phone Number ID" value={channel.whatsAppPhoneNumberId} /><Info label="WABA ID" value={channel.whatsAppBusinessAccountId} /></div>{status && <div className={`rounded-lg border p-3 ${status.isConnected ? "border-emerald-500/30 bg-emerald-500/5" : "border-destructive/30 bg-destructive/5"}`}><div className="flex items-start gap-2">{status.isConnected ? <CheckCircle2 className="mt-0.5 h-4 w-4 text-emerald-600" /> : <CircleAlert className="mt-0.5 h-4 w-4 text-destructive" />}<div><p className="text-sm font-medium">{status.isConnected ? status.verifiedName || "Conexion verificada" : "No se pudo validar"}</p><p className="text-xs text-muted-foreground">{status.message}</p>{status.businessAccountName && <p className="mt-1 text-xs text-muted-foreground">Cuenta: {status.businessAccountName} · Calidad: {status.qualityRating || "sin dato"}</p>}</div></div></div>}<div className="flex flex-wrap gap-2"><Button variant="outline" size="sm" onClick={onCheck} disabled={checking}><RefreshCw className={`mr-2 h-4 w-4 ${checking ? "animate-spin" : ""}`} />Validar con Meta</Button><Button variant="ghost" size="sm" onClick={onEdit}><Pencil className="mr-2 h-4 w-4" />Editar</Button>{channel.isActive && <Button variant="ghost" size="sm" className="text-destructive hover:text-destructive" onClick={onRemove}><Trash2 className="mr-2 h-4 w-4" />Desactivar</Button>}</div></CardContent></Card>; }
function Info({ label, value }: { label: string; value: string }) { return <div className="min-w-0"><p className="text-xs text-muted-foreground">{label}</p><p className="truncate font-medium" title={value}>{value}</p></div>; }
function Field({ label, children, wide = false }: { label: string; children: React.ReactNode; wide?: boolean }) { return <div className={`space-y-2 ${wide ? "sm:col-span-2" : ""}`}><Label>{label}</Label>{children}</div>; }
function getErrorMessage(error: unknown) { return typeof error === "object" && error !== null && "message" in error ? String(error.message) : "No fue posible completar la operacion."; }
