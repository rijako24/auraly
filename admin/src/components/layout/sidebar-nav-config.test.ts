import assert from "node:assert/strict";
import test from "node:test";
import { authorizedNavigationGroups, authorizedNavigationItems } from "./sidebar-nav-config";

test("login permissions are the single visibility source for every navigation surface", () => {
  const items = authorizedNavigationItems(["catalog.read"]);

  assert.deepEqual(items.map((item) => item.name), ["Productos"]);
  assert.equal(items.some((item) => item.href === "/dashboard/settings/fiscal"), false);
  assert.deepEqual(
    authorizedNavigationGroups(["catalog.read"]),
    [{ label: "Catálogo", items }],
  );
});

test("the fiscal workspace is exposed as DIAN only with its backend permission", () => {
  assert.deepEqual(
    authorizedNavigationItems(["fiscal.configuration.read"])
      .map(({ name, href }) => ({ name, href })),
    [{ name: "DIAN", href: "/dashboard/settings/fiscal" }],
  );
});
