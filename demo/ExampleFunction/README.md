# Example Function App

`ExampleFunction` is the repository's supported reference app for
`Azure.Functions.Worker.TestServer`. It demonstrates an isolated-worker HTTP Function App that
exposes its service and middleware registration through `IFunctionAppStartup`, allowing
`FunctionAppFactory<TProgram>` to host and invoke it in-process.

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
