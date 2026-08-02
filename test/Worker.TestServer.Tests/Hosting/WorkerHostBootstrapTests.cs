using ExampleFunction;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Worker.TestServer.Tests.Hosting;

[Trait("Suite", "Hosting")]
public class WorkerHostBootstrapTests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public void Host_BuildsSuccessfully_WithoutGrpcConnection()
    {
        // just verify the container builds and key services resolve
        var workerBuilder = factory.Services.GetService<IFunctionsWorkerApplicationBuilder>();
        Assert.NotNull(workerBuilder);
    }
}
