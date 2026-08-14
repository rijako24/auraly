import type { DeliveryResultInput, DispatchExecution } from "@/services/api/dispatches";

const DATABASE = "auraly-dispatch-pwa";
const VERSION = 2;
const SNAPSHOTS = "dispatch-snapshots";
const OUTBOX = "dispatch-outbox";
const EVIDENCE = "pending-evidence";

type Evidence = { placeholder: string; file: File };
type DeliveryOperation = { id: string; dispatchId: string; kind: "delivery"; request: DeliveryResultInput; evidence: Evidence[]; queuedAt: string; attempts: number };
type ExpenseOperation = { id: string; dispatchId: string; kind: "expense"; request: { category: string; amount: number; description: string; evidenceUrl: string; idempotencyKey: string; occurredAt: string }; evidence: Evidence[]; queuedAt: string; attempts: number };
type DispatchOperation = DeliveryOperation | ExpenseOperation;

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(SNAPSHOTS)) db.createObjectStore(SNAPSHOTS, { keyPath: "dispatchId" });
      if (!db.objectStoreNames.contains(OUTBOX)) db.createObjectStore(OUTBOX, { keyPath: "id" });
      if (!db.objectStoreNames.contains(EVIDENCE)) db.createObjectStore(EVIDENCE, { keyPath: "placeholder" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function transact<T>(storeName: string, mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await openDatabase();
  try {
    return await new Promise<T>((resolve, reject) => {
      const request = action(db.transaction(storeName, mode).objectStore(storeName));
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  } finally {
    db.close();
  }
}

export async function saveDispatchSnapshot(value: DispatchExecution) {
  await transact(SNAPSHOTS, "readwrite", store => store.put({ ...value, synchronizedAt: new Date().toISOString() }));
}

export async function loadDispatchSnapshot(dispatchId: string) {
  return await transact(SNAPSHOTS, "readonly", store => store.get(dispatchId)) as (DispatchExecution & { synchronizedAt: string }) | undefined;
}

async function putOperation(value: DispatchOperation) { await transact(OUTBOX, "readwrite", store => store.put(value)); }
async function removeOperation(id: string) { await transact(OUTBOX, "readwrite", store => store.delete(id)); }
export async function pendingDispatchOperations() { return await transact(OUTBOX, "readonly", store => store.getAll()) as DispatchOperation[]; }
export async function savePendingEvidence(placeholder: string, file: File) { await transact(EVIDENCE, "readwrite", store => store.put({ placeholder, file })); }
export async function loadPendingEvidence(placeholder: string) { return (await transact(EVIDENCE, "readonly", store => store.get(placeholder)) as { placeholder: string; file: File } | undefined)?.file; }
export async function removePendingEvidence(placeholder: string) { await transact(EVIDENCE, "readwrite", store => store.delete(placeholder)); }

export async function queueDeliveryOperation(dispatchId: string, request: DeliveryResultInput, evidence: Evidence[]) {
  await putOperation({ id: request.idempotencyKey, dispatchId, kind: "delivery", request, evidence, queuedAt: new Date().toISOString(), attempts: 0 });
  const snapshot = await loadDispatchSnapshot(dispatchId);
  if (!snapshot) return;
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
  await saveDispatchSnapshot({ ...snapshot, status: "InDelivery", documents });
}

export async function queueExpenseOperation(dispatchId: string, request: ExpenseOperation["request"], evidence: Evidence[]) {
  await putOperation({ id: request.idempotencyKey, dispatchId, kind: "expense", request, evidence, queuedAt: new Date().toISOString(), attempts: 0 });
  const snapshot = await loadDispatchSnapshot(dispatchId);
  if (snapshot) await saveDispatchSnapshot({ ...snapshot, expenses: [...snapshot.expenses, { expenseId: `local-${request.idempotencyKey}`, category: request.category, amount: request.amount, description: request.description, evidenceUrl: request.evidenceUrl, approvalStatus: "Pending", approvedAmount: null }] });
}

async function resolveEvidence(dispatchId: string, evidence: Evidence[]) {
  const { dispatchesApi } = await import("@/services/api/dispatches");
  const urls = new Map<string, string>();
  for (const item of evidence) urls.set(item.placeholder, (await dispatchesApi.uploadEvidence(dispatchId, item.file)).url);
  return urls;
}

export async function flushDispatchOutbox() {
  if (typeof navigator === "undefined" || !navigator.onLine) return { uploaded: 0, pending: (await pendingDispatchOperations()).length };
  const { dispatchesApi } = await import("@/services/api/dispatches");
  let uploaded = 0;
  const operations = (await pendingDispatchOperations()).sort((a, b) => a.queuedAt.localeCompare(b.queuedAt));
  for (const operation of operations) {
    try {
      const urls = await resolveEvidence(operation.dispatchId, operation.evidence);
      let result: DispatchExecution;
      if (operation.kind === "delivery") {
        const request = { ...operation.request, payments: operation.request.payments.map(payment => ({ ...payment, evidenceUrl: payment.evidenceUrl && urls.get(payment.evidenceUrl) || payment.evidenceUrl })) };
        result = await dispatchesApi.recordDelivery(operation.dispatchId, request);
      } else {
        const request = { ...operation.request, evidenceUrl: urls.get(operation.request.evidenceUrl) || operation.request.evidenceUrl };
        result = await dispatchesApi.addExpense(operation.dispatchId, request);
      }
      await saveDispatchSnapshot(result);
      await removeOperation(operation.id);
      uploaded++;
    } catch {
      await putOperation({ ...operation, attempts: operation.attempts + 1 });
      break;
    }
  }
  return { uploaded, pending: (await pendingDispatchOperations()).length };
}
