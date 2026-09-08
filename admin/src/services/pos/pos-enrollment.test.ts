import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { posEnrollmentProblemDetail } from "./pos-enrollment-error";
import { shouldCompletePosEnrollment } from "./pos-enrollment-transition";

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
    assert.equal(shouldCompletePosEnrollment(true, "IdentitySynchronizing"), true);
    assert.equal(shouldCompletePosEnrollment(true, "Synchronizing"), true);
  });

  it("waits only while the unenrolled host is still restarting", () => {
    assert.equal(shouldCompletePosEnrollment(true, "EnrollmentRequired"), false);
    assert.equal(shouldCompletePosEnrollment(false, "LoginRequired"), false);
  });
});
