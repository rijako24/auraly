import type { RecordSalesRouteVisit, SalesRouteDetail, SalesRouteVisit } from "@/services/api/routes";
import { openSalesOfflineDatabase } from "@/lib/sales-offline-database";
export { dailyRouteSnapshotKey } from "@/lib/seller-offline-scope";

const SNAPSHOTS = "daily-route-snapshots";
const OUTBOX = "route-visit-outbox";

export type DailyRouteSnapshot = {
  key: string;
  userId: string;
  businessId: string;
  warehouseId: string;
  date: string;
  route: SalesRouteDetail;
  visits: SalesRouteVisit[];
  synchronizedAt: string;
};

export type PendingRouteVisit = {
  idempotencyKey: string;
  userId: string;
  routeId: string;
  businessId: string;
  warehouseId: string;
  request: RecordSalesRouteVisit;
  queuedAt: string;
  attempts: number;
};

async function request<T>(storeName: string, mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const database = await openSalesOfflineDatabase();
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

export async function loadDailyRouteSnapshots(userId: string, businessId: string, warehouseId: string, date: string) {
  const prefix=`${userId}:${businessId}:${warehouseId}:${date}:`;
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

export async function pendingRouteVisits(userId: string) {
  const values=(await request(OUTBOX, "readonly", (store) => store.getAll())) as PendingRouteVisit[];
  return values.filter(value=>value.userId===userId);
}

export async function removePendingRouteVisit(idempotencyKey: string) {
  await request(OUTBOX, "readwrite", (store) => store.delete(idempotencyKey));
}

export async function flushPendingRouteVisits(
  userId: string,
  upload: (routeId: string, request: RecordSalesRouteVisit) => Promise<SalesRouteVisit>,
) {
  if (!navigator.onLine) return { uploaded: 0, pending: (await pendingRouteVisits(userId)).length };
  let uploaded = 0;
  const values = (await pendingRouteVisits(userId)).sort((left, right) => left.queuedAt.localeCompare(right.queuedAt));
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
  return { uploaded, pending: (await pendingRouteVisits(userId)).length };
}
