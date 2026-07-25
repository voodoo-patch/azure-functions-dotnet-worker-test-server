using System.Net.Http.Json;
using ExampleFunction;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

public class Phase3Tests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public async Task Function_CanGet()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/greeter");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Welcome to Azure Functions!", content);
    }
    
    [Fact]
    public async Task Function_CanGetById()
    {
        var client = factory.CreateFunctionsClient();
        var testId = 5;

        var response = await client.GetAsync($"/api/greeter/{testId}");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal($"Welcome to Azure Functions! Id: {testId}", content);
    }
    
    [Fact]
    public async Task Function_CanPostWithJson()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.PostAsJsonAsync("/api/greeter", new Greeter.GreeterInput("TestUser"));
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Welcome to Azure Functions, TestUser!", content);
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

public class Phase3TestsWithCustomizations(FunctionAppFactoryWithMockServices factory)
    : IClassFixture<FunctionAppFactoryWithMockServices>
{
    [Fact]
    public async Task Function_CanGetWithMockedService()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/greeterWithService");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal($"Welcome to Azure Functions! Data: Mock data", content);
    }
}