# Invoking HTTP trigger functions through the worker pipeline

**Goal:** Given an `HttpClient` from the phase-2 factory, make a real HTTP request actually run the
real, unmodified function method (middleware included) — no host, no gRPC, no mocking of the
function's own code.

**Commits:** `8bb4110` — *"getting first test invoking a function endpoint via HttpClient to
work"* (first working spike), `9abb519` — *"Refactor Phase 3"* (centralizing the reflection into
`WorkerInternals`, `TestFunctionDefinition`, `TestInvocationStubs`).

This was the biggest conceptual jump so far: everything before this point only proved the DI
container could build. This phase proves an actual `[Function]`-attributed method can run end to
end from a real `HttpRequestMessage`.

## The core problem: two independent pipelines need to meet in the middle

`Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` (the ASP.NET Core HTTP integration
package) works by running **two pipelines concurrently for the same logical request**:

1. The **ASP.NET Core pipeline** — an incoming `HttpContext` flows through ASP.NET Core middleware
   and eventually into `FunctionsHttpProxyingMiddleware`, which needs to hand off to...
2. The **worker pipeline** — `IFunctionsApplication.InvokeFunctionAsync(FunctionContext)`, which
   runs the worker's own middleware chain and ultimately invokes the function method.

In production, these are stitched together over gRPC: the host sends an invocation request, the
worker asks (via `IHttpCoordinator`) for the matching `HttpContext`, and the two rendezvous. There
is no host here, so **we are the thing that has to stitch them together.**

### Resolution: drive both pipelines from the same `DelegatingHandler`, concurrently

`TestClientHandler : DelegatingHandler` (installed via `CreateFunctionsClient()` in
`FunctionAppFactory`) does the stitching:

```csharp
protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
{
    var invocationId = Guid.NewGuid().ToString();
    request.Headers.Add(InvocationIdHeader, invocationId); // "x-ms-invocation-id"

    var functionContext = await BuildFunctionContextAsync(invocationId, request);

    // Start the worker pipeline WITHOUT awaiting it yet.
    var workerTask = WorkerInternals.InvokeFunctionAsync(provider, functionContext);

    // Let the ASP.NET Core pipeline run — this is what actually flows the HttpContext
    // through FunctionsHttpProxyingMiddleware and rendezvous with the worker side
    // via the worker's own DefaultHttpCoordinator (keyed by the invocation-id header).
    var response = await base.SendAsync(request, ct);

    await workerTask; // now safe to await — the function has already run by this point
    return response;
}
```

The key insight: **we never touch `IHttpCoordinator` ourselves.** The worker's own ASP.NET Core
integration package already knows how to find the in-flight `HttpContext` for a given invocation
ID and rendezvous with it — that's exactly what `IHttpCoordinator`/`DefaultHttpCoordinator`
(internal to the worker extension) is for. All we had to do was **not race** the two calls:
kick off the worker task first (so it's waiting/ready), then let `base.SendAsync` drive the
ASP.NET Core side, then await the worker task once the ASP.NET side completes.

The very first spike (`8bb4110`) had commented-out code that tried to call
`IHttpCoordinator.SetFunctionContextAsync`/`CompleteFunctionInvocation` manually — this turned out
to be unnecessary once the concurrent-await shape above was in place; the coordinator wires itself
up as long as both sides are running against the same invocation ID.

## Challenge: matching a request to a function definition

There's no host-side routing table here — routing information only exists in the **build-time
generated function metadata** (`IFunctionMetadataProvider`, produced by the Functions SDK's source
generator). `MatchFunction` in `TestClientHandler` walks all functions' `RawBindings` (raw JSON
per binding), filters to `httpTrigger` bindings, and checks the route/method:

```csharp
var metadataProvider = provider.GetRequiredService<IFunctionMetadataProvider>();
var allMetadata = await metadataProvider.GetFunctionMetadataAsync("");
// ...match by route (falling back to function name) + HTTP method
```

