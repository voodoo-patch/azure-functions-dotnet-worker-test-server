# Example Function App

`ExampleFunction` is the repository's supported reference app for
`Azure.Functions.Worker.TestServer`. It demonstrates an isolated-worker HTTP Function App that
exposes its service and middleware registration through `IFunctionAppStartup`, allowing
`FunctionAppFactory<TProgram>` to host and invoke it in-process.

## Trigger-type organization

Examples are organized by trigger type under this project:

- `Http\Greeter.cs` contains HTTP-triggered functions and input models.
- `Http\HttpExceptionMiddleware.cs`, `Http\ShortCircuitMiddleware.cs`, and
  `Http\StampHttpHeaderMiddleware.cs` contain middleware used by HTTP-triggered examples.
- `Program.cs` remains at the project root and wires host/services/worker middleware.

The companion tests in `test\AzureFunctionsDotNetWorkerTestServer.Tests` demonstrate:

- HTTP GET, constrained-route, query-string, and JSON-body invocation;
- service replacement through a custom `FunctionAppFactory<Program>`;
- normal middleware execution, short-circuiting, and exception handling.

## Run the walkthrough

From the repository root, restore and run the HTTP invocation tests:

```powershell
dotnet restore
dotnet test test\AzureFunctionsDotNetWorkerTestServer.Tests\AzureFunctionsDotNetWorkerTestServer.Tests.csproj --configuration Release --filter "Suite=Http"
```

Read `Program.cs` first to see the testable Function App setup, then see the HTTP and middleware
test areas for the corresponding `FunctionAppFactory<Program>` usage. This sample demonstrates
worker-pipeline component testing only. It does not replace host-level testing for function keys,
trigger listeners, retries, or binding-extension runtime contracts.
