using ExampleFunction;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

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
