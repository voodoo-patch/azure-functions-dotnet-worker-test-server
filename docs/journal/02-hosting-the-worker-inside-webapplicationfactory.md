# Hosting the worker inside WebApplicationFactory

**Goal:** Turn the one-off host-building smoke test from phase 1 into a reusable, `xUnit`-friendly
fixture that supports per-test-class DI overrides (mocking services) — the "`WebApplicationFactory`
for Azure Functions" experience described in the project's [README](../../README.md).

**Commit:** `5181d42` — *"phase 2: add WebApplicationFactory integration"*

## Why `WebApplicationFactory<T>`?

The worker's ASP.NET Core HTTP integration (`Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`)
means the worker host **is** an ASP.NET Core app under the hood — it has a real `IWebHostBuilder`
pipeline. That means the standard ASP.NET Core integration testing tool,
`Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<TEntryPoint>`, applies almost unmodified:
it gives us `TestServer`-backed lifecycle management, an in-memory `HttpClient`, and a
`ConfigureWebHost` extension point for swapping services — all for free.

## Challenge: `TProgram` needs a way to expose its service registrations

`WebApplicationFactory<TEntryPoint>` expects `TEntryPoint` to be the marker type for a real ASP.NET
Core `Program`. But Azure Functions isolated worker apps don't have a conventional
`Startup`/`ConfigureServices` split — everything happens inline in `Program.Main`, which the
factory cannot call (it calls `CreateHostBuilder`/`ConfigureWebHost`, not `Main`).

### Resolution: `IFunctionAppServices` contract

A tiny abstraction (`src/AzureFunctionsDotNetWorkerTestServer.Abstractions/IFunctionAppServices.cs`)
was introduced:

```csharp
public interface IFunctionAppServices
{
    static abstract void ConfigureServices(IServiceCollection services);
}
```

Function apps under test implement this on their `Program` class and extract their service
registration into a static method that both `Main` (production) and the test factory (via
`TProgram.ConfigureServices(services)`, using a C# static abstract interface member) can call. This
keeps production and test DI registration as a single source of truth — no duplicated or
drifting service lists between "real" `Program.cs` and test setup.

```csharp
// demo/ExampleFunction/Program.cs
public class Program : IFunctionAppServices
{
    public static void Main(string[] args) { /* ...calls ConfigureServices(builder.Services)... */ }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDummyService, DummyService>();
    }
}
```

## Challenge: relocating the phase-1 host-building logic into the factory's extension points

`WebApplicationFactory<T>` exposes two overridable hooks:
- `CreateHostBuilder()` — where the phase-1 `Host.CreateDefaultBuilder(...).ConfigureFunctionsWebApplication()`
  logic moved.
- `ConfigureWebHost(IWebHostBuilder)` — where `WorkerHostedService` removal and
  `TProgram.ConfigureServices(services)` registration now happen, alongside an optional
  `Action<IServiceCollection>` for **per-test-class overrides** (e.g. swapping a service for a
  mock), applied *after* the app's own registrations so overrides always win.

```csharp
public class FunctionAppFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class, IFunctionAppServices
{
    private readonly Action<IServiceCollection>? _serviceOverrides;

    protected FunctionAppFactory(Action<IServiceCollection>? serviceOverrides = null)
        => _serviceOverrides = serviceOverrides;

    protected override IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder(["--Functions:Worker:HostEndpoint=http://localhost:0/"])
            .ConfigureFunctionsWebApplication();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            RemoveWorkerHostedService(services);
            TProgram.ConfigureServices(services);
            _serviceOverrides?.Invoke(services);
        });
    }
}
```

Test classes that need mocked services derive a small factory subclass:

```csharp
public class FunctionAppFactoryWithMockServices()
    : FunctionAppFactory<Program>(services => services.ReplaceService<IDummyService, MockDummyService>())
{
    public class MockDummyService : IDummyService { /* ... */ }
}
```

`ReplaceService<TService, TReplacement>` (`ServiceCollectionExtensions.cs`) removes the existing
registration and re-adds it with the same lifetime, so overrides don't accidentally change
singleton/scoped/transient semantics.

## Outcome

The dependency-injection test area verifies both that:
1. A factory with no overrides exposes the app's real services unmodified
   (`Host_BuildsAndContainsProgramServices`).
2. A factory with overrides swaps the service as expected once the host is built
   (`Host_ServicesCanBeMocked`).

This is the reusable fixture the plan's Phase 2 ("Reusable worker fixture/factory") called for —
and it's what every later invocation test builds on top of.

## Later addition (carried in from phase 3+): generated metadata provider registration

Once phase 3 needed to resolve HTTP routes from build-time metadata, `FunctionAppFactory` also
gained `RegisterGeneratedMetadataProvider`, which finds the source-generated
`IFunctionMetadataProvider` implementation in the function app's assembly and re-registers it
(replacing any default). This is documented in
[phase 3](03-invoking-http-trigger-functions-through-the-worker-pipeline.md) since it's really part
of the invocation-matching problem, but it lives in `FunctionAppFactory.cs` alongside the phase-2
code above.
