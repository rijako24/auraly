import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { posEnrollmentProblemDetail } from "./pos-enrollment-error";
import {
  completePendingPosEnrollment,
  isPosPreparationPending,
  shouldCompletePosEnrollment,
} from "./pos-enrollment-transition";

describe("posEnrollmentProblemDetail", () => {
  it("never exposes an internal server stack to the cashier", async () => {
    const response = new Response(JSON.stringify({
      detail: "System.InvalidOperationException: infrastructure detail\n at Internal.Class",
    }), { status: 500 });

    const message = await posEnrollmentProblemDetail(response);

    assert.match(message, /no pudo completar la preparación/i);
    assert.doesNotMatch(message, /InvalidOperationException|Internal\.Class/);
  });

  it("keeps an actionable enrollment conflict", async () => {
    const response = new Response(JSON.stringify({
      detail: "La organización alcanzó el máximo de cajas enroladas.",
    }), { status: 409 });

    assert.equal(
      await posEnrollmentProblemDetail(response),
      "La organización alcanzó el máximo de cajas enroladas.",
    );
  });
});

describe("shouldCompletePosEnrollment", () => {
  it("installs the enrollment package while its local preparation finishes", () => {
    assert.equal(shouldCompletePosEnrollment("IdentitySynchronizing", true, false), true);
    assert.equal(shouldCompletePosEnrollment("Synchronizing", true, false), true);
  });

  it("waits only while the unenrolled host is still restarting", () => {
    assert.equal(shouldCompletePosEnrollment("EnrollmentRequired", true, false), false);
    assert.equal(shouldCompletePosEnrollment("LoginRequired", false, false), false);
  });

  it("recovers the durable initial session after the app was closed", () => {
    assert.equal(
      shouldCompletePosEnrollment("IdentitySynchronizing", true, false),
      true,
    );
    assert.equal(shouldCompletePosEnrollment("LoginRequired", false, false), false);
  });

  it("reuses an existing local login instead of replacing it", () => {
    assert.equal(shouldCompletePosEnrollment("Synchronizing", true, true), false);
  });
});

describe("isPosPreparationPending", () => {
  it("keeps only actual preparation states in the progress experience", () => {
    assert.equal(isPosPreparationPending("IdentitySynchronizing"), true);
    assert.equal(isPosPreparationPending("Synchronizing"), true);
    assert.equal(isPosPreparationPending("Ready"), false);
    assert.equal(isPosPreparationPending("LoginRequired"), false);
  });
});

describe("completePendingPosEnrollment", () => {
  it("refreshes health only after installing the local user and work sessions", async () => {
    const calls: string[] = [];
    const result = await completePendingPosEnrollment({
      async completeEnrollment() {
        calls.push("complete-enrollment");
        return { token: "local-user", workSessionId: null as string | null };
      },
      async openWorkSession() {
        calls.push("open-work-session");
        return { token: "local-user", workSessionId: "work-session" };
      },
      async health() {
        calls.push("fresh-health");
        return { status: "Ready" };
      },
    });

    assert.deepEqual(calls, [
      "complete-enrollment",
      "open-work-session",
      "fresh-health",
    ]);
    assert.equal(result.health.status, "Ready");
    assert.equal(result.session.workSessionId, "work-session");
  });
});