This is also why `FunctionAppFactory.RegisterGeneratedMetadataProvider` (see
[phase 2](02-hosting-the-worker-inside-webapplicationfactory.md)) exists — the function app's generated
provider has to be found by scanning its assembly for a concrete `IFunctionMetadataProvider` and
explicitly re-registered, since the test host doesn't go through the normal generated
`Program`/`FunctionsApplication` bootstrapping that would register it automatically. Similarly,
`FUNCTIONS_APPLICATION_DIRECTORY` has to be set manually (to the function assembly's directory) so
`TestFunctionDefinition.PathToAssembly` resolves correctly.

*(Route matching started as an exact/`StartsWith` string comparison against `/api/{route}`; it was
later replaced with ASP.NET Core's own template matcher — see the "Route template matching"
section in [phase 4](04-binding-request-data-frombody-and-parameter-conversion.md#refactor-pass-hardening-what-already-worked).)*

## Challenge: constructing a `FunctionContext` without the internal factory being public

`FunctionContext` instances are normally created by an internal `IFunctionContextFactory`. Every
internal worker type needed from here on follows the same shape of problem, solved the same way,
so this is where the pattern was established and centralized into **`WorkerInternals.cs`**:

- Resolve the internal `Type` by full name from `typeof(FunctionContext).Assembly` once, statically,
  and cache it (and any `MethodInfo` needed) — avoid repeating `GetType`/`GetMethod` string lookups
  on every request.
- Resolve the actual service instance from DI by that internal `Type` (`provider.GetRequiredService(type)`)
  — DI doesn't care that the type is `internal` from our assembly's perspective, only that it's
  registered.
- Invoke methods via cached `MethodInfo.Invoke(...)`.

```csharp
public static FunctionContext CreateFunctionContext(IServiceProvider provider, IInvocationFeatures features)
{
    var factory = provider.GetRequiredService(FunctionContextFactoryType); // IFunctionContextFactory
    return (FunctionContext)CreateContextMethod.Invoke(factory, [features, CancellationToken.None])!;
}
```

`IFunctionContextFactory.Create` takes an `IInvocationFeatures` bag — our own
`TestInvocationFeatures : IInvocationFeatures` (a plain `Dictionary<Type, object>`-backed
implementation at this point; it gets smarter in phase 4) — plus a `FunctionDefinition` and
`FunctionInvocation` set into that bag beforehand.

## Challenge: `FunctionDefinition` also has to be built from metadata, not gRPC

Production builds `FunctionDefinition` (`GrpcFunctionDefinition`) from a `FunctionLoadRequest`
received over gRPC. We have no gRPC message — only the same `IFunctionMetadata` used for routing.
`TestFunctionDefinition : FunctionDefinition` was introduced to bridge this: it parses
`RawBindings` JSON for `InputBindings`/`OutputBindings`, and uses reflection over the function
method (found via `EntryPoint`, format `"Namespace.ClassName.MethodName"`) to build `Parameters`.//
At this stage, `Parameters` only carried names/types/default values — no attribute metadata yet
(that's a phase-4 concern).

## Challenge: `IFunctionBindingsFeature` is internal and has no public implementation to `new` up

The bindings feature (`Context.Features.IFunctionBindingsFeature`) holds per-invocation state:
trigger metadata, input data, output binding data, and the invocation result (what the output
binding middleware reads to shape the HTTP response). We can't implement an internal interface
directly from our assembly. The chosen workaround: **`DispatchProxy`**.

```csharp
// WorkerInternals.CreateBindingsFeatureProxy
var proxy = DispatchProxyCreateMethod                 // System.Reflection.DispatchProxy.Create<T,TProxy>()
    .MakeGenericMethod(FunctionBindingsFeatureType, typeof(TestFunctionBindingsFeatureProxy))
    .Invoke(null, null)!;
```

`TestFunctionBindingsFeatureProxy : DispatchProxy` intercepts every member access by method name
(`get_TriggerMetadata`, `get_InputData`, `get_OutputBindingData`, `get_OutputBindingsInfo`,
`get_InvocationResult`/`set_InvocationResult`) and returns test-controlled values. Because
`DispatchProxy.Create()` always uses a parameterless constructor, dependencies (like the
pre-computed `OutputBindingsInfo`) can't be constructor-injected — they're set as a mutable
property (`CachedOutputBindingsInfo`) immediately after creation instead.

`OutputBindingsInfo` itself has to be computed by yet another internal service,
`IOutputBindingsInfoProvider.GetBindingsInfo(FunctionDefinition)` — resolved and invoked the same
reflection way and cached onto the proxy before it's wired into the invocation features.

## Outcome

By the end of this phase, `HttpFunctionInvocationTests.Function_CanGet` passed: a real `HttpClient.GetAsync` call
executes the real `Greeter.Run` method (with its real DI-injected `ILogger<Greeter>`), through the
real worker middleware pipeline, and gets back the real `IActionResult`-shaped HTTP response —
with no function parameters beyond the trigger request itself. Parameter *binding* (anything past
the bare `HttpRequest`) was explicitly out of scope here and became the subject of
[phase 4](04-binding-request-data-frombody-and-parameter-conversion.md).

## Reusable pattern established here

Every subsequent "the worker needs some `internal` feature/service we can't reference directly"
problem (and there were several more in phase 4) was solved with the same three-step recipe,
which is now the standing convention for this codebase:

1. Resolve the `Type` by full name from the worker assembly once, statically, in `WorkerInternals`.
2. Resolve the actual instance from DI (`provider.GetRequiredService(type)` /
   `provider.GetService(type)`), or fake it with `DispatchProxy` if there's no DI-registered
   implementation to use.
3. Invoke members via cached `MethodInfo`, never raw reflection scattered through call sites —
   `WorkerInternals` is the single place that knows about internal worker types.
