# Validating middleware short-circuit and exception paths

**Goal:** Close Phase 4 with harness-focused middleware coverage that proves non-happy execution
flows run correctly through the in-process worker pipeline.

After Phase 3, we already had proof that a middleware could execute on the happy path. The
remaining risk for the harness was not "does Azure Functions middleware work," but rather "does our
request-to-invocation bridge remain stable when middleware exits early or handles downstream
exceptions?"

## Challenge: middleware registration parity between production and tests

A middleware was registered in `Program.Main` with `builder.UseWhen(...)`, but it did not run in
tests. Root cause: `Program.Main` is not executed by `FunctionAppFactory<TProgram>`, so worker
pipeline registration done only there is invisible to the test host.

## Resolution: explicit startup contract for worker pipeline wiring

The app bootstrap contract was expanded to include worker registration:

- `IFunctionAppStartup.ConfigureServices(IServiceCollection services)`
- `IFunctionAppStartup.ConfigureWorker(IFunctionsWorkerApplicationBuilder builder)`

`FunctionAppFactory<TProgram>` now calls `ConfigureFunctionsWebApplication(TProgram.ConfigureWorker)`
so test host and production host share the same middleware registration path.

## Harness-focused Phase 4 scenarios added

The suite now covers three high-value middleware paths:

1. Happy path: middleware runs on a normal HTTP trigger call (`X-Stamp` header assertion).
2. Short-circuit path: middleware returns HTTP 418 without calling `next`, proving the function is
   not executed (`X-Function-Executed` is absent).
3. Exception path: function throws, outer middleware catches and maps to deterministic HTTP 500
   payload.

This validates the harness behavior we own (wiring, execution, and completion paths) without
duplicating broad runtime-level testing.

## Outcome

- Phase 4 middleware coverage goals are complete for the planned scope.
- Middleware registration parity is now explicit and reusable.
- The test suite now guards against regressions where invocation hangs, bypasses middleware, or
  loses middleware-produced responses on non-happy paths.
