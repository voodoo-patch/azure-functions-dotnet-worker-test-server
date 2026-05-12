using ExampleFunction;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

public class Phase3Tests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public async Task Function_CanBeCalledAndUsesRegisteredServices()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/Greeter");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Welcome to Azure Functions!", content);
    }

    [Fact]
    public void ProgramsAssemblyContains_GeneratedFunctionMetadataProvider()
    {
        var types = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(IFunctionMetadataProvider).IsAssignableFrom(t))
            .ToList();
        
        Assert.NotEmpty(types);
    }
}