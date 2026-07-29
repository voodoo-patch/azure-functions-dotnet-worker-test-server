using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzureFunctionsDotNetWorkerTestServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ReplaceService<TService, TReplacement>(this IServiceCollection services,
        ServiceLifetime? lifetime = null)
        where TService : class
        where TReplacement : class, TService
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TService));
        services.RemoveAll<TService>();

        var replacement = new ServiceDescriptor(typeof(TService), typeof(TReplacement),
            lifetime ?? descriptor?.Lifetime ?? ServiceLifetime.Transient);
        services.Add(replacement);

        return services;
    }
}
