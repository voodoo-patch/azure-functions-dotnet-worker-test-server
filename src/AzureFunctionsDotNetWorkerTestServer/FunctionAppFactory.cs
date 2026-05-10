using AzureFunctionsDotNetWorkerTestServer.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AzureFunctionsDotNetWorkerTestServer;

public class FunctionAppFactory<TProgram>
    : WebApplicationFactory<TProgram>
    where TProgram : class, IFunctionAppServices
{
    private readonly Action<IServiceCollection>? _serviceOverrides;

    public FunctionAppFactory()
    {
    }

    protected FunctionAppFactory(Action<IServiceCollection>? serviceOverrides = null)
    {
        _serviceOverrides = serviceOverrides;
    }

    protected override IHostBuilder CreateHostBuilder()
    {
        // Your custom host — Program is just used as assembly marker
        var hostBuilder = Host
            .CreateDefaultBuilder(["--Functions:Worker:HostEndpoint=http://localhost:0/"])
            .ConfigureFunctionsWebApplication();

        return hostBuilder;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .ConfigureServices(services =>
            {
                var hostedServiceDescriptor = services
                    .First(d => d.ImplementationType?.Name == "WorkerHostedService");
                services.Remove(hostedServiceDescriptor);

                // Register Program's services by calling the ConfigureServices method
                TProgram.ConfigureServices(services);

                // overrides run AFTER all registrations
                _serviceOverrides?.Invoke(services);
            });
    }
}