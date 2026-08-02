using ExampleFunction;
using ExampleFunction.Http;

namespace Azure.Functions.Worker.TestServer.Tests.TestInfrastructure;

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
