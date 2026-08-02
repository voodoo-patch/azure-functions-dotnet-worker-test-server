# Azure.Functions.Worker.TestServer

A testing library for **.NET Azure Functions isolated worker** that enables component-level testing of HTTP trigger functions — including middleware pipelines — without running the Azure Functions host or `func.exe`.

---

## Overview

Testing Azure Functions in the isolated worker model traditionally requires either the Azure Functions Core Tools host (slow, process-based, hard to parallelise) or hand-rolled mocks that don't exercise the real worker pipeline. This library takes a different approach: it boots the **worker pipeline in-process** inside a standard ASP.NET Core `TestServer`, resolves function invocations using the **build-time generated metadata**, and drives the real `IFunctionsApplication` middleware stack — all without gRPC or a host process.

The result is a test experience similar to `WebApplicationFactory<T>` from ASP.NET Core: fast, deterministic, in-process, and with full control over the DI container.

---

## How It Works

```
Test code
  │
  ├── HttpClient  (created by FunctionAppFactory)
  │       │
  │       └── TestClientHandler  (DelegatingHandler)
  │               │
  │               ├── 1. Reads build-time function metadata (IFunctionMetadataProvider)
  │               ├── 2. Matches the request URL + HTTP method to a function
  │               ├── 3. Builds a FunctionContext (definition, invocation, bindings feature)
  │               └── 4. Calls IFunctionsApplication.InvokeFunctionAsync → worker middleware pipeline
  │
  └── ASP.NET Core TestServer  (hosts the worker's ASP.NET Core integration layer)
```

Key design points:

- **Metadata-driven routing** — function routes are read from the source-generated `IFunctionMetadataProvider` (the same JSON used at runtime), so the harness stays in sync with the function definitions automatically.
- **Internal pipeline fidelity** — the harness calls the real `IFunctionsApplication.InvokeFunctionAsync`, which executes every registered worker middleware in production order.
- **Reflection boundary** — all access to `internal` Azure Functions Worker types is centralised in `WorkerInternals.cs`, making it easy to adapt when worker internals change across package versions.
- **DI override** — `FunctionAppFactory<TProgram>` inherits from `WebApplicationFactory<TProgram>`, so standard `ConfigureServices` overrides work for mocking/replacing services per test class.

---

## Quick Start

### 1. Implement `IFunctionAppStartup` in your `Program`

```csharp
// In your function app's Program.cs
public class Program : IFunctionAppStartup
{
    public static void Main(string[] args) 
    {        
        var builder = FunctionsApplication.CreateBuilder(args);
        
        ConfigureServices(builder.Services);
        ConfigureWorker(builder);
        
        ...
    }

    // Extract service registration into a static method so the test harness can reuse it.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMyService, MyService>();
    }

    // Register worker middleware used by your function app.
    public static void ConfigureWorker(IFunctionsWorkerApplicationBuilder builder)
    {
        builder.UseMiddleware<MyMiddleware>();
    }
}
```

### 2. Write a test

```csharp
public class MyFunctionTests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public async Task Get_ReturnsExpectedResponse()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/my-function");

        response.EnsureSuccessStatusCode();
        Assert.Equal("expected body", await response.Content.ReadAsStringAsync());
    }
}
```

### 3. Override services per test class

```csharp
public class MyFunctionTestsWithMocks(MyFunctionFactoryWithMocks factory)
    : IClassFixture<MyFunctionFactoryWithMocks>
{
    [Fact]
    public async Task Get_UsesMockedService()
    {
        var client = factory.CreateFunctionsClient();
        var response = await client.GetAsync("/api/my-function");
        response.EnsureSuccessStatusCode();
    }
}

public class MyFunctionFactoryWithMocks()
    : FunctionAppFactory<Program>(services =>
        services.ReplaceService<IMyService, MockMyService>());
```

---

## Project Structure

