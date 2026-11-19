import type { DeliveryResultInput, DispatchExecution, DispatchExpenseInput } from "@/services/api/dispatches";
import { useAuthStore } from "@/stores/auth-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function dispatchOfflineScope(): string {
  const auth = useAuthStore.getState();
  const tenant = useTenantContextStore.getState().selectedTenantId;
  const business = useBusinessContextStore.getState().selectedBusinessId;
  if (!auth.isAuthenticated || !auth.user?.userId || !tenant || !business)
    throw new Error("Inicia sesión y selecciona la empresa y sede antes de abrir un despacho.");
  return `${tenant}:${business}:${auth.user.userId}`;
}

export function assertDispatchOfflineScope(scope: string) {
  if (scope !== dispatchOfflineScope()) throw new Error("Cambió la sesión o la sede. Abre de nuevo el despacho.");
}

export const DISPATCH_SYNC_COMPLETED_EVENT = "auraly:dispatch-synchronized";

const DATABASE = "auraly-dispatch-pwa";
// IndexedDB versions are monotonic. Version 3 was already shipped in DEV;
// opening it as version 2 blocks startup with VersionError.
const VERSION = 5;
const SYNC_BATCH_SIZE = 20;
const SNAPSHOTS = "dispatch-snapshots";
const OUTBOX = "dispatch-outbox";
const EVIDENCE = "pending-evidence";

type Evidence = { placeholder: string; file: File };
type DeliveryOperation = { scope: string; id: string; dispatchId: string; kind: "delivery"; request: DeliveryResultInput; evidence: Evidence[]; queuedAt: string; attempts: number };
type ExpenseOperation = { scope: string; id: string; dispatchId: string; kind: "expense"; request: DispatchExpenseInput; evidence: Evidence[]; queuedAt: string; attempts: number };
type DispatchOperation = DeliveryOperation | ExpenseOperation;

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(SNAPSHOTS)) db.createObjectStore(SNAPSHOTS, { keyPath: "dispatchId" });
      if (!db.objectStoreNames.contains(OUTBOX)) db.createObjectStore(OUTBOX, { keyPath: "id" });
      if (!db.objectStoreNames.contains(EVIDENCE)) db.createObjectStore(EVIDENCE, { keyPath: "placeholder" });
      const outbox = request.transaction!.objectStore(OUTBOX);
      if (!outbox.indexNames.contains("scope")) outbox.createIndex("scope", "scope");
      if (!outbox.indexNames.contains("scopeQueuedAt")) outbox.createIndex("scopeQueuedAt", ["scope", "queuedAt"]);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function transact<T>(storeName: string, mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await openDatabase();
  try {
    return await new Promise<T>((resolve, reject) => {
      const transaction = db.transaction(storeName, mode);
      const request = action(transaction.objectStore(storeName));
      transaction.oncomplete = () => resolve(request.result);
      transaction.onabort = () => reject(transaction.error ?? new Error("No fue posible guardar los cambios sin conexión."));
      transaction.onerror = () => reject(transaction.error);
      request.onerror = () => reject(request.error);
    });
  } finally {
    db.close();
  }
}

export async function saveDispatchSnapshot(value: DispatchExecution) {
  const scope = dispatchOfflineScope();
  await transact(SNAPSHOTS, "readwrite", store => store.put({ ...value, dispatchId: `${scope}:${value.dispatchId}`, scope, synchronizedAt: new Date().toISOString() }));
  assertDispatchOfflineScope(scope);
}

export async function loadDispatchSnapshot(dispatchId: string) {
  const scope = dispatchOfflineScope();
  const value = await transact(SNAPSHOTS, "readonly", store => store.get(`${scope}:${dispatchId}`)) as (DispatchExecution & { scope: string; synchronizedAt: string }) | undefined;
  assertDispatchOfflineScope(scope);
  return value?.scope === scope ? { ...value, dispatchId } : undefined;
}

