using System.Net.Http.Json;
using System.Text;
using ExampleFunction;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;

namespace AzureFunctionsDotNetWorkerTestServer.Tests;

[Trait("Suite", "Harness")]
[Trait("Speed", "Fast")]
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
    public async Task Function_ThrowsWhenRouteParamTypeDoesNotMatch()
    {
        var client = factory.CreateFunctionsClient();
        var wrongTypeParameter = "abc";

        Task<HttpResponseMessage> Invocation() => client.GetAsync($"/api/greeter/{wrongTypeParameter}");
        await Assert.ThrowsAsync<InvalidOperationException>(Invocation);
    }
    
    [Fact]
    public async Task Function_CanGetByQueryParam()
    {
        var client = factory.CreateFunctionsClient();
        var testId = 5;

        var response = await client.GetAsync($"/api/greeterWithQuery?id={testId}");
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
    public async Task Function_CanPostWithRawJson()
    {
        var client = factory.CreateFunctionsClient();

        var requestContent = new StringContent("{\"name\":\"TestUser\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/greeter", requestContent);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Welcome to Azure Functions, TestUser!", content);
    }
    
    [Fact]
    public async Task Function_ThrowsOnInvalidRoute()
    {
        var client = factory.CreateFunctionsClient();

        Task<HttpResponseMessage> InvokeNonExistent() => client.GetAsync("/api/nonexistent");
        await Assert.ThrowsAsync<InvalidOperationException>(InvokeNonExistent);
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

[Trait("Suite", "Harness")]
[Trait("Speed", "Fast")]
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