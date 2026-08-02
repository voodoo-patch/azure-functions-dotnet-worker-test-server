using Azure.Functions.Worker.TestServer.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Worker.TestServer;

public class FunctionAppFactory<TProgram>
    : WebApplicationFactory<TProgram>
    where TProgram : class, IFunctionAppStartup
{
    private readonly Action<IServiceCollection>? _serviceOverrides;

    // Required for IClassFixture<T> to work with xUnit, but not intended for direct use.
    public FunctionAppFactory()
    {
    }

    protected FunctionAppFactory(Action<IServiceCollection>? serviceOverrides = null)
    {
        _serviceOverrides = serviceOverrides;
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> pre-configured to drive the Functions worker pipeline
    /// for each request, enabling end-to-end in-process testing of HTTP trigger functions.
    /// </summary>
    /// <param name="timeout">The timeout for each function invocation. Defaults to 10 seconds if not specified.</param>
    public HttpClient CreateFunctionsClient(TimeSpan? timeout = null)
        => CreateDefaultClient(new TestClientHandler(Services, timeout ?? TimeSpan.FromSeconds(10)));

    protected override IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder(["--Functions:Worker:HostEndpoint=http://localhost:0/"])
            .ConfigureFunctionsWebApplication(TProgram.ConfigureWorker);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable(
            "FUNCTIONS_APPLICATION_DIRECTORY",
            Path.GetDirectoryName(typeof(TProgram).Assembly.Location));

        builder.ConfigureServices(services =>
        {
            RemoveWorkerHostedService(services);
            RegisterGeneratedMetadataProvider(services);

            TProgram.ConfigureServices(services);
            _serviceOverrides?.Invoke(services);
        });
    }

    private static void RemoveWorkerHostedService(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ImplementationType?.Name == "WorkerHostedService");
        if (descriptor is not null)
            services.Remove(descriptor);
    }

    private static void RegisterGeneratedMetadataProvider(IServiceCollection services)
    {
        var metadataProviderType = typeof(TProgram).Assembly.GetTypes()
            .FirstOrDefault(t => t.IsAssignableTo(typeof(IFunctionMetadataProvider))
                                 && t is { IsInterface: false, IsAbstract: false });

        if (metadataProviderType is not null)
        {
            services.RemoveAll<IFunctionMetadataProvider>();
            services.AddSingleton(typeof(IFunctionMetadataProvider), metadataProviderType);
        }
    }
}
