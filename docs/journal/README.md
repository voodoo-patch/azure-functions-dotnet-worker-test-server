# Implementation Journal

This folder is a running log of the challenges encountered while building the test server, and
the reasoning behind each solution. Unlike the [README](../../README.md) (which describes the
finished shape of the library) or code comments (which describe *what* the code does), this
journal exists to capture *why* it's built this way and *what else was tried*. Revisit these docs
when:

- You're re-entering the codebase after time away and need to rebuild context fast.
- You're deciding whether a piece of reflection-heavy code can be simplified or removed.
- You hit a similar "internal type not resolving" or "feature is null" problem and want to check
  if the same class of issue was already solved elsewhere.
- You're planning a refactor and need to know which decisions were deliberate trade-offs versus
  accidents of history.

## Entries, in build order

1. [Bootstrapping the worker host without gRPC](01-bootstrapping-the-worker-host-without-grpc.md) —
   getting the isolated worker's DI container to build in-process, without a running Functions
   host or gRPC channel.
2. [Hosting the worker inside WebApplicationFactory](02-hosting-the-worker-inside-webapplicationfactory.md) —
   wrapping the host in ASP.NET Core's `WebApplicationFactory<T>` for lifecycle management and
   per-test service overrides.
3. [Invoking HTTP trigger functions through the worker pipeline](03-invoking-http-trigger-functions-through-the-worker-pipeline.md) —
   driving a real invocation end-to-end: matching a request to a function, faking just enough of
   the internal context/features to satisfy the pipeline, and coordinating with the ASP.NET Core
   HTTP integration's concurrency model.
4. [Binding request data: `[FromBody]` and parameter conversion](04-binding-request-data-frombody-and-parameter-conversion.md) —
   making the worker's real input-binding/conversion pipeline run, so attributed parameters
   (starting with `[FromBody]`) get populated the same way they would in production.
5. [Validating middleware short-circuit and exception paths](05-validating-middleware-short-circuit-and-exception-paths.md) —
   closing middleware coverage with harness-focused non-happy path tests and worker registration
   parity between production and test hosts.
6. [Reliability, CI hardening, and version drift guards](06-reliability-ci-hardening-and-version-drift-guards.md) —
   adding deterministic CI defaults, test categorization, fixture model documentation, and package
   drift watchdog assertions.

## Architecture cheat sheet

A quick-reference map of the internal Azure Functions Worker types this library depends on via
reflection (all centralized in `WorkerInternals.cs`), what they're for, and where we first needed
them. If the worker package is upgraded and something breaks, start here.

| Type (all `internal`, worker assembly) | Purpose | Resolved in |
|---|---|---|
| `Pipeline.IFunctionContextFactory` | Creates a `FunctionContext` from an `IInvocationFeatures` bag. | `WorkerInternals.CreateFunctionContext` |
| `IFunctionsApplication` | Entry point that runs the middleware pipeline for a given `FunctionContext` (`InvokeFunctionAsync`). | `WorkerInternals.InvokeFunctionAsync` |
| `Context.Features.IFunctionBindingsFeature` | Per-invocation store for trigger metadata, input data, output binding data, and the invocation result. We fake this with a `DispatchProxy`. | `WorkerInternals.CreateBindingsFeatureProxy`, `TestFunctionBindingsFeatureProxy` |
| `OutputBindings.IOutputBindingsInfoProvider` | Computes the `$return`/output binding shape for a `FunctionDefinition`, needed by the bindings feature. | `WorkerInternals.CreateBindingsFeatureProxy` |
| `IInvocationFeatureProvider` | Generic feature-factory abstraction (`bool TryCreate(Type, out object)`); the worker registers several of these in DI (e.g. `DefaultBindingFeatureProvider` for `IFunctionInputBindingFeature`). | `WorkerInternals.TryCreateFeatureViaInvocationProvider` |
| `Context.Features.IFunctionInputBindingFeature` | Runs `BindFunctionInputAsync`, which converts raw sources into typed method arguments — this is what actually invokes converters. | Resolved lazily via `IInvocationFeatureProvider`, requested from `TestClientHandler` |
| `Context.Features.IInputConversionFeature` | Strongly-typed provider (`IInputConversionFeatureProvider`, not `IInvocationFeatureProvider`) that `DefaultFunctionInputBindingFeature` depends on directly. Discovered structurally, not by a hardcoded provider name — see journal entry 4. | `WorkerInternals.TryCreateFeatureViaProvider` |
| `Extensions.Http.AspNetCore.IFromBodyConversionFeature` | Set by `FunctionsHttpProxyingMiddleware` itself once the ASP.NET Core `HttpContext` is attached; converts request bodies for `[FromBody]` parameters. **We must not set this ourselves.** | N/A — worker sets it automatically once `HttpContext` plumbing is correct |

Key non-obvious facts worth remembering:

- **`FunctionParameter.Properties` is metadata, not values.** It only carries hints that
  converters consult (`converterType`, `bindingAttribute`, etc.); it is built once from
  reflection over the function method's parameters and attributes, mirroring what
  `GrpcFunctionDefinition` does at runtime from gRPC-supplied metadata.
- **`converterType` must be a `string` (`Type.AssemblyQualifiedName`), not a `Type` instance.**
  `DefaultInputConversionFeature` does a `string` type-check before trying to load the converter;
  passing a `Type` silently falls through to "unhandled".
- **Attribute source matters.** `Microsoft.AspNetCore.Mvc.FromBodyAttribute` and
  `Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute` look similar but only the worker one
  carries the `InputConverterAttribute` metadata the conversion pipeline needs.
- **`[FromServices]` is not an isolated-worker concept.** There is no Functions equivalent for
  ASP.NET Core's parameter-level DI attribute; the worker convention is constructor injection.
- **`RawBindings` only reflects Functions bindings** (`httpTrigger`, `$return`, queue/blob/etc.
  bindings) — never ASP.NET Core model-binding attributes like `[FromBody]`. Don't go looking for
  those there.
