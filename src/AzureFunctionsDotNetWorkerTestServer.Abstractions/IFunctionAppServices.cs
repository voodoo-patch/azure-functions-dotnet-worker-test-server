using Microsoft.Extensions.DependencyInjection;

namespace AzureFunctionsDotNetWorkerTestServer.Abstractions;

public interface IFunctionAppServices
{
    static abstract void ConfigureServices(IServiceCollection services);
}
