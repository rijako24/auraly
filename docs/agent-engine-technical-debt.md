# Agent Engine Technical Debt

## Fact capture and service resolution

Current decision: keep `set_fact` and `resolve_service_selection` as separate tools.

Why:
- `set_fact` persists scalar facts from the tenant fact schema.
- `resolve_service_selection` resolves user wording against the active service catalog before persisting `booking.service`.
- Merging catalog resolution into `set_fact` would make a generic persistence tool depend on catalog lookup behavior, ambiguity handling, and service-specific recovery.
- The current split is more explicit for the LLM and safer for deterministic flow gating, as long as prompts and stage `allowedActions` mention the exact tool names.

Known friction:
- The LLM sees two write-capable tools, which can feel redundant.
- A mistaken `set_fact(service)` call requires a recovery response that points to `resolve_service_selection`.
- Tests and seeds must keep guidance consistent: `service` uses `resolve_service_selection`; other user facts use `set_fact`.

Future clean design, if we decide to simplify:
- Introduce a generic fact writer pipeline with per-role resolvers.
- `set_fact` would accept a fact key and value, then delegate by fact role:
  - `booking.service` -> service catalog resolver.
  - `booking.addons` -> add-on validator.
  - date/time/customer facts -> scalar validation only.
- The delegation must be declarative by fact role, not hardcoded by tenant or business.
- The response shape must preserve explicit recovery actions for ambiguous/not-found service selections.
- Remove `resolve_service_selection` only after updating seeds, prompt guidance, integration tests, console critical flow, and test mocks in one migration.

Recommendation:
- Do not merge the tools in the current stabilization pass.
- Keep the guard in `set_fact` that rejects `booking.service` writes and redirects to `resolve_service_selection`.
- Revisit only if tool count or LLM confusion remains a measurable problem after current console/integration scenarios are stable.

## Flow routing and post-reservation management

Current decision: use flow routing to enter reservation management only when the customer clearly asks to manage an existing reservation.

Debt:
- Add more console scenarios for ambiguous follow-ups after a long pause.
- Keep the default route as booking/discovery unless a route explicitly resolves to another flow.
- Avoid global-action keywords that interrupt booking on weak wording.

## Catalog search

Current decision: search/filter services at repository level when the customer provides a query, and fall back to category overview when no service-level match exists.

Debt:
- Add repository-level integration coverage against SQL for search term behavior and result limits.
- Monitor query shape/performance as catalogs grow.


## Pending production hardening points

The following items came from the PR/design review and should stay visible beyond the tool-split discussion.

### P0 - Keep stable before publish

- Console critical flow must remain the release gate for Luis. It should keep covering booking happy path, payment summary regeneration, post-payment reservation changes, service-change escalation, flow switching, and return to booking after a new greeting.
- Integration scenarios must remain at 51/51 or better before publishing. If a new deterministic rule is added, add a scenario before deploying.
- `set_fact(service)` must stay blocked while `resolve_service_selection` exists. Otherwise the engine can persist non-canonical service names and break pricing, availability, checkout, and add-ons.
- Checkout and availability verifications must stay dependency-based. If service/date/time/add-ons change, stale summaries must not be reused silently.

### P1 - Clean architecture follow-ups

- Review large orchestration methods in `AgentConversationService`, `FlowRuntimeOrchestrator`, `FlowPolicyEngine`, and reservation tools. Prefer extracting narrow services only where behavior is already covered by tests.
- Keep flow routing generic: the primary flow comes from flow.type=primary; only explicit route matches should move to reservation management or another flow.
- Replace any remaining keyword-heavy routing with configurable route intents or entry-action conditions only when the condition is tenant-neutral and testable.
- Keep stage `ask` content inside conversational guidance unless there is a clear deterministic UI/payload reason to keep it separate.
- Review `priority` semantics. If priority does not affect execution order or conflict resolution, remove it from configuration to avoid false confidence.

### P1 - Catalog and pricing

- Catalog service search should continue filtering in the repository/database layer when a query exists, with bounded terms and bounded result count.
- Price display must always use the reusable catalog pricing service so promotions, base price, and future pricing rules stay centralized.
- Add SQL-backed integration coverage for catalog search, not only in-memory tests, before relying on large tenant catalogs.

### P1 - Flow management after long pauses

- Add console scenarios for: customer starts reservation-management wording, disappears, then later sends only a time; customer greets later and starts a new booking; existing reservation no longer qualifies as active.
- The engine should not ask "continue change or start new request?" by default. It should route transparently when there is enough signal and fall back to booking when there is not.

### P2 - Configuration ergonomics

- Reduce duplicated wording between `conversationGuidance`, `allowedActions`, and entry-action explanations.
- Keep exact tool names in guidance while tools are directly exposed to the LLM.
- Avoid business-specific phrases in engine code. Tenant-specific wording belongs in seed/configuration.

### P2 - Test data and readability

- Normalize test strings to ASCII where accents are not part of the assertion. This avoids encoding noise in diffs and console output.
- Keep Luis console scenarios named around behavior, not incident numbers, except when preserving a known regression history is useful.
- Add a small PR checklist for release review: no hardcoded tenant names in engine, no unknown allowed actions, no stale checkout reuse, no hidden catalog full scans.
