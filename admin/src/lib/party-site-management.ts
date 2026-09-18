export type PartyWithSiteRoles = {
  customer?: unknown | null;
  supplier?: unknown | null;
};

export function supportsManagedSites(party: PartyWithSiteRoles): boolean {
  return Boolean(party.customer || party.supplier);
}
