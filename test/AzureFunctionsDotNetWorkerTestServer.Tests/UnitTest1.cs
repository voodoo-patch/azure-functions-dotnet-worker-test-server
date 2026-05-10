using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

public class UnitTest1
{
    
    [Fact]
    public void Host_BuildsSuccessfully_WithoutGrpcConnection()
    {
        // just verify the container builds and key services resolve
        var app = TestHostBuilder.Build();

        var workerBuilder = app.Services.GetService<IFunctionsWorkerApplicationBuilder>();
        Assert.NotNull(workerBuilder);
    }
}

public class TestHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // No-op
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No-op
        return Task.CompletedTask;
    }
}