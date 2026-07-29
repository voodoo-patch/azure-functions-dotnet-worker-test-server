# Contributing

Thank you for improving Azure.Functions.Worker.TestServer. This project provides an in-process
test harness for HTTP-triggered Azure Functions isolated-worker applications; changes should
preserve that focused scope.

## Prerequisites

- .NET SDK 10.0 or later
- Git

## Local workflow

1. Fork the repository and create a focused branch from `main`.
2. Restore and build the solution:

   ```powershell
   dotnet restore
   dotnet build azure-functions-dotnet-worker-test-server.slnx --configuration Release --no-restore
   ```

3. Run the full test suite:

   ```powershell
   dotnet test test\AzureFunctionsDotNetWorkerTestServer.Tests\AzureFunctionsDotNetWorkerTestServer.Tests.csproj --configuration Release --no-build
   ```

4. When changing a library project, verify its packages:

   ```powershell
   dotnet pack azure-functions-dotnet-worker-test-server.slnx --configuration Release --no-build --output artifacts\packages
   ```

## Issues and pull requests

- Search existing issues before opening a new one. Bug reports should include the target framework,
  Azure Functions Worker package versions, a minimal reproduction, expected behavior, and actual
  behavior.
- Open an issue before starting a large API, architecture, or scope change so maintainers can
  confirm the direction.
- Keep pull requests focused. Explain the problem, approach, user-visible impact, and validation
  performed. Link the relevant issue where one exists.
- Do not include secrets, connection strings, private application code, or other sensitive data in
  issues, pull requests, samples, or test fixtures.

## Code and test conventions

- Follow `.editorconfig` and the existing nullable-enabled, file-scoped C# style.
- Keep reflection against Azure Functions Worker internals centralized in `WorkerInternals.cs`.
- Test files are organized by behavior area, not development milestones.
- Add or update a focused test for each behavior change. Sample code must remain safe to run
  locally without credentials.

## Community and security

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). For general
help, use [GitHub Issues](SUPPORT.md). Do not report vulnerabilities in a public issue; follow
the private reporting instructions in [SECURITY.md](SECURITY.md).

## Release publishing

The `Publish NuGet packages` workflow publishes when a protected tag named
`v<semantic-version>` is pushed. Before creating a release tag, ensure that GitHub private
vulnerability reporting is enabled, the `NUGET_API_KEY` repository secret is configured, and the
workflow's generated package artifacts pass validation. The workflow obtains repository metadata
from GitHub at run time; no local git remote is required to create a release artifact.
