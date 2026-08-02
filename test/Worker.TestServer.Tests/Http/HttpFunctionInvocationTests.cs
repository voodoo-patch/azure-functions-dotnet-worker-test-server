using System.Net.Http.Json;
using System.Text;
using Azure.Functions.Worker.TestServer.Tests.TestInfrastructure;
using ExampleFunction;
using ExampleFunction.Http;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;

namespace Azure.Functions.Worker.TestServer.Tests.Http;

[Trait("Suite", "Http")]
public class HttpFunctionInvocationTests(FunctionAppFactory<Program> factory)
    : IClassFixture<FunctionAppFactory<Program>>
{
    [Fact]
    public async Task Function_CanGet()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/http-trigger-examples");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Welcome to Azure Functions!", content);
    }

    [Fact]
    public async Task Function_CanGetById()
    {
        var client = factory.CreateFunctionsClient();
        var testId = 5;

        var response = await client.GetAsync($"/api/http-trigger-examples/{testId}");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal($"Welcome to Azure Functions! Id: {testId}", content);
    }

    [Fact]
    public async Task Function_ThrowsWhenRouteParamTypeDoesNotMatch()
    {
        var client = factory.CreateFunctionsClient();
        var wrongTypeParameter = "abc";

        Task<HttpResponseMessage> Invocation() => client.GetAsync($"/api/http-trigger-examples/{wrongTypeParameter}");
        await Assert.ThrowsAsync<InvalidOperationException>(Invocation);
    }

    [Fact]
    public async Task Function_CanGetByQueryParam()
    {
        var client = factory.CreateFunctionsClient();
        var testId = 5;

        var response = await client.GetAsync($"/api/http-trigger-examples/by-query?id={testId}");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal($"Welcome to Azure Functions! Id: {testId}", content);
    }

    [Fact]
    public async Task Function_CanPostWithJson()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.PostAsJsonAsync("/api/http-trigger-examples", new HttpTriggerExamples.HttpTriggerExampleInput("TestUser"));
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Welcome to Azure Functions, TestUser!", content);
    }

    [Fact]
    public async Task Function_CanPostWithRawJson()
    {
        var client = factory.CreateFunctionsClient();

        var requestContent = new StringContent("{\"name\":\"TestUser\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/http-trigger-examples", requestContent);
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

[Trait("Suite", "Http")]
public class HttpFunctionInvocationWithServiceOverridesTests(FunctionAppFactoryWithMockServices factory)
    : IClassFixture<FunctionAppFactoryWithMockServices>
{
    [Fact]
    public async Task Function_CanGetWithMockedService()
    {
        var client = factory.CreateFunctionsClient();

        var response = await client.GetAsync("/api/http-trigger-examples/with-service");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal($"Welcome to Azure Functions! Data: Mock data", content);
    }
}
