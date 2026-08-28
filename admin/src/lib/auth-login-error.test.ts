import assert from "node:assert/strict";
import test from "node:test";

import {
  AUTH_SERVICE_UNAVAILABLE_MESSAGE,
  translateLoginFailure,
} from "./auth-login-error";

test("login translates an unavailable Azure HTML response into a connection message", async () => {
  const failure = await translateLoginFailure(new Response(
    "<html><body>Web App - Unavailable</body></html>",
    { status: 403, headers: { "content-type": "text/html" } },
  ));

  assert.deepEqual(failure, {
    message: AUTH_SERVICE_UNAVAILABLE_MESSAGE,
    status: 503,
  });
});

test("login does not expose upstream JSON for transient failures", async () => {
  const failure = await translateLoginFailure(Response.json(
    { detail: { provider: "Azure", state: "AdminDisabled" } },
    { status: 503 },
  ));

  assert.deepEqual(failure, {
    message: AUTH_SERVICE_UNAVAILABLE_MESSAGE,
    status: 503,
  });
});

test("login translates a disabled Azure subscription JSON response", async () => {
  const failure = await translateLoginFailure(Response.json(
    {
      error: {
        code: "ReadOnlyDisabledSubscription",
        message: "The subscription is disabled.",
      },
    },
    { status: 403 },
  ));

  assert.deepEqual(failure, {
    message: AUTH_SERVICE_UNAVAILABLE_MESSAGE,
    status: 503,
  });
});

test("login keeps credential and safe domain errors distinct from connectivity", async () => {
  const invalidCredentials = await translateLoginFailure(Response.json(
    { detail: "internal detail must not replace the credential message" },
    { status: 401 },
  ));
  const forbidden = await translateLoginFailure(Response.json(
    { detail: "La organización está inactiva." },
    { status: 403 },
  ));

  assert.deepEqual(invalidCredentials, {
    message: "Usuario, empresa o contraseña incorrectos.",
    status: 401,
  });
  assert.deepEqual(forbidden, {
    message: "La organización está inactiva.",
    status: 403,
  });
});
