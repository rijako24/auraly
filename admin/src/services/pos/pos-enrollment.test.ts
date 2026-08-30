import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { posEnrollmentProblemDetail } from "./pos-enrollment-error";

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
