export type PosInstaller = {
  downloadUrl: string;
  version: string;
  sha256: string;
  tenantPreconfigured: false;
};

export async function loadPosInstaller(): Promise<PosInstaller> {
  const response = await fetch("/api/commerce/v1/pos/installer", {
    cache: "no-store",
    credentials: "include",
    headers: window.localStorage.getItem("selected_tenant_id")
      ? { "X-Tenant-Id": window.localStorage.getItem("selected_tenant_id")! }
      : undefined,
  });
  if (!response.ok) {
    const raw = await response.text();
    let detail = raw;
    try {
      const problem = JSON.parse(raw) as { detail?: string; title?: string };
      detail = problem.detail || problem.title || raw;
    } catch {
      // Plain-text failures are valid problem details too.
    }
    throw new Error(detail || "No fue posible consultar el instalador del POS.");
  }
  return (await response.json()) as PosInstaller;
}
