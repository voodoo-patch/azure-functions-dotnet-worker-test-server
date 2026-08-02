using ExampleFunction;
using ExampleFunction.Http;

namespace AzureFunctionsDotNetWorkerTestServer.Tests.TestInfrastructure;

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
