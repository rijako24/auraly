import type { RecordSalesRouteVisit, SalesRouteDetail, SalesRouteVisit } from "@/services/api/routes";

const DATABASE = "auraly-sales-pwa";
const VERSION = 2;
const SNAPSHOTS = "daily-route-snapshots";
const OUTBOX = "route-visit-outbox";

export type DailyRouteSnapshot = {
  key: string;
  businessId: string;
  warehouseId: string;
  date: string;
  route: SalesRouteDetail;
  visits: SalesRouteVisit[];
  synchronizedAt: string;
};

export type PendingRouteVisit = {
  idempotencyKey: string;
  routeId: string;
  businessId: string;
  warehouseId: string;
  request: RecordSalesRouteVisit;
  queuedAt: string;
  attempts: number;
};

export const dailyRouteSnapshotKey = (businessId: string, warehouseId: string, date: string, routeId: string) =>
  `${businessId}:${warehouseId}:${date}:${routeId}`;

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(SNAPSHOTS))
        database.createObjectStore(SNAPSHOTS, { keyPath: "key" });
      if (!database.objectStoreNames.contains(OUTBOX))
        database.createObjectStore(OUTBOX, { keyPath: "idempotencyKey" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function request<T>(storeName: string, mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const database = await openDatabase();
  try {
    return await new Promise<T>((resolve, reject) => {
      const value = action(database.transaction(storeName, mode).objectStore(storeName));
      value.onsuccess = () => resolve(value.result);
      value.onerror = () => reject(value.error);
    });
  } finally {
    database.close();
  }
}

export async function loadDailyRouteSnapshots(businessId: string, warehouseId: string, date: string) {
  const prefix=`${businessId}:${warehouseId}:${date}:`;
  const values=(await request(SNAPSHOTS,"readonly",(store)=>store.getAll())) as DailyRouteSnapshot[];
  return values.filter((value)=>value.key.startsWith(prefix)).sort((left,right)=>routeOrder(left.route,date)-routeOrder(right.route,date));
}

function routeOrder(route:SalesRouteDetail,date:string){
  const day=new Date(`${date}T12:00:00`).getDay()||7;
  return route.schedules.find((schedule)=>schedule.dayOfWeek===day)?.runOrder??Number.MAX_SAFE_INTEGER;
}

export async function saveDailyRouteSnapshot(snapshot: DailyRouteSnapshot) {
  await request(SNAPSHOTS, "readwrite", (store) => store.put(snapshot));
}

export async function queueRouteVisit(value: PendingRouteVisit) {
  await request(OUTBOX, "readwrite", (store) => store.put(value));
}

export async function pendingRouteVisits() {
  return (await request(OUTBOX, "readonly", (store) => store.getAll())) as PendingRouteVisit[];
}

export async function removePendingRouteVisit(idempotencyKey: string) {
  await request(OUTBOX, "readwrite", (store) => store.delete(idempotencyKey));
}

export async function flushPendingRouteVisits(
  upload: (routeId: string, request: RecordSalesRouteVisit) => Promise<SalesRouteVisit>,
) {
  if (!navigator.onLine) return { uploaded: 0, pending: (await pendingRouteVisits()).length };
  let uploaded = 0;
  const values = (await pendingRouteVisits()).sort((left, right) => left.queuedAt.localeCompare(right.queuedAt));
  for (const value of values) {
    try {
      await upload(value.routeId, value.request);
      await removePendingRouteVisit(value.idempotencyKey);
      uploaded += 1;
    } catch {
      await queueRouteVisit({ ...value, attempts: value.attempts + 1 });
      break;
    }
  }
  return { uploaded, pending: (await pendingRouteVisits()).length };
}
