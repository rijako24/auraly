import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";

import { getBackendRequestUrl } from "./backend-request-url";

const originalBackend = process.env.NEXT_PUBLIC_API_URL;
const originalCommerceBackend = process.env.AURALY_COMMERCE_API_URL;

afterEach(() => {
  restore("NEXT_PUBLIC_API_URL", originalBackend);
  restore("AURALY_COMMERCE_API_URL", originalCommerceBackend);
});

describe("getBackendRequestUrl", () => {
  it("preserves the single gateway topology by default", () => {
    process.env.NEXT_PUBLIC_API_URL = "https://gateway.auraly.test/api/";
    delete process.env.AURALY_COMMERCE_API_URL;

    assert.equal(
      getBackendRequestUrl("commerce/v1/pos/drafts/active"),
      "https://gateway.auraly.test/api/commerce/v1/pos/drafts/active",
    );
  });

  it("routes Commerce requests to the dedicated Auraly API", () => {
    process.env.NEXT_PUBLIC_API_URL = "https://identity.auraly.test/api";
    process.env.AURALY_COMMERCE_API_URL = "http://127.0.0.1:5097/";

    assert.equal(
      getBackendRequestUrl(
        "commerce/v1/pos/register-context/options",
        "page=1",
      ),
      "http://127.0.0.1:5097/api/commerce/v1/pos/register-context/options?page=1",
    );
  });

  it("uses the root health endpoint of the dedicated Auraly API", () => {
    process.env.NEXT_PUBLIC_API_URL = "https://identity.auraly.test/api";
    process.env.AURALY_COMMERCE_API_URL = "http://127.0.0.1:5097";

    assert.equal(
      getBackendRequestUrl("health"),
      "http://127.0.0.1:5097/health",
    );
  });

  it("keeps authentication and existing admin routes on the primary API", () => {
    process.env.NEXT_PUBLIC_API_URL = "https://identity.auraly.test/api";
    process.env.AURALY_COMMERCE_API_URL = "https://commerce.auraly.test";

    assert.equal(
      getBackendRequestUrl("users"),
      "https://identity.auraly.test/api/users",
    );
  });
});

function restore(name: string, value: string | undefined) {
  if (value === undefined) delete process.env[name];
  else process.env[name] = value;
}
