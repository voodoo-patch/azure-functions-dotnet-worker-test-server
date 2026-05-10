using ExampleFunction;
using Microsoft.Extensions.DependencyInjection;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

public class Phase2TestsWithNoCustomizations(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public void Host_BuildsAndContainsProgramServices()
    {
        var dummyService = factory.Services.GetService<IDummyService>();
        Assert.NotNull(dummyService);
        Assert.IsType<DummyService>(dummyService);
    }
}

public class FunctionAppFactoryWithMockServices()
    : FunctionAppFactory<Program>(services => services.ReplaceService<IDummyService, MockDummyService>())
{
    public class MockDummyService : IDummyService
    {
        public string GetData()
        {
            return "Mock data";
        }
    }
}

public class Phase2TestsWithCustomizations(FunctionAppFactoryWithMockServices factory)
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