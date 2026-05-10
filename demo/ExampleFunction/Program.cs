using AzureFunctionsDotNetWorkerTestServer.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExampleFunction;

public class Program : IFunctionAppServices
{
    public static void Main(string[] args)
    {
        var builder = FunctionsApplication.CreateBuilder(args);

        builder.ConfigureFunctionsWebApplication();

        builder.Services
            .AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights();

        ConfigureServices(builder.Services);

        builder.Build().Run();
    }

// register any services you want to use in your functions here, so that this method can be called
// from the ASP.NET Core integration extension method that the TestServer needs to configure the Test Host's container
    public static void ConfigureServices(IServiceCollection services)
    {
        // register any services you want to use in your functions here
        services.AddSingleton<IDummyService, DummyService>();
    }
}