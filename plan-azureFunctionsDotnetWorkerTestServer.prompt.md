## Plan: In-Process Isolated Worker Test Harness

Build an in-process test harness that validates .NET isolated worker middleware and HTTP-trigger execution without Azure Functions host or gRPC emulation, using xUnit and ASP.NET Core integration model. Start with a reusable fixture/factory so tests stay concise and scale as middleware/filter coverage grows.

**Steps**
1. Define MVP boundaries and fidelity rules.
- Include: worker-side middleware pipeline execution, function invocation path, request/response shaping, short-circuit behavior, exception translation behavior.
- Exclude: host auth/key checks, route discovery, host-managed binding extension behavior, retries/checkpoint semantics, trigger listeners.
- Pass criteria: deterministic HTTP pipeline invocation in tests, no dependency on Azure Functions Core Tools process.

2. Phase 1: Test project foundation.
- Create/confirm a dedicated test project targeting the same runtime and worker package major version as the function app.
- Pin worker and ASP.NET Core integration package versions to reduce model drift.
- Add xUnit baseline setup and shared test utility conventions.
- Add a smoke test verifying function assembly load + DI bootstrap in test environment.
- Dependency: blocks all later phases.

3. Phase 2: Reusable worker fixture/factory.
- Implement a reusable fixture that builds the same DI + middleware registrations as production startup.
- Centralize host creation/teardown with deterministic lifecycle.
- Add extension points for per-test overrides (mocked services, alternate middleware ordering, feature flags).
- Add guard assertions to verify middleware registration order and catch drift from production.
- Dependency: required before final invocation API; some helper design can start in parallel.

4. Phase 3: HTTP invocation harness.
- Implement request builder abstractions: method, path, headers, query, body, content type, user/context metadata needed by middleware.
- Implement response capture abstraction: status, headers, payload, exception/error envelope.
- Implement a single InvokeAsync-style entrypoint that executes the worker pipeline and returns normalized response.
- Align serializer settings with production (JSON options/casing/null behavior) to avoid false positives.
- Add cancellation/timeout handling to avoid hung tests on deadlocks.
- Dependency: depends on Phase 2.

5. Phase 4: Middleware/filter coverage suite.
- Happy path tests: middleware chain executes, function runs, response is correct.
- Short-circuit tests: middleware returns early and function is not executed.
- Ordering tests: verify A/B/C middleware sequence and side effects.
- Error-path tests: middleware/function exceptions map to expected status/payload/logging behavior.
- Mutation tests: middleware modifications to headers/body/context are visible downstream.
- Optional parity set: compare 1-2 critical scenarios against higher-fidelity integration tests.
- Dependency: depends on Phase 3.

6. Phase 5: Reliability and CI hardening.
- Add test traits/categories for fast pipeline tests vs slower high-fidelity tests.
- Decide fixture isolation model explicitly (shared per class for speed vs isolated per test for purity).
- Normalize CI environment (timezone/culture/env vars) for deterministic behavior.
- Add version drift watchdog assertion for critical worker package major versions.
- Document limitations and escalation path to host-level integration tests.

**Relevant files**
- Not enumerated yet because no workspace is currently open.
- Once a repository is open, map this to concrete files for: test project, fixture/factory, request/response builders, middleware suite, and CI config.

**Verification**
1. Run full xUnit suite repeatedly and confirm deterministic pass results.
2. Validate at least one short-circuit and one exception-path test proving middleware behavior independently of Azure Functions host.
3. Validate one serialization-sensitive endpoint with representative payload to confirm production JSON parity.
4. Validate CI outcome matches local outcome for harness test category.
5. Confirm excluded host behaviors are not asserted in harness tests.

**Decisions Captured**
- Test framework: xUnit.
- HTTP model: ASP.NET Core integration types.
- MVP scope: HTTP-only.
- Construction style: reusable fixture/factory from day 1.
- Principle: optimize deterministic worker-pipeline coverage over host parity.
