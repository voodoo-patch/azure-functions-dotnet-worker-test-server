using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Worker.TestServer.Abstractions;

public interface IFunctionAppStartup
{
    static abstract void ConfigureServices(IServiceCollection services);
    static virtual void ConfigureWorker(IFunctionsWorkerApplicationBuilder builder) { }
}