async function putOperation(value: DispatchOperation) {
  assertDispatchOfflineScope(value.scope);
  await transact(OUTBOX, "readwrite", store => store.put(value));
}
async function removeOperation(id: string) { await transact(OUTBOX, "readwrite", store => store.delete(id)); }
export async function pendingDispatchOperations() {
  const scope = dispatchOfflineScope();
  const values = await transact(OUTBOX, "readonly", store => store.index("scopeQueuedAt").getAll(
    IDBKeyRange.bound([scope, ""], [scope, "\uffff"]), SYNC_BATCH_SIZE)) as DispatchOperation[];
  assertDispatchOfflineScope(scope);
  return values;
}
async function outboxCounts(scope: string) {
  const db = await openDatabase();
  try {
    return await new Promise<{ pending: number; unattributed: number }>((resolve,reject)=>{
      const transaction=db.transaction(OUTBOX,"readonly"),store=transaction.objectStore(OUTBOX);
      const pending=store.index("scope").count(scope),total=store.count(),attributed=store.index("scope").count();
      transaction.oncomplete=()=>resolve({pending:pending.result,unattributed:total.result-attributed.result});
      transaction.onerror=()=>reject(transaction.error);
      transaction.onabort=()=>reject(transaction.error);
    });
  } finally { db.close(); }
}

export async function savePendingEvidence(placeholder: string, file: File) {
  const scope = dispatchOfflineScope();
  await transact(EVIDENCE, "readwrite", store => store.put({ placeholder: `${scope}:${placeholder}`, scope, file }));
}
export async function loadPendingEvidence(placeholders: readonly string[]): Promise<Evidence[]> {
  if (!placeholders.length) return [];
  const scope = dispatchOfflineScope();
  const db = await openDatabase();
  try {
    return await new Promise<Evidence[]>((resolve,reject)=>{
      const transaction=db.transaction(EVIDENCE,"readonly"),store=transaction.objectStore(EVIDENCE);
      const requests=placeholders.map(placeholder=>({placeholder,request:store.get(`${scope}:${placeholder}`)}));
      transaction.oncomplete=()=>{
        try {
          assertDispatchOfflineScope(scope);
          const evidence=requests.map(({placeholder,request})=>{
            const value=request.result as {scope:string;file:File}|undefined;
            if(value?.scope!==scope) throw new Error("No se encontró el soporte guardado. Vuelve a adjuntarlo antes de confirmar.");
            return {placeholder,file:value.file};
          });
          resolve(evidence);
        } catch(error) { reject(error); }
      };
      transaction.onerror=()=>reject(transaction.error);
      transaction.onabort=()=>reject(transaction.error);
    });
  } finally { db.close(); }
}

export async function removePendingEvidence(placeholder: string) {
  const scope = dispatchOfflineScope();
  await transact(EVIDENCE, "readwrite", store => store.delete(`${scope}:${placeholder}`));
}

export async function queueDeliveryOperation(dispatchId: string, request: DeliveryResultInput, evidence: Evidence[]) {
  const scope = dispatchOfflineScope();
  const snapshot = await loadDispatchSnapshot(dispatchId);
  if (!snapshot) throw new Error("Prepara el despacho con conexión antes de trabajar sin señal.");
  await putOperation({ scope, id: `${scope}:${request.idempotencyKey}`, dispatchId, kind: "delivery", request, evidence, queuedAt: new Date().toISOString(), attempts: 0 });
  const documents = snapshot.documents.map(document => document.dispatchSourceDocumentId === request.dispatchSourceDocumentId ? {
    ...document,
    deliveryStatus: request.deliveryStatus,
    reason: request.reason,
    notes: request.notes,
    latitude: request.latitude,
    longitude: request.longitude,
    deliveredAt: request.occurredAt,
    payments: request.payments.map((value, index) => ({ paymentId: `local-${index}`, ...value })),
    returns: request.returns.map((value, index) => {
      const line = document.lines.find(item => item.originalLineNumber === value.originalLineNumber);
      return { returnLineId: `local-${index}`, productId: line?.productId ?? "", productCode: line?.productCode ?? "", description: line?.description ?? "", ...value };
    }),
  } : document);
  const routeCompleted=documents.every(document=>document.deliveryStatus!=="Pending");
  const updated: DispatchExecution = { ...snapshot, status: routeCompleted?"PendingSettlement":"InDelivery", documents };
  await saveDispatchSnapshot(updated);
  return updated;
}

