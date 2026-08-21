import { openSalesOfflineDatabase } from "@/lib/sales-offline-database";
import type { AuthUser } from "@/types/api";

const STORE = "offline-logins";
const ITERATIONS = 210_000;

type OfflineLogin = {
  key: string;
  salt: number[];
  verifier: number[];
  user: AuthUser;
  preparedAt: string;
};

const loginKey = (tenantKey: string, username: string) =>
  `${tenantKey.trim().toLocaleLowerCase("es")}:${username.trim().toLocaleLowerCase("es")}`;

async function transact<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>) {
  const database = await openSalesOfflineDatabase();
  try {
    return await new Promise<T>((resolve, reject) => {
      const request = action(database.transaction(STORE, mode).objectStore(STORE));
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  } finally {
    database.close();
  }
}

async function derive(password: string, salt: Uint8Array) {
  const material = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
  const saltBuffer = Uint8Array.from(salt).buffer;
  const bits = await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt: saltBuffer, iterations: ITERATIONS }, material, 256);
  return new Uint8Array(bits);
}

export async function rememberOfflineLogin(tenantKey: string, username: string, password: string, user: AuthUser) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const verifier = await derive(password, salt);
  await transact("readwrite", store => store.put({
    key: loginKey(tenantKey, username),
    salt: [...salt],
    verifier: [...verifier],
    user,
    preparedAt: new Date().toISOString(),
  } satisfies OfflineLogin));
}

export async function verifyOfflineLogin(tenantKey: string, username: string, password: string) {
  const saved = await transact("readonly", store => store.get(loginKey(tenantKey, username))) as OfflineLogin | undefined;
  if (!saved) return null;
  const candidate = await derive(password, new Uint8Array(saved.salt));
  if (candidate.length !== saved.verifier.length) return null;
  let difference = 0;
  for (let index = 0; index < candidate.length; index += 1) difference |= candidate[index] ^ saved.verifier[index];
  return difference === 0 ? saved.user : null;
}