```
src/
  Azure.Functions.Worker.TestServer/          # Core library
    FunctionAppFactory.cs                        # WebApplicationFactory<T> subclass; entry point for test authors
    TestClientHandler.cs                         # DelegatingHandler; routes HTTP requests through the worker pipeline
    TestFunctionDefinition.cs                    # FunctionDefinition built from source-generated metadata
    TestInvocationFeatures.cs                    # IInvocationFeatures implementation for test contexts
    TestInvocationStubs.cs                       # Stubs: FunctionInvocation, TraceContext, BindingMetadata, bindings proxy
    WorkerInternals.cs                           # Reflection gateway to internal worker types (single source of truth)
    ServiceCollectionExtensions.cs               # ReplaceService<> helper for DI overrides

  Azure.Functions.Worker.TestServer.Abstractions/
    IFunctionAppStartup.cs                       # Interface function apps implement to expose service + worker pipeline setup

demo/
  ExampleFunction/                               # Supported reference Function App
    README.md                                    # Setup and test walkthrough

test/
  Azure.Functions.Worker.TestServer.Tests/    # Test suite organized by behavior area
    Hosting/                                     # Host bootstrap coverage
    DependencyInjection/                         # Application services and override coverage
    Http/                                        # HTTP routing and binding coverage
    Middleware/                                  # Middleware pipeline coverage
    Packaging/                                   # Dependency and packaging compatibility checks
    TestInfrastructure/                          # Shared test fixtures
```

---

## Scope and Limitations

**In scope:**
- Worker-side middleware pipeline execution
- HTTP trigger function invocation
- Request/response shaping and content negotiation
- DI container configuration and per-test overrides
- Parameter binding for `HttpRequest`, route/query values, and `[FromBody]`

**Out of scope (by design):**
- Host-level auth/function key checks
- Route discovery outside of HTTP triggers
- Host-managed binding extension behaviour (queues, blobs, Service Bus, etc.)
- Retry/checkpoint semantics
- Trigger listeners and long-running trigger polling

For scenarios outside this scope, use the Azure Functions Core Tools or a hosted integration test environment.

---

## Requirements

- .NET 10+
- `Microsoft.Azure.Functions.Worker` 2.x
- `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` 2.x
- `Microsoft.AspNetCore.Mvc.Testing` 10.x
- xUnit (for the test suite; the library itself is test-framework-agnostic)

## Installation

Install the core package in a test project:

```powershell
dotnet add package Azure.Functions.Worker.TestServer
```

The package brings the companion abstractions package transitively. Your Function App must use the
isolated worker model and expose its service and worker configuration through
`IFunctionAppStartup`, as shown above.

For a clone of this repository, use the following commands:

```powershell
dotnet restore
dotnet build azure-functions-dotnet-worker-test-server.slnx --configuration Release --no-restore
dotnet test test\Azure.Functions.Worker.TestServer.Tests\Azure.Functions.Worker.TestServer.Tests.csproj --configuration Release --no-build
```

See the [Example Function walkthrough](demo/ExampleFunction/README.md) for a runnable reference
app and the tests that invoke it.

---

## Stability

The library currently supports in-process component testing for HTTP-triggered isolated-worker
Function Apps and their worker middleware. The documented non-goals below remain intentionally
unsupported. The test suite is organized by behavior area and runs in CI on each pull request and
push to `main`.

---

## Reliability and CI Conventions

- **Fixture isolation model**: tests use `IClassFixture<FunctionAppFactory<TProgram>>`, sharing one
  host instance per test class for deterministic startup behavior and faster execution.
- **Version drift watchdog**: package compatibility tests assert critical worker package majors (`2.x`) and
  cross-project version alignment for `Microsoft.Azure.Functions.Worker` and
  `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`.
- **Deterministic CI environment**: workflow sets timezone/culture (`TZ=UTC`, `en_US.UTF-8`) and
  stable .NET CLI flags before building, testing, and packing the libraries.

---

## Known Non-Goals

- Parameter-level `[FromServices]` injection is not targeted in this harness. Use constructor injection or `FunctionContext.InstanceServices` in isolated-worker functions.
- Host-level behaviors (auth keys, trigger listeners, retries/checkpoint orchestration) remain out of scope.

## Escalation Path for Host-Level Behaviors

When a scenario depends on host-managed runtime semantics (for example function keys/auth,
extension listener behavior, trigger checkpoint/retry orchestration, or binding extension runtime
contracts), run those assertions in a higher-fidelity suite with Azure Functions Core Tools or a
deployed integration environment rather than this in-process harness.

## Implementation Journal

The [`docs/journal/`](docs/journal/README.md) folder tracks the challenges hit while building this
library and the reasoning behind each solution — a narrative companion to the code, useful when
revisiting internals, diagnosing a similar "internal type not resolving" issue, or planning a
refactor. It also includes an architecture cheat sheet of the internal worker types this library
depends on via reflection.

## Contributing and support

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening an issue or pull request. Community support
is provided on a best-effort basis through [GitHub Issues](SUPPORT.md). For a vulnerability, do
not open a public issue; use the private reporting process in [SECURITY.md](SECURITY.md).

This project is licensed under the [Apache License 2.0](LICENSE).
