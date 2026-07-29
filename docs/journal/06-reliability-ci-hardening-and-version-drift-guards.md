# Reliability, CI hardening, and version drift guards

**Goal:** Close Phase 5 by making the suite deterministic in CI, explicit about test scope/speed,
and resilient to package version drift across projects.

## Why this phase matters

By Phase 4, function invocation and middleware behavior were already covered. The remaining risks
were operational:

- tests passing locally but failing in CI due to environment differences,
- silent dependency skew between demo app, library, abstractions, and tests,
- unclear separation between fast harness tests and any future slower high-fidelity suites.

## Reliability changes implemented

1. **Version drift watchdog tests**
   - Added `WorkerPackageCompatibilityTests` to assert:
     - critical Azure Functions worker packages remain on supported major version `2.x`,
     - worker package versions are aligned across app/library/abstractions/tests.
   - This makes dependency skew fail loudly during test execution.

2. **Deterministic CI workflow**
   - Added `.github/workflows/ci.yml` with:
     - stable .NET setup and restore/test steps,
     - normalized environment (`TZ=UTC`, `LANG/LC_ALL=en_US.UTF-8`),
     - deterministic .NET CLI settings (`DOTNET_NOLOGO`, telemetry/first-run flags).

3. **Explicit fixture model and escalation guidance**
   - Documented that harness tests use `IClassFixture<FunctionAppFactory<TProgram>>` (one host per
     class), balancing determinism with speed.
   - Documented when to escalate to host-level integration tests for out-of-scope runtime behavior.

## Outcome

- Phase 5 goals are complete for this repository's planned scope.
- CI now encodes deterministic defaults and a clear fast-test path.
- Dependency drift that could invalidate harness fidelity is guarded by dedicated tests.
