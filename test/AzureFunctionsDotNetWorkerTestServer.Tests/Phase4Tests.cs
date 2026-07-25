using ExampleFunction;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

public class Phase4Tests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public async Task Function_CanInvokeStampMiddleware()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/greeter");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Stamp"));
    }
}