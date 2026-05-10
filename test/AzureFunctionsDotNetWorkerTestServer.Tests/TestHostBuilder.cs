using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

public class TestHostBuilder
{
    
    public static WebApplication Build()
    {
        // 1. Use the real setup — gets you ALL internal services properly registered
        var builder = WebApplication.CreateBuilder([
            "--Functions:Worker:HostEndpoint=http://localhost:0/"  // satisfies config guard
        ]);
        builder.Services.AddFunctionsWorkerDefaults();  // or ConfigureFunctionsWorkerDefaults

        // 2. Remove ONLY the service that initiates the gRPC connection
        var hostedServiceDescriptor = builder.Services
            .FirstOrDefault(d => d.ImplementationType?.Name == "WorkerHostedService");
        if (hostedServiceDescriptor != null)
            builder.Services.Remove(hostedServiceDescriptor);

        // 3. Build — all internal services resolve correctly, no connection attempted
        return builder.Build();
    }
}