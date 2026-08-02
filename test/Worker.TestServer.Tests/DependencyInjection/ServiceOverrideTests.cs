using Azure.Functions.Worker.TestServer.Tests.TestInfrastructure;
using ExampleFunction;
using ExampleFunction.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Worker.TestServer.Tests.DependencyInjection;

[Trait("Suite", "DependencyInjection")]
public class ApplicationServiceRegistrationTests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public void Host_BuildsAndContainsProgramServices()
    {
        var dummyService = factory.Services.GetService<IDummyService>();
        Assert.NotNull(dummyService);
        Assert.IsType<DummyService>(dummyService);
        Assert.Equal("Hello from DummyService!", dummyService.GetData());
    }
}

[Trait("Suite", "DependencyInjection")]
public class ServiceOverrideTests(FunctionAppFactoryWithMockServices factory)
    : IClassFixture<FunctionAppFactoryWithMockServices>
{
    [Fact]
    public void Host_ServicesCanBeMocked()
    {
        _ = factory.CreateClient(); // trigger the host to build with the new services configuration

        var dummyService = factory.Services.GetService<IDummyService>();
        Assert.NotNull(dummyService);
        Assert.IsType<FunctionAppFactoryWithMockServices.MockDummyService>(dummyService);
    }
}