export async function queueExpenseOperation(dispatchId: string, request: ExpenseOperation["request"], evidence: Evidence[]) {
  const scope = dispatchOfflineScope();
  const snapshot = await loadDispatchSnapshot(dispatchId);
  if (!snapshot) throw new Error("Prepara el despacho con conexión antes de trabajar sin señal.");
  await putOperation({ scope, id: `${scope}:${request.idempotencyKey}`, dispatchId, kind: "expense", request, evidence, queuedAt: new Date().toISOString(), attempts: 0 });
  const expenseId = `local-${request.idempotencyKey}`;
  const updated: DispatchExecution = { ...snapshot, expenses: [...snapshot.expenses.filter(value=>value.expenseId!==expenseId),
    { expenseId, category: request.category, amount: request.amount, description: request.description,
      evidenceUrl: request.evidenceUrl, approvalStatus: "Pending", approvedAmount: null,
      conceptId:request.conceptId??null,occurredAt:request.occurredAt }] };
  await saveDispatchSnapshot(updated);
  return updated;
}

export async function flushDispatchOutbox(): Promise<{uploaded:number;pending:number;unattributed:number;lastError?:string}> {
  if (typeof navigator === "undefined" || !useAuthStore.getState().isAuthenticated ||
      !useTenantContextStore.getState().selectedTenantId || !useBusinessContextStore.getState().selectedBusinessId)
    return { uploaded: 0, pending: 0, unattributed: 0 };
  const scope = dispatchOfflineScope();
  if (!navigator.onLine) return { uploaded: 0, ...await outboxCounts(scope) };
  if (!navigator.locks) {
    const counts=await outboxCounts(scope);
    return { uploaded: 0, ...counts, lastError: counts.pending ?
      "Este navegador no permite sincronizar los despachos de forma segura. Usa un navegador actualizado; los cambios se conservan." : undefined };
  }
  // IndexedDB is shared by tabs. One sender owns the scoped batch, including
  // proof uploads, so an idempotency key cannot race with different proof URLs.
  return navigator.locks.request(`auraly:dispatch-outbox:${scope}`, { ifAvailable: true }, async lock => {
    if (!lock) return { uploaded: 0, ...await outboxCounts(scope) };
    assertDispatchOfflineScope(scope);
    return flushScopedDispatchOutbox(scope);
  });
}

async function flushScopedDispatchOutbox(scope:string) {
  const { sendDispatchEvidence, sendDispatchDelivery, sendDispatchExpense } = await import("@/services/api/dispatches");
  let uploaded = 0;
  let lastError: string|undefined;
  const operations = await pendingDispatchOperations();
  for (const operation of operations) {
    let pending=operation;
    try {
      assertDispatchOfflineScope(scope);
      for (const item of operation.evidence) {
        assertDispatchOfflineScope(scope);
        const {url}=await sendDispatchEvidence(pending.dispatchId,item.file);
        assertDispatchOfflineScope(scope);
        const evidence=pending.evidence.filter(value=>value.placeholder!==item.placeholder);
        pending=pending.kind==="delivery" ? {...pending,evidence,request:{...pending.request,
          payments:pending.request.payments.map(payment=>payment.evidenceUrl===item.placeholder?{...payment,evidenceUrl:url}:payment)}}
          : {...pending,evidence,request:{...pending.request,evidenceUrl:pending.request.evidenceUrl===item.placeholder?url:pending.request.evidenceUrl}};
        // Save each successful upload before the next one. A partial failure or
        // reload resumes with only the remaining files and the same command ID.
        await putOperation(pending);
      }
      assertDispatchOfflineScope(scope);
      const result=pending.kind==="delivery"
        ? await sendDispatchDelivery(pending.dispatchId,pending.request)
        : await sendDispatchExpense(pending.dispatchId,pending.request);
      assertDispatchOfflineScope(scope);
      await removeOperation(pending.id);
      window.dispatchEvent(new CustomEvent(DISPATCH_SYNC_COMPLETED_EVENT,{detail:{scope,execution:result}}));
      uploaded++;
    } catch(error) {
      assertDispatchOfflineScope(scope);
      await putOperation({...pending,attempts:pending.attempts+1});
      lastError=error instanceof Error?error.message:"No fue posible enviar el cambio de despacho. Se conserva para reintentar.";
      break;
    }
  }
  return { uploaded, ...await outboxCounts(scope), lastError };
}
