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
  AzureFunctionsDotNetWorkerTestServer/          # Core library
    FunctionAppFactory.cs                        # WebApplicationFactory<T> subclass; entry point for test authors
    TestClientHandler.cs                         # DelegatingHandler; routes HTTP requests through the worker pipeline
    TestFunctionDefinition.cs                    # FunctionDefinition built from source-generated metadata
    TestInvocationFeatures.cs                    # IInvocationFeatures implementation for test contexts
    TestInvocationStubs.cs                       # Stubs: FunctionInvocation, TraceContext, BindingMetadata, bindings proxy
    WorkerInternals.cs                           # Reflection gateway to internal worker types (single source of truth)
    ServiceCollectionExtensions.cs               # ReplaceService<> helper for DI overrides

  AzureFunctionsDotNetWorkerTestServer.Abstractions/
    IFunctionAppStartup.cs                       # Interface function apps implement to expose service + worker pipeline setup

demo/
  ExampleFunction/                               # Reference function app used by the test suite

test/
  AzureFunctionsDotNetWorkerTestServer.Tests/    # Test suite covering each build phase
    Phase1Tests.cs                               # Assembly load and DI bootstrap smoke tests
    Phase2Tests.cs                               # Fixture lifecycle and DI override verification
    Phase3Tests.cs                               # HTTP invocation harness tests
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

---

## Status

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Test project foundation | ✅ Complete |
| 2 | Reusable worker fixture/factory | ✅ Complete |
| 3 | HTTP invocation harness | ✅ Complete |
| 4 | Middleware/filter coverage suite | ✅ Complete |
| 5 | Reliability and CI hardening | ⬜ Planned |

---


## Known Non-Goals

- Parameter-level `[FromServices]` injection is not targeted in this harness. Use constructor injection or `FunctionContext.InstanceServices` in isolated-worker functions.
- Host-level behaviors (auth keys, trigger listeners, retries/checkpoint orchestration) remain out of scope.

## Implementation Journal

The [`docs/journal/`](docs/journal/README.md) folder tracks the challenges hit while building this
library and the reasoning behind each solution — a narrative companion to the code, useful when
revisiting internals, diagnosing a similar "internal type not resolving" issue, or planning a
refactor. It also includes an architecture cheat sheet of the internal worker types this library
depends on via reflection.

