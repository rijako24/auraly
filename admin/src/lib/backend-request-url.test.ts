import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";

import { getBackendRequestUrl } from "./backend-request-url";

const originalBackend = process.env.NEXT_PUBLIC_API_URL;

afterEach(() => {
  if (originalBackend === undefined) delete process.env.NEXT_PUBLIC_API_URL;
  else process.env.NEXT_PUBLIC_API_URL = originalBackend;
});

describe("getBackendRequestUrl", () => {
  it("routes platform and Commerce requests through one API", () => {
    process.env.NEXT_PUBLIC_API_URL = "https://api.auraly.test/api/";

    assert.equal(
      getBackendRequestUrl("businesses", "page=1"),
      "https://api.auraly.test/api/v1/businesses?page=1",
    );
    assert.equal(
      getBackendRequestUrl("commerce/v1/pos/drafts/active"),
      "https://api.auraly.test/api/commerce/v1/pos/drafts/active",
    );
    assert.equal(
      getBackendRequestUrl("auth/login"),
      "https://api.auraly.test/api/v1/auth/login",
    );
  });

  it("uses the root health endpoint of the same API", () => {
    process.env.NEXT_PUBLIC_API_URL = "http://127.0.0.1:5097/api";

    assert.equal(
      getBackendRequestUrl("health"),
      "http://127.0.0.1:5097/health",
    );
  });
});
