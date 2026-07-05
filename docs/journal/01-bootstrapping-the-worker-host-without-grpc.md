# Bootstrapping the worker host without gRPC

**Goal:** Prove that the isolated worker's DI container (all the services registered by
`ConfigureFunctionsWebApplication()`/`AddFunctionsWorkerDefaults()`) can be built and resolved
in-process, without ever starting the gRPC channel that normally connects the worker to the
Azure Functions host.

**Commit:** `1cebd33` — *"phase 1: add demo function and create smoke test to verify host builds
without gRPC"*

## Why this matters

Every later phase depends on this one fact being true: **the worker's service registrations are
just a normal `IServiceCollection`/`IHost`, and nothing forces a live gRPC connection at container
build time.** If that weren't true, none of the reflection-based invocation plumbing built in
later phases would be possible — we'd be stuck needing a real host process.

## Challenge: the worker tries to connect to a Functions host on startup

Calling `Host.CreateDefaultBuilder().ConfigureFunctionsWebApplication().Build()` (or the
`FunctionsApplication.CreateBuilder(args).Build()` production equivalent) registers an
`IHostedService` (internally `WorkerHostedService`) that, once the host **runs**, opens a gRPC
connection back to a Functions host process. In a test process there is no host to connect to, so
letting that hosted service start would hang or throw.

### Investigation

- The gRPC connection is only *initiated* by the hosted service's `StartAsync`, not by DI
  container construction. This means `Build()` alone is safe — only `Run()`/`StartAsync()` is
  dangerous.
- The worker still needs *some* value for `Functions:Worker:HostEndpoint` at configuration-bind
  time, or host construction throws a configuration-guard exception before we even get that far.

### Resolution

1. Supply a dummy `--Functions:Worker:HostEndpoint=http://localhost:0/` command-line argument so
   configuration binding succeeds without needing a real endpoint.
2. After building the `IServiceCollection` (before `Build()`), find and remove the
   `WorkerHostedService` descriptor by matching `ImplementationType?.Name == "WorkerHostedService"`
   — this is an internal type, so it's matched by name rather than by a strong type reference.
3. Build the host and resolve `IFunctionsWorkerApplicationBuilder` from `Services` as a smoke test
   — if that resolves, the whole worker DI graph came up correctly.

```csharp
// test/.../TestHostBuilder.cs (superseded in phase 2, but this is where the pattern started)
var builder = WebApplication.CreateBuilder(["--Functions:Worker:HostEndpoint=http://localhost:0/"]);
builder.Services.AddFunctionsWorkerDefaults();

var hostedServiceDescriptor = builder.Services
    .FirstOrDefault(d => d.ImplementationType?.Name == "WorkerHostedService");
if (hostedServiceDescriptor != null)
    builder.Services.Remove(hostedServiceDescriptor);

return builder.Build(); // no gRPC connection attempted
```

## Outcome

A single smoke test (`Host_BuildsSuccessfully_WithoutGrpcConnection`) proved the container builds
and `IFunctionsWorkerApplicationBuilder` resolves. This "remove `WorkerHostedService` by name"
trick is still in use today (see `FunctionAppFactory.RemoveWorkerHostedService`), just relocated
into the reusable factory built in the next phase.

## Carried forward

- The `--Functions:Worker:HostEndpoint=...` dummy argument.
- The by-name removal of `WorkerHostedService` (name-matching internal types recurs constantly in
  this codebase — see the cheat sheet in the journal [README](README.md)).
